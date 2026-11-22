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
    /// Decomposition des murs, sols, toits et plafonds composites, COUCHE PAR COUCHE : pour chaque
    /// materiau de la structure du type, surface (Element.GetMaterialArea) et volume
    /// (Element.GetMaterialVolume) calcules par Revit sur la geometrie reelle de l'element
    /// (ouvertures percees, jointures appliquees). Lecture seule.
    /// Les couches vont dans ExtractionReport.Layers (recapitulatif « matériaux par couche ») ;
    /// seules les couches isolantes vont aussi au BOQ (article Isolation), car elles n'y sont pas
    /// deja : beton, maconnerie et finitions sont metres par leurs propres extracteurs.
    /// </summary>
    internal sealed class LayerExtractor
    {
        public const string Name = "Couches";

        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Ceilings
        };

        private readonly ElementDescriber describer;
        private readonly LayerSettings settings;
        private readonly ExtractionSettings all;
        private readonly IProgressSink progress;

        public LayerExtractor(ElementDescriber describer, ExtractionSettings all, IProgressSink progress)
        {
            this.describer = describer;
            this.all = all ?? new ExtractionSettings();
            this.settings = this.all.Layers ?? new LayerSettings();
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            if (!settings.Enabled) return;
            Document doc = describer.Document;
            List<Element> elements = StackedWalls.Expand(doc, new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(Categories))
                .WhereElementIsNotElementType()).ToList();
            var done = new HashSet<long>();
            int index = 0;
            foreach (Element e in elements)
            {
                progress.Step(Name, ++index, elements.Count);
                if (!done.Add(e.Id.Value) || (e is Wall w && (w.IsStackedWall || w.WallType.Kind != WallKind.Basic)))
                {
                    continue;
                }
                if (!(doc.GetElement(e.GetTypeId()) is HostObjAttributes type)) continue;
                CompoundStructure cs = type.GetCompoundStructure();
                if (cs == null || cs.LayerCount == 0) continue;
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (describer.PhaseAllows(e, out string status, out _) && status == PhaseStatus.Demolished)
                {
                    continue; // un ouvrage demoli n'a pas de couches a fournir
                }
                try
                {
                    ExtractElement(e, cs, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Warning, "COUCHES_ILLISIBLES", "Couches illisibles pour l'élément " + e.Id.Value + " : " + ex.Message, e.Id.Value);
                }
            }
        }

        private void ExtractElement(Element e, CompoundStructure cs, ExtractionReport report)
        {
            IList<CompoundStructureLayer> layers = cs.GetLayers();
            var definitions = new List<(LayerDefinition Def, ElementId Material)>();
            for (int i = 0; i < layers.Count; i++)
            {
                definitions.Add((new LayerDefinition
                {
                    Index = i + 1,
                    Function = LayerFunctions.FromRevit(layers[i].Function.ToString()),
                    MaterialKey = layers[i].MaterialId.Value.ToString(CultureInfo.InvariantCulture),
                    ThicknessMm = RevitUnits.ToMillimeters(cs.GetLayerWidth(i))
                }, layers[i].MaterialId));
            }

            bool anything = false;
            foreach (var group in definitions.GroupBy(d => d.Material.Value))
            {
                ElementId materialId = group.First().Material;
                List<LayerDefinition> defs = group.Select(g => g.Def).ToList();
                double area, volume;
                Reliability reliability = Reliability.Measured;
                string assumption = null;
                string method;
                string materialName;
                if (materialId == ElementId.InvalidElementId)
                {
                    // Couche « par catégorie » : pas de materiau a interroger. Surface de l'element x epaisseur.
                    materialName = "(matériau « par catégorie »)";
                    Parameter p = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                    area = p != null && p.HasValue ? RevitUnits.ToSquareMeters(p.AsDouble()) : 0;
                    volume = area * defs.Sum(d => d.ThicknessMm) / 1000.0;
                    reliability = Reliability.Estimated;
                    assumption = "Couche sans matériau (« par catégorie ») : surface de l'élément × épaisseur de la couche. Affectez un matériau à la couche dans le type.";
                    method = "Surface de l'élément (paramètre Surface) × épaisseur de la couche.";
                }
                else
                {
                    materialName = describer.Name(materialId);
                    area = RevitUnits.ToSquareMeters(e.GetMaterialArea(materialId, false));
                    volume = RevitUnits.ToCubicMeters(e.GetMaterialVolume(materialId));
                    method = "Surface (GetMaterialArea) et volume (GetMaterialVolume) du matériau dans l'élément, calculés par Revit sur la géométrie (ouvertures et jointures appliquées).";
                }
                if (area <= 1e-9 && volume <= 1e-9)
                {
                    continue; // materiau absent de la geometrie (element decoupe en Parts, couche nulle...)
                }
                foreach (LayerShare share in LayerSplit.Apply(defs, area, volume))
                {
                    anything = true;
                    ElementQuantity q = describer.Describe(e);
                    q.Kind = QuantityKinds.Layer;
                    q.Unit = BoqUnit.CubicMeter;
                    q.Value = share.VolumeM3;
                    q.Material = materialName;
                    q.MethodCode = "LAYER_MATERIAL";
                    q.MethodDescription = method + (share.AreaFromVolume ? " Surface = volume ÷ épaisseur (surface Revit nulle)." : string.Empty);
                    q.Reliability = share.Split ? Reliability.Estimated : reliability;
                    q.Assumption = share.Split
                        ? "Matériau présent dans " + defs.Count + " couches : volume réparti au prorata des épaisseurs, surface à parts égales."
                        : assumption;
                    var inv = CultureInfo.InvariantCulture;
                    q.Parameters[LayerFunctions.Parameter] = share.Layer.Function;
                    q.Parameters[LayerSummary.Thickness] = Math.Round(share.Layer.ThicknessMm, 1).ToString(inv);
                    q.Parameters[LayerSummary.Area] = share.AreaM2.ToString("0.####", inv);
                    q.Parameters[LayerSummary.Index] = share.Layer.Index.ToString(inv) + "/" + layers.Count.ToString(inv);
                    report.Layers.Add(q);

                    // Isolant au BOQ, sauf s'il est deja compte comme finition (etancheite, enduit...) : jamais deux fois.
                    if (settings.InsulationToBoq && share.AreaM2 > 1e-6 &&
                        (share.Layer.Function == LayerFunctions.Insulation || settings.InsulationMaterials.IsMatch(materialName)) &&
                        FinishClassifier.KindOf(materialName, all.Finishes) == null)
                    {
                        ElementQuantity iso = describer.Describe(e);
                        iso.Kind = QuantityKinds.Insulation;
                        iso.Unit = BoqUnit.SquareMeter;
                        iso.Value = share.AreaM2;
                        iso.Material = materialName;
                        iso.MethodCode = "LAYER_INSULATION_AREA";
                        iso.MethodDescription = "Couche isolante n° " + share.Layer.Index + " (" + Math.Round(share.Layer.ThicknessMm) + " mm) : " +
                                                (share.AreaFromVolume ? "volume ÷ épaisseur." : "surface du matériau calculée par Revit (GetMaterialArea).");
                        iso.Reliability = q.Reliability;
                        iso.Assumption = q.Assumption;
                        iso.Parameters[LayerSummary.Thickness] = q.Parameters[LayerSummary.Thickness];
                        iso.Parameters["Isolant"] = materialName + " — " + Math.Round(share.Layer.ThicknessMm).ToString(inv) + " mm";
                        report.Quantities.Add(iso);
                    }
                }
            }
            if (!anything)
            {
                report.Note(IssueSeverity.Info, "COUCHES_SANS_GEOMETRIE",
                    "Élément " + e.Id.Value + " : aucune couche mesurable (élément découpé en pièces/Parts ?). Décomposition par couches non faite.", e.Id.Value);
            }
        }
    }
}
