using System;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Terrassements generaux (lecture seule) :
    ///  - decapage de terre vegetale (m²) : sols du niveau le plus bas, surface elargie du debord ;
    ///  - fouille en pleine masse (m³) : sols situes SOUS le terrain naturel (sous-sols), du terrain
    ///    naturel au dessous du sol, sur sa surface.
    /// Remblai et evacuation sont ensuite deduits par EarthworkBalance (Core).
    /// </summary>
    internal sealed class GeneralEarthworkExtractor
    {
        public const string Name = "Terrassements généraux";

        private readonly ElementDescriber describer;
        private readonly ExtractionSettings settings;

        public GeneralEarthworkExtractor(ElementDescriber describer, ExtractionSettings settings)
        {
            this.describer = describer;
            this.settings = settings ?? new ExtractionSettings();
        }

        public void Extract(ExtractionReport report)
        {
            GeneralEarthworkSettings s = settings.GeneralEarthworks ?? new GeneralEarthworkSettings();
            Document doc = describer.Document;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            Level lowest = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault();
            double? ground = GroundElevation(doc);
            int bulk = 0;
            foreach (Floor floor in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().OfType<Floor>())
            {
                if (describer.ShouldSkip(floor, out _)) continue;
                double area = Param(floor, BuiltInParameter.HOST_AREA_COMPUTED, RevitUnits.ToSquareMeters);
                if (area <= 0) continue;

                if (s.Stripping && lowest != null && floor.LevelId == lowest.Id)
                {
                    report.Count(report.ElementsScanned, Name);
                    double perimeter = Param(floor, BuiltInParameter.HOST_PERIMETER_COMPUTED, RevitUnits.ToMeters);
                    double stripped = EarthworkBalance.EnlargedArea(area, perimeter, s.StrippingMarginM);
                    ElementQuantity q = describer.Describe(floor);
                    q.Kind = QuantityKinds.Stripping;
                    q.Unit = BoqUnit.SquareMeter;
                    q.Value = Math.Round(stripped, 4);
                    q.Reliability = Reliability.Estimated;
                    q.MethodCode = "STRIPPING_FOOTPRINT";
                    q.MethodDescription = "Surface du sol du niveau le plus bas (" + area.ToString("0.##", fr) + " m²), élargie de " +
                                          s.StrippingMarginM.ToString("0.##", fr) + " m sur son périmètre (" + perimeter.ToString("0.##", fr) + " m).";
                    q.Assumption = "ESTIMÉ : emprise du décapage = sols du niveau « " + lowest.Name + " » + débord de " + s.StrippingMarginM.ToString("0.##", fr) +
                                   " m (plusieurs sols voisins : débords comptés entre eux, légère surestimation).";
                    report.Quantities.Add(q);
                }

                if (s.BulkExcavation && ground.HasValue)
                {
                    BoundingBoxXYZ box = floor.get_BoundingBox(null);
                    if (box == null || box.Max.Z >= ground.Value - 1e-6) continue;
                    double depth = RevitUnits.ToMeters(ground.Value - box.Min.Z);
                    if (depth <= 0) continue;
                    report.Count(report.ElementsScanned, Name);
                    ElementQuantity q = describer.Describe(floor);
                    q.Kind = QuantityKinds.BulkExcavation;
                    q.Unit = BoqUnit.CubicMeter;
                    q.Value = Math.Round(area * depth, 4);
                    q.Reliability = Reliability.Estimated;
                    q.MethodCode = "BULK_FROM_GROUND_TO_SLAB_BOTTOM";
                    q.MethodDescription = "Surface du sol × profondeur du terrain naturel au-dessous du sol (" + depth.ToString("0.00", fr) + " m).";
                    q.Assumption = "ESTIMÉ : pleine masse sur l'emprise du sol, parois verticales, depuis le terrain naturel « " +
                                   settings.Excavation?.NaturalGroundLevel + " ».";
                    report.Quantities.Add(q);
                    bulk++;
                }
            }
            if (bulk > 0 && report.Quantities.Any(q => q.Kind == QuantityKinds.Excavation))
            {
                report.Notes.Add(new BoqIssue(IssueSeverity.Warning, "PLEINE_MASSE_ET_FOUILLES",
                    "Fouille en pleine masse (sous-sol) ET fouilles de fondations : les fouilles des fondations sous le sous-sol sont comptées depuis le " +
                    "terrain naturel. Vérifier le double compte (les retirer ou les réduire en forfait)."));
            }
        }

        private double? GroundElevation(Document doc)
        {
            string name = settings.Excavation?.NaturalGroundLevel;
            if (string.IsNullOrWhiteSpace(name)) return null;
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            return level == null ? (double?)null : level.ProjectElevation + UnitUtils.ConvertToInternalUnits(settings.Excavation.OffsetM, UnitTypeId.Meters);
        }

        private static double Param(Element e, BuiltInParameter bip, Func<double, double> convert)
        {
            Parameter p = e.get_Parameter(bip);
            return p != null && p.HasValue && p.StorageType == StorageType.Double && p.AsDouble() > 0 ? convert(p.AsDouble()) : 0;
        }
    }
}
