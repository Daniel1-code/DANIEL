using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Complements du gros oeuvre (lecture seule) :
    ///  - pieux : nombre (recepage des tetes) et longueur (forage), en plus de leur volume de beton ;
    ///  - dallages sur terre-plein : film polyane (m²) et herissonnage (m³, epaisseur hypothese) ;
    ///  - assemblages de charpente MODELISES (StructuralConnectionHandler) : masse d'acier mesuree.
    /// </summary>
    internal sealed class SubstructureExtractor
    {
        public const string Name = "Compléments gros œuvre";

        private readonly ElementDescriber describer;
        private readonly ExtractionSettings settings;
        private readonly IProgressSink progress;

        public SubstructureExtractor(ElementDescriber describer, ExtractionSettings settings, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings ?? new ExtractionSettings();
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            Piles(report);
            SlabsOnGrade(report);
            Connections(report);
        }

        // ------------------------------------------------------------------ pieux
        private void Piles(ExtractionReport report)
        {
            Document doc = describer.Document;
            var foundations = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType().OfType<FamilyInstance>().ToList();
            int index = 0;
            foreach (FamilyInstance fi in foundations)
            {
                progress.Step("Pieux", ++index, foundations.Count, 20);
                string family = fi.Symbol?.FamilyName;
                string type = fi.Symbol?.Name;
                if (!PileRules.IsPile("OST_StructuralFoundation", family, type, settings.Piles) || describer.ShouldSkip(fi, out _))
                {
                    continue;
                }
                report.Count(report.ElementsScanned, Name);
                ElementQuantity count = describer.Describe(fi);
                count.Kind = QuantityKinds.Count;
                count.Unit = BoqUnit.Each;
                count.Value = 1;
                count.MethodCode = "PILE_COUNT";
                count.MethodDescription = "Un pieu (recépage de la tête).";
                count.Parameters[PileRules.Parameter] = PileRules.Yes;
                report.Quantities.Add(count);

                BoundingBoxXYZ box = fi.get_BoundingBox(null);
                if (box == null)
                {
                    report.Note(IssueSeverity.Warning, "PIEU_SANS_GEOMETRIE", "Pieu " + fi.Id.Value + " sans géométrie : longueur non mesurée.", fi.Id.Value);
                    continue;
                }
                double lengthM = RevitUnits.ToMeters(box.Max.Z - box.Min.Z);
                ElementQuantity length = describer.Describe(fi);
                length.Kind = QuantityKinds.Length;
                length.Unit = BoqUnit.LinearMeter;
                length.Value = Math.Round(lengthM, 3);
                length.MethodCode = "PILE_BOUNDING_HEIGHT";
                length.MethodDescription = "Hauteur de l'enveloppe du pieu (cote haute − cote basse), géométrie Revit.";
                length.Parameters[PileRules.Parameter] = PileRules.Yes;
                report.Quantities.Add(length);
            }
        }

        // ------------------------------------------------------------------ dallages sur terre-plein
        private void SlabsOnGrade(ExtractionReport report)
        {
            SlabOnGradeSettings s = settings.SlabsOnGrade ?? new SlabOnGradeSettings();
            if (!s.Polythene && !s.Hardcore)
            {
                return;
            }
            Document doc = describer.Document;
            PatternList types = settings.Formwork?.SlabOnGradeTypes;
            ElementId lowest = s.LowestLevel
                ? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).FirstOrDefault()?.Id
                : null;
            foreach (Floor floor in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Floors).WhereElementIsNotElementType().OfType<Floor>())
            {
                string typeName = doc.GetElement(floor.GetTypeId())?.Name;
                bool byType = types != null && !types.IsEmpty && types.IsMatch(typeName);
                bool byLevel = !byType && lowest != null && floor.LevelId == lowest;
                if ((!byType && !byLevel) || describer.ShouldSkip(floor, out _))
                {
                    continue;
                }
                Parameter areaParam = floor.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                double area = areaParam != null && areaParam.HasValue ? RevitUnits.ToSquareMeters(areaParam.AsDouble()) : 0;
                if (area <= 0)
                {
                    continue;
                }
                report.Count(report.ElementsScanned, Name);
                foreach (var item in SlabOnGradeRule.Quantities(area, byLevel, s))
                {
                    ElementQuantity q = describer.Describe(floor);
                    q.Kind = item.kind;
                    q.Unit = item.unit;
                    q.Value = Math.Round(item.value, 4);
                    q.Reliability = item.reliability;
                    q.Assumption = item.assumption;
                    q.MethodCode = item.kind == QuantityKinds.Polythene ? "SLAB_ON_GRADE_AREA" : "SLAB_ON_GRADE_AREA_X_THICKNESS";
                    q.MethodDescription = item.method;
                    report.Quantities.Add(q);
                }
            }
        }

        // ------------------------------------------------------------------ assemblages modelises
        private void Connections(ExtractionReport report)
        {
            Document doc = describer.Document;
            var handlers = new FilteredElementCollector(doc).OfClass(typeof(StructuralConnectionHandler)).WhereElementIsNotElementType().ToList();
            if (handlers.Count == 0)
            {
                return;
            }
            decimal density = settings.Steel?.DensityKgPerM3 > 0 ? settings.Steel.DensityKgPerM3 : 7850m;
            int measured = 0;
            string source = null;
            foreach (Element h in handlers)
            {
                if (describer.ShouldSkip(h, out _)) continue;
                report.Count(report.ElementsScanned, Name);
                double volume = RevitUnits.ToCubicMeters(ConcreteExtractor.SolidVolume(h));
                if (volume <= 0)
                {
                    report.Note(IssueSeverity.Warning, "ASSEMBLAGE_SANS_GEOMETRIE",
                        "Assemblage " + h.Id.Value + " sans géométrie 3D (assemblage générique ?) : masse non mesurée.", h.Id.Value);
                    continue;
                }
                ElementQuantity q = describer.Describe(h);
                q.Kind = QuantityKinds.SteelConnections;
                q.Unit = BoqUnit.Kilogram;
                q.Value = Math.Round(volume * (double)density, 2);
                q.MethodCode = "CONNECTION_SOLID_VOLUME";
                q.MethodDescription = "Volume des platines, raidisseurs et boulons modélisés × " + density.ToString("0", CultureInfo.InvariantCulture) + " kg/m³.";
                report.Quantities.Add(q);
                source = q.SourceDocument;
                measured++;
            }
            if (measured > 0)
            {
                // Assemblages modelises : l'estimation en % de la masse des profiles ferait double compte.
                int removed = report.Quantities.RemoveAll(q => q.Kind == QuantityKinds.SteelConnections && q.MethodCode == "SHARE_OF_STEEL_MASS" &&
                                                             q.SourceDocument == source);
                report.Notes.Add(new BoqIssue(IssueSeverity.Info, "ASSEMBLAGES_MODELISES",
                    measured + " assemblage(s) modélisé(s) mesuré(s)" + (removed > 0 ? " ; estimation en % de la masse des profilés retirée (" + removed + " quantité(s))" : string.Empty) + "."));
            }
        }
    }
}
