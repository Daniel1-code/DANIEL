using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Surface de maconnerie des murs dont le materiau porteur (couche structure) n'est PAS du
    /// beton. Mesure : aire reelle des faces laterales (HostObjectUtils.GetSideFaces), percees
    /// par Revit de toutes les ouvertures et coupees par les elements joints ; puis regle de
    /// deduction des ouvertures (extraction.json) appliquee par OpeningDeductionRule.
    /// </summary>
    internal sealed class MasonryExtractor
    {
        public const string Name = "Maçonnerie";

        private readonly ElementDescriber describer;
        private readonly MaterialClassifier materials;
        private readonly ExtractionSettings settings;

        private readonly IProgressSink progress;

        public MasonryExtractor(ElementDescriber describer, MaterialClassifier materials, ExtractionSettings settings, IProgressSink progress)
        {
            this.describer = describer;
            this.materials = materials;
            this.settings = settings;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;
            List<Wall> walls = StackedWalls.Expand(doc, new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_Walls)
                .WhereElementIsNotElementType()
                .OfClass(typeof(Wall))).OfType<Wall>().ToList();

            var done = new HashSet<long>();
            int index = 0;
            foreach (Wall wall in walls)
            {
                progress.Step("Maçonnerie", ++index, walls.Count);
                if (!done.Add(wall.Id.Value))
                {
                    continue; // membre de mur empile deja traite via son parent
                }
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(wall, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (wall.WallType.Kind != WallKind.Basic || wall.IsStackedWall)
                {
                    continue;
                }
                try
                {
                    ExtractWall(wall, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC",
                        "Surface illisible pour le mur " + wall.Id.Value + " : " + ex.Message, wall.Id.Value);
                }
            }
        }

        private void ExtractWall(Wall wall, ExtractionReport report)
        {
            Document doc = describer.Document;
            ElementId coreMaterial = CoreMaterial(wall.WallType);
            if (materials.ConcreteReason(coreMaterial) != null)
            {
                return; // voile beton : son volume est metre par ConcreteExtractor
            }

            double exterior = SideArea(wall, ShellLayerType.Exterior);
            double interior = SideArea(wall, ShellLayerType.Interior);
            if (exterior <= 0 && interior <= 0)
            {
                report.Note(IssueSeverity.Warning, "MUR_SANS_FACE",
                    "Mur " + wall.Id.Value + " sans face latérale lisible : surface non mesurée.", wall.Id.Value);
                return;
            }

            double faceArea;
            string faceLabel;
            switch (settings.MasonryFace)
            {
                case MasonryMeasureFace.Exterieure: faceArea = exterior; faceLabel = "face extérieure"; break;
                case MasonryMeasureFace.Interieure: faceArea = interior; faceLabel = "face intérieure"; break;
                default:
                    if (exterior > 0 && interior > 0)
                    {
                        faceArea = (exterior + interior) / 2.0;
                        faceLabel = "moyenne des faces extérieure et intérieure";
                    }
                    else
                    {
                        faceArea = Math.Max(exterior, interior);
                        faceLabel = "seule face lisible";
                    }
                    break;
            }

            List<WallOpening> openings = wall.FindInserts(true, false, true, true)
                .Select(id => Opening(doc.GetElement(id)))
                .Where(o => o != null)
                .ToList();

            DeductionResult deduction = OpeningDeductionRule.Apply(faceArea, openings, settings);

            ElementQuantity q = describer.Describe(wall);
            q.Kind = QuantityKinds.Masonry;
            q.Unit = BoqUnit.SquareMeter;
            q.Value = deduction.AreaM2;
            q.Material = describer.Name(coreMaterial);
            q.MethodCode = "SIDE_FACE_AREA_OPENING_RULE";
            q.MethodDescription = "Aire réelle de la " + faceLabel + " (percée par Revit), puis " + deduction.Description;
            q.Reliability = deduction.Reliability;
            q.Assumption = deduction.Assumption;

            var fr = CultureInfo.InvariantCulture;
            q.Parameters["EpaisseurMm"] = Math.Round(RevitUnits.ToMillimeters(wall.Width)).ToString(fr);
            q.Parameters["SurfaceFacePerceeM2"] = faceArea.ToString("0.####", fr);
            q.Parameters["OuverturesDeduitesM2"] = deduction.OpeningsDeductedM2.ToString("0.####", fr);
            q.Parameters["OuverturesRajouteesM2"] = deduction.OpeningsKeptM2.ToString("0.####", fr);
            q.Parameters["NbOuvertures"] = openings.Count.ToString(fr);
            q.Parameters["Structurel"] = (wall.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() ?? 0) == 1 ? "Oui" : "Non";
            report.Quantities.Add(q);
        }

        private static ElementId CoreMaterial(WallType type)
        {
            CompoundStructure cs = type.GetCompoundStructure();
            if (cs == null)
            {
                return ElementId.InvalidElementId;
            }
            int index = cs.StructuralMaterialIndex;
            if (index < 0)
            {
                IList<CompoundStructureLayer> layers = cs.GetLayers();
                index = layers.ToList().FindIndex(l => l.Function == MaterialFunctionAssignment.Structure);
                if (index < 0 && layers.Count > 0)
                {
                    index = layers.Select((l, i) => new { l.Width, i }).OrderByDescending(x => x.Width).First().i;
                }
            }
            return index >= 0 ? cs.GetMaterialId(index) : ElementId.InvalidElementId;
        }

        private static double SideArea(Wall wall, ShellLayerType side)
        {
            double total = 0;
            foreach (Reference r in HostObjectUtils.GetSideFaces(wall, side))
            {
                if (wall.GetGeometryObjectFromReference(r) is Face face)
                {
                    total += face.Area;
                }
            }
            return RevitUnits.ToSquareMeters(total);
        }

        /// <summary>Surface de la baie d'une insertion ; null si illisible (jamais devinee).</summary>
        private WallOpening Opening(Element insert)
        {
            if (insert == null)
            {
                return null;
            }
            var o = new WallOpening { ElementId = insert.Id.Value, Description = insert.Name };

            if (insert is FamilyInstance fi)
            {
                // Baie brute d'abord (c'est le trou dans le mur), dimensions nominales ensuite.
                double? w = describer.ReadLengthMm(fi, BuiltInParameter.FAMILY_ROUGH_WIDTH_PARAM, BuiltInParameter.DOOR_WIDTH,
                    BuiltInParameter.WINDOW_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.GENERIC_WIDTH);
                double? h = describer.ReadLengthMm(fi, BuiltInParameter.FAMILY_ROUGH_HEIGHT_PARAM, BuiltInParameter.DOOR_HEIGHT,
                    BuiltInParameter.WINDOW_HEIGHT, BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.GENERIC_HEIGHT);
                if (w.HasValue && h.HasValue)
                {
                    o.AreaM2 = w.Value / 1000.0 * h.Value / 1000.0;
                }
            }
            else if (insert is Opening opening && opening.IsRectBoundary)
            {
                IList<XYZ> rect = opening.BoundaryRect;
                if (rect != null && rect.Count == 2)
                {
                    XYZ d = rect[1] - rect[0];
                    double width = Math.Sqrt(d.X * d.X + d.Y * d.Y);
                    o.AreaM2 = RevitUnits.ToSquareMeters(width * Math.Abs(d.Z));
                }
            }
            else if (insert is Wall embedded)
            {
                Parameter p = embedded.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                if (p != null && p.HasValue)
                {
                    o.AreaM2 = RevitUnits.ToSquareMeters(p.AsDouble());
                }
            }
            return o;
        }
    }
}
