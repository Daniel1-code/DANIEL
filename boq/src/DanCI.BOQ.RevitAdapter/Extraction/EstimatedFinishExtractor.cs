using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Finitions ESTIMEES (lecture seule) quand la maquette ne les modelise pas, comme dans un BOQ
    /// de projet reel :
    /// - murs : face interieure et face exterieure (HostObjectUtils.GetSideFaces, ouvertures percees
    ///   par Revit) -> enduit / crepissage au mortier de ciment + peinture ; mur de facade = fonction
    ///   « Exterieur » du type ;
    /// - ossature en beton (poteaux, poutres) : faces laterales et sous-faces non masquees par un mur,
    ///   un sol ou un autre element -> enduit + peinture ;
    /// - pieces : surface -> carrelage de sol, enduit et peinture sous plafond ; pieces humides ->
    ///   faience (perimetre moins les portes) x hauteur.
    /// Chaque quantite est rattachee a son element et a son niveau, marquee estimee avec l'hypothese.
    /// Les doublons avec les finitions modelisees sont retires ensuite (EstimatedFinishes.Reconcile).
    /// </summary>
    internal sealed class EstimatedFinishExtractor
    {
        public const string Name = "Finitions estimées";
        private const double ProbeFeet = 0.05; // ~15 mm devant la face

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        private readonly ElementDescriber describer;
        private readonly MaterialClassifier materials;
        private readonly EstimatedFinishSettings s;
        private readonly SecondWorkSettings secondWork;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public EstimatedFinishExtractor(ElementDescriber describer, MaterialClassifier materials, EstimatedFinishSettings settings,
            SecondWorkSettings secondWork, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.materials = materials;
            s = settings ?? new EstimatedFinishSettings();
            this.secondWork = secondWork ?? new SecondWorkSettings();
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            if (!s.Enabled) return;
            if (s.WallPlaster || (s.Paint && !s.WallPlaster)) Walls(report);
            if (s.FramePlaster) Frame(report);
            Rooms(report);
        }

        private bool SkipLevel(ElementQuantity q) => EstimatedFinishSettings.Matches(s.NoFinishLevels, q.Level);

        private bool Demolished(Element e) => describer.PhaseAllows(e, out string status, out _) && status == PhaseStatus.Demolished;

        // ---------------------------------------------------------------- murs
        private void Walls(ExtractionReport report)
        {
            Document doc = describer.Document;
            List<Element> walls = StackedWalls.Expand(doc, new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()).ToList();
            var done = new HashSet<long>();
            int index = 0;
            foreach (Element e in walls)
            {
                progress.Step(Name, ++index, walls.Count);
                if (!(e is Wall w) || !done.Add(w.Id.Value) || w.IsStackedWall || w.WallType == null || w.WallType.Kind != WallKind.Basic) continue;
                if (describer.ShouldSkip(w, out _) || Demolished(w)) continue;
                string typeName = w.WallType.Name;
                if (EstimatedFinishSettings.Matches(s.NoPlasterWallTypes, typeName) ||
                    EstimatedFinishSettings.Matches(s.NoPlasterWallTypes, w.WallType.FamilyName)) continue;
                ElementQuantity probe = describer.Describe(w);
                if (SkipLevel(probe) || IsVrd(probe)) continue;
                WallFunction function = Function(w.WallType);
                if (function == WallFunction.Foundation) continue;
                report.Count(report.ElementsScanned, Name);
                try
                {
                    double ext = SideArea(w, ShellLayerType.Exterior), inn = SideArea(w, ShellLayerType.Interior);
                    bool facade = function == WallFunction.Exterior;
                    // Mur de soutenement : la face cote terre n'est ni enduite ni peinte.
                    if (function != WallFunction.Retaining)
                    {
                        AddFace(report, w, facade ? EstimatedFinishes.Exterior : EstimatedFinishes.Interior, ext,
                            facade ? "face extérieure du mur de façade" : "face 1 du mur intérieur");
                    }
                    AddFace(report, w, EstimatedFinishes.Interior, inn, facade || function == WallFunction.Retaining ? "face intérieure du mur" : "face 2 du mur intérieur");
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Warning, "FINITION_ESTIMEE_ECHEC", "Faces du mur " + w.Id.Value + " illisibles : " + ex.Message, w.Id.Value);
                }
            }
        }

        private static WallFunction Function(WallType type)
        {
            Parameter p = type.get_Parameter(BuiltInParameter.FUNCTION_PARAM);
            return p != null && p.HasValue && p.StorageType == StorageType.Integer ? (WallFunction)p.AsInteger() : WallFunction.Interior;
        }

        private static double SideArea(Wall w, ShellLayerType side)
        {
            double area = 0;
            foreach (Reference r in HostObjectUtils.GetSideFaces(w, side))
            {
                if (w.GetGeometryObjectFromReference(r) is Face f) area += f.Area;
            }
            return RevitUnits.ToSquareMeters(area);
        }

        private void AddFace(ExtractionReport report, Element e, string face, double areaM2, string what)
        {
            if (areaM2 <= 0.01) return;
            bool exterior = face == EstimatedFinishes.Exterior;
            string a = areaM2.ToString("0.00", Fr);
            if (s.WallPlaster)
            {
                Add(report, e, QuantityKinds.Plaster, face, areaM2, exterior ? "Crépissage extérieur (estimé)" : "Enduit intérieur (estimé)",
                    "WALL_FACE_AREA_" + face.ToUpperInvariant(),
                    "Surface de la " + what + " : " + a + " m² (face calculée par Revit, ouvertures déduites).",
                    "ESTIMÉ : enduit au mortier de ciment non modélisé ; surface = " + what + ". Faïence éventuelle non déduite.");
            }
            if (s.Paint && (!exterior || s.ExteriorPaint))
            {
                Add(report, e, QuantityKinds.Paint, face, areaM2, exterior ? "Peinture de façade (estimée)" : "Peinture intérieure (estimée)",
                    "WALL_FACE_AREA_" + face.ToUpperInvariant(),
                    "Surface de la " + what + " : " + a + " m² (face calculée par Revit, ouvertures déduites).",
                    "ESTIMÉ : aucun matériau peinture modélisé ; peinture sur la surface enduite (" + what + ").");
            }
        }

        // ---------------------------------------------------------------- ossature
        private void Frame(ExtractionReport report)
        {
            Document doc = describer.Document;
            var elements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns, BuiltInCategory.OST_StructuralFraming }))
                .WhereElementIsNotElementType().ToList();
            int index = 0;
            foreach (Element e in elements)
            {
                progress.Step(Name + " (ossature)", ++index, elements.Count);
                if (describer.ShouldSkip(e, out _) || Demolished(e)) continue;
                if (!e.GetMaterialIds(false).Any(id => materials.ConcreteReason(id) != null)) continue; // acier, bois : pas d'enduit
                ElementQuantity probe = describer.Describe(e);
                if (SkipLevel(probe)) continue;
                report.Count(report.ElementsScanned, Name);
                bool beam = ElementDescriber.CategoryName(e.Category) == nameof(BuiltInCategory.OST_StructuralFraming);
                double area = 0;
                int hidden = 0;
                try
                {
                    foreach (Solid solid in solids.Of(e))
                    {
                        foreach (Face f in solid.Faces)
                        {
                            BoundingBoxUV bb = f.GetBoundingBox();
                            UV mid = new UV((bb.Min.U + bb.Max.U) / 2, (bb.Min.V + bb.Max.V) / 2);
                            XYZ n = f.ComputeNormal(mid);
                            bool side = Math.Abs(n.Z) < 0.2, under = n.Z < -0.5;
                            if (!(side || (beam && under))) continue;
                            XYZ p = f.Evaluate(mid) + n.Normalize() * ProbeFeet;
                            if (Covered(doc, e, p)) { hidden++; continue; }
                            area += f.Area;
                        }
                    }
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Warning, "FINITION_ESTIMEE_ECHEC", "Faces de l'élément " + e.Id.Value + " illisibles : " + ex.Message, e.Id.Value);
                    continue;
                }
                double m2 = RevitUnits.ToSquareMeters(area);
                if (m2 <= 0.01) continue;
                string what = (beam ? "faces latérales et sous-face apparentes de la poutre" : "faces apparentes du poteau") +
                              (hidden > 0 ? " (" + hidden + " face(s) contre un mur, un sol ou un autre élément non comptée(s))" : string.Empty);
                string a = m2.ToString("0.00", Fr);
                if (s.WallPlaster || s.FramePlaster)
                {
                    Add(report, e, QuantityKinds.Plaster, EstimatedFinishes.Interior, m2, "Enduit intérieur (estimé)", "FRAME_EXPOSED_FACES",
                        "Surface des " + what + " : " + a + " m².",
                        "ESTIMÉ : enduit non modélisé ; faces de l'ossature en béton non masquées (contrôle au centre de chaque face), comptées en intérieur.");
                }
                if (s.Paint)
                {
                    Add(report, e, QuantityKinds.Paint, EstimatedFinishes.Interior, m2, "Peinture intérieure (estimée)", "FRAME_EXPOSED_FACES",
                        "Surface des " + what + " : " + a + " m².",
                        "ESTIMÉ : aucun matériau peinture modélisé ; peinture sur les faces enduites de l'ossature.");
                }
            }
        }

        /// <summary>Le point devant la face est-il dans un autre element (mur, sol, poteau, poutre, toit) ?</summary>
        private bool Covered(Document doc, Element self, XYZ p)
        {
            var near = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(new[]
                {
                    BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_Columns,
                    BuiltInCategory.OST_StructuralFraming, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_StructuralFoundation
                }))
                .WherePasses(new BoundingBoxContainsPointFilter(p))
                .WhereElementIsNotElementType()
                .Excluding(new List<ElementId> { self.Id });
            Line probe = Line.CreateBound(p - XYZ.BasisZ * 0.01, p + XYZ.BasisZ * 0.01);
            var options = new SolidCurveIntersectionOptions { ResultType = SolidCurveIntersectionMode.CurveSegmentsInside };
            foreach (Element other in near)
            {
                foreach (Solid solid in solids.Of(other))
                {
                    if (solid.IntersectWithCurve(probe, options).SegmentCount > 0) return true;
                }
            }
            return false;
        }

        // ---------------------------------------------------------------- pieces
        private void Rooms(ExtractionReport report)
        {
            Document doc = describer.Document;
            List<Room> rooms = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType()
                .OfType<Room>().Where(r => r.Area > 0).ToList();
            bool anyRoomWork = s.FloorTiling || s.Ceilings || s.WallTiling;
            if (rooms.Count == 0)
            {
                if (anyRoomWork && new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Walls).WhereElementIsNotElementType().Any())
                {
                    report.Notes.Add(new BoqIssue(IssueSeverity.Warning, "PIECES_ABSENTES",
                        "Aucune pièce Revit placée" + (string.IsNullOrEmpty(report.CurrentSource) ? string.Empty : " [" + report.CurrentSource + "]") +
                        " : carrelage de sol, plinthes, enduit et peinture sous plafond et faïence ne sont PAS estimés " +
                        "(aucune quantité inventée). Placez des pièces (Architecture → Pièce) puis relancez l'analyse.") { LinkInstanceId = report.CurrentLinkInstance });
                }
                return;
            }
            var doorWidths = new Dictionary<long, List<double>>();
            if (s.WallTiling)
            {
                foreach (FamilyInstance door in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>())
                {
                    double width = SecondWorkExtractor.DoorWidth(door);
                    if (width <= 0) continue;
                    foreach (Room room in new[] { door.FromRoom, door.ToRoom }.Where(r => r != null).Distinct())
                    {
                        if (!doorWidths.TryGetValue(room.Id.Value, out List<double> list)) doorWidths[room.Id.Value] = list = new List<double>();
                        list.Add(width);
                    }
                }
            }
            int index = 0;
            foreach (Room room in rooms)
            {
                progress.Step(Name + " (pièces)", ++index, rooms.Count);
                if (describer.ShouldSkip(room, out _)) continue;
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString() ?? string.Empty;
                ElementQuantity probe = describer.Describe(room);
                if (SkipLevel(probe) || EstimatedFinishSettings.Matches(s.NoTilingRooms, name)) continue;
                report.Count(report.ElementsScanned, Name);
                double area = RevitUnits.ToSquareMeters(room.Area);
                string a = area.ToString("0.00", Fr);
                bool outdoor = secondWork.SkirtingExcludedRooms != null && !secondWork.SkirtingExcludedRooms.IsEmpty && secondWork.SkirtingExcludedRooms.IsMatch(name);
                if (s.FloorTiling)
                {
                    Add(report, room, QuantityKinds.FloorFinish, EstimatedFinishes.Floor, area, "Carrelage de sol (estimé)", "ROOM_AREA",
                        "Surface nette de la pièce « " + name + " » : " + a + " m² (calculée par Revit).",
                        "ESTIMÉ : revêtement de sol non modélisé ; carrelage sur toute la surface de la pièce.", name);
                }
                if (s.Ceilings && !outdoor)
                {
                    Add(report, room, QuantityKinds.Plaster, EstimatedFinishes.Ceiling, area, "Enduit sous plafond (estimé)", "ROOM_AREA_CEILING",
                        "Plafond de la pièce « " + name + " » = surface de la pièce : " + a + " m².",
                        "ESTIMÉ : sous-face de dalle enduite, surface = surface de la pièce (retombées de poutres comptées avec l'ossature).", name);
                    if (s.Paint)
                    {
                        Add(report, room, QuantityKinds.Paint, EstimatedFinishes.Ceiling, area, "Peinture sous plafond (estimée)", "ROOM_AREA_CEILING",
                            "Plafond de la pièce « " + name + " » = surface de la pièce : " + a + " m².",
                            "ESTIMÉ : aucun matériau peinture modélisé ; peinture sous plafond sur la surface de la pièce.", name);
                    }
                }
                if (s.WallTiling && EstimatedFinishSettings.Matches(s.WetRooms, name))
                {
                    double perimeter = RevitUnits.ToMeters(room.Perimeter);
                    doorWidths.TryGetValue(room.Id.Value, out List<double> widths);
                    double tiles = EstimatedFinishes.WallTilingArea(perimeter, widths, s.WallTilingHeightM);
                    if (tiles > 0.01)
                    {
                        Add(report, room, QuantityKinds.FloorFinish, EstimatedFinishes.WallTiles, tiles, "Faïence murale (estimée)", "ROOM_PERIMETER_X_HEIGHT",
                            "Faïence de la pièce humide « " + name + " » : (périmètre " + perimeter.ToString("0.00", Fr) + " m − " + (widths?.Count ?? 0) +
                            " porte(s)) × " + s.WallTilingHeightM.ToString("0.00", Fr) + " m = " + tiles.ToString("0.00", Fr) + " m².",
                            "ESTIMÉ : faïence non modélisée ; hauteur " + s.WallTilingHeightM.ToString("0.00", Fr) +
                            " m (finitionsEstimees.faienceHauteurM). Fenêtres non déduites ; peinture des murs non diminuée d'autant.", name);
                    }
                }
            }
        }

        private static bool IsVrd(ElementQuantity q) =>
            q.Parameters != null && q.Parameters.TryGetValue(VrdRules.Parameter, out string v) && v == VrdRules.Yes;

        private void Add(ExtractionReport report, Element e, string kind, string face, double areaM2, string material, string method,
            string description, string assumption, string roomName = null)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = kind;
            q.Unit = BoqUnit.SquareMeter;
            q.Value = Math.Round(areaM2, 4);
            q.Material = material;
            q.MethodCode = method;
            q.MethodDescription = description;
            q.Reliability = Reliability.Estimated;
            q.Assumption = assumption;
            if (roomName != null && string.IsNullOrEmpty(q.TypeName)) q.TypeName = roomName;
            q.Parameters[EstimatedFinishes.Parameter] = face;
            report.Quantities.Add(q);
        }
    }
}
