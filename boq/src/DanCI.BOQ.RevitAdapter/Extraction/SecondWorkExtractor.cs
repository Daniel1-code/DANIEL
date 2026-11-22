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
    /// Second oeuvre (lecture seule) : garde-corps (ml), gouttieres et bandeaux de rive (ml),
    /// faux-plafonds (m² par type), murs-rideaux (m² par type), plinthes (ml : perimetre de la
    /// piece moins la largeur des portes).
    /// </summary>
    internal sealed class SecondWorkExtractor
    {
        public const string Name = "Second œuvre";
        public const string CurtainParameter = "MurRideau";

        private readonly ElementDescriber describer;
        private readonly SecondWorkSettings settings;
        private readonly IProgressSink progress;

        private readonly VrdSettings vrd;

        private readonly ISet<long> elevatedFloors;

        public SecondWorkExtractor(ElementDescriber describer, SecondWorkSettings settings, IProgressSink progress, VrdSettings vrd = null,
            ISet<long> elevatedFloors = null)
        {
            this.elevatedFloors = elevatedFloors ?? new HashSet<long>();
            this.describer = describer;
            this.vrd = vrd ?? new VrdSettings();
            this.settings = settings ?? new SecondWorkSettings();
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            if (settings.Railings) Railings(report);
            if (settings.GuttersAndFascias)
            {
                Sweeps(report, BuiltInCategory.OST_Gutter, "gouttière");
                Sweeps(report, BuiltInCategory.OST_Fascia, "bandeau de rive");
            }
            if (settings.Ceilings) Ceilings(report);
            if (settings.CurtainWalls) CurtainWalls(report);
            if (settings.Skirtings) Skirtings(report);
            ExteriorFloors(report);
        }

        /// <summary>Sols exterieurs (voiries, parkings...) : surface par type, article VRD.</summary>
        private void ExteriorFloors(ExtractionReport report)
        {
            Document doc = describer.Document;
            foreach (Floor floor in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().OfType<Floor>())
            {
                string typeName = doc.GetElement(floor.GetTypeId())?.Name;
                if (!VrdRules.IsExteriorFloor("OST_Floors", typeName, vrd) || describer.ShouldSkip(floor, out _)) continue;
                if (elevatedFloors.Contains(floor.Id.Value)) continue; // dalle d'etage au nom de VRD : signalee par VrdRules
                report.Count(report.ElementsScanned, Name);
                double area = Area(floor);
                if (area <= 0) continue;
                AddArea(report, floor, area, "EXTERIOR_FLOOR_AREA", "Surface du sol extérieur (aire calculée par Revit).",
                    q => q.Parameters[VrdRules.Parameter] = VrdRules.Yes);
            }
        }

        private void Railings(ExtractionReport report)
        {
            foreach (Railing r in new FilteredElementCollector(describer.Document).OfClass(typeof(Railing)).WhereElementIsNotElementType().Cast<Railing>())
            {
                if (describer.ShouldSkip(r, out _)) continue;
                report.Count(report.ElementsScanned, Name);
                double length = r.GetPath().Sum(c => c.Length);
                string method = "Longueur du tracé du garde-corps (somme des segments).";
                if (length <= 0)
                {
                    length = ParameterLength(r);
                    method = "Paramètre Longueur du garde-corps.";
                }
                AddLength(report, r, length, "RAILING_PATH", method);
            }
        }

        private void Sweeps(ExtractionReport report, BuiltInCategory category, string label)
        {
            foreach (Element e in new FilteredElementCollector(describer.Document).OfCategory(category).WhereElementIsNotElementType())
            {
                if (describer.ShouldSkip(e, out _)) continue;
                report.Count(report.ElementsScanned, Name);
                double length = ParameterLength(e);
                if (length <= 0)
                {
                    report.Note(IssueSeverity.Warning, "LONGUEUR_ILLISIBLE", "Longueur illisible pour la " + label + " " + e.Id.Value + " : non comptée.", e.Id.Value);
                    continue;
                }
                AddLength(report, e, length, "SWEEP_LENGTH", "Paramètre Longueur de la " + label + " (Revit).");
            }
        }

        private void Ceilings(ExtractionReport report)
        {
            foreach (Element e in new FilteredElementCollector(describer.Document).OfCategory(BuiltInCategory.OST_Ceilings).WhereElementIsNotElementType())
            {
                if (describer.ShouldSkip(e, out _)) continue;
                report.Count(report.ElementsScanned, Name);
                double area = Area(e);
                if (area <= 0) continue;
                AddArea(report, e, area, "CEILING_AREA", "Surface du plafond (aire calculée par Revit).", null);
            }
        }

        private void CurtainWalls(ExtractionReport report)
        {
            foreach (Wall w in new FilteredElementCollector(describer.Document).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>())
            {
                if (w.WallType?.Kind != WallKind.Curtain || describer.ShouldSkip(w, out _)) continue;
                report.Count(report.ElementsScanned, Name);
                double area = Area(w);
                if (area <= 0) continue;
                AddArea(report, w, area, "CURTAIN_WALL_AREA", "Surface du mur-rideau (aire calculée par Revit), portes et fenêtres intégrées comprises.",
                    q => q.Parameters[CurtainParameter] = "oui");
            }
        }

        private void Skirtings(ExtractionReport report)
        {
            Document doc = describer.Document;
            // Largeur de chaque porte, cote piece de depart et piece d'arrivee.
            var doorWidths = new Dictionary<long, List<double>>();
            foreach (FamilyInstance door in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Doors).WhereElementIsNotElementType().OfType<FamilyInstance>())
            {
                double width = DoorWidth(door);
                if (width <= 0) continue;
                foreach (Room room in new[] { door.FromRoom, door.ToRoom }.Where(r => r != null).Distinct())
                {
                    if (!doorWidths.TryGetValue(room.Id.Value, out List<double> list)) doorWidths[room.Id.Value] = list = new List<double>();
                    list.Add(width);
                }
            }
            foreach (Room room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>())
            {
                if (room.Area <= 0 || describer.ShouldSkip(room, out _)) continue;
                string name = room.get_Parameter(BuiltInParameter.ROOM_NAME)?.AsString();
                if (settings.SkirtingExcludedRooms != null && !settings.SkirtingExcludedRooms.IsEmpty && settings.SkirtingExcludedRooms.IsMatch(name)) continue;
                report.Count(report.ElementsScanned, Name);
                double perimeter = RevitUnits.ToMeters(room.Perimeter);
                doorWidths.TryGetValue(room.Id.Value, out List<double> widths);
                double length = SkirtingRule.Length(perimeter, widths);
                if (length <= 0) continue;
                ElementQuantity q = describer.Describe(room);
                q.Kind = QuantityKinds.Length;
                q.Unit = BoqUnit.LinearMeter;
                q.Value = Math.Round(length, 3);
                q.TypeName = string.IsNullOrEmpty(q.TypeName) ? name : q.TypeName;
                q.MethodCode = "ROOM_PERIMETER_MINUS_DOORS";
                q.MethodDescription = "Périmètre de la pièce (" + perimeter.ToString("0.00", CultureInfo.InvariantCulture) + " m) moins la largeur des " +
                                      (widths?.Count ?? 0) + " porte(s) qui s'y ouvrent.";
                q.Parameters[SkirtingRule.Parameter] = SkirtingRule.Value;
                report.Quantities.Add(q);
            }
        }

        private void AddLength(ExtractionReport report, Element e, double lengthM, string method, string description)
        {
            if (lengthM <= 0) return;
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Length;
            q.Unit = BoqUnit.LinearMeter;
            q.Value = Math.Round(lengthM, 3);
            q.MethodCode = method;
            q.MethodDescription = description;
            report.Quantities.Add(q);
        }

        private void AddArea(ExtractionReport report, Element e, double areaM2, string method, string description, Action<ElementQuantity> tag)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Area;
            q.Unit = BoqUnit.SquareMeter;
            q.Value = Math.Round(areaM2, 4);
            q.MethodCode = method;
            q.MethodDescription = description;
            tag?.Invoke(q);
            report.Quantities.Add(q);
        }

        /// <summary>Longueur en metres (parametre Longueur de Revit), 0 si absent.</summary>
        private static double ParameterLength(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.CURVE_ELEM_LENGTH);
            if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
            {
                return RevitUnits.ToMeters(p.AsDouble());
            }
            foreach (string name in new[] { "Longueur", "Length" })
            {
                Parameter q = e.LookupParameter(name);
                if (q != null && q.HasValue && q.StorageType == StorageType.Double && q.AsDouble() > 0)
                {
                    return RevitUnits.ToMeters(q.AsDouble());
                }
            }
            return 0;
        }

        private static double Area(Element e)
        {
            Parameter p = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
            return p != null && p.HasValue ? RevitUnits.ToSquareMeters(p.AsDouble()) : 0;
        }

        private static double DoorWidth(FamilyInstance door)
        {
            foreach (BuiltInParameter bip in new[] { BuiltInParameter.DOOR_WIDTH, BuiltInParameter.FAMILY_WIDTH_PARAM })
            {
                Parameter p = door.get_Parameter(bip) ?? door.Symbol?.get_Parameter(bip);
                if (p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0)
                {
                    return RevitUnits.ToMeters(p.AsDouble());
                }
            }
            return 0;
        }
    }
}
