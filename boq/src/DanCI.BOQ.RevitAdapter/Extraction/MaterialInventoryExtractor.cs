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
    /// Inventaire de TOUS les materiaux modelises : chaque element de modele dont la categorie porte
    /// des quantites de materiaux (murs, sols, poteaux, poutres, fondations, menuiseries, familles,
    /// modeles generiques...), et pour chaque materiau de l'element : surface (GetMaterialArea) et
    /// volume (GetMaterialVolume) calcules par Revit, couches des elements multicouches comprises.
    /// Un element sans aucun materiau est inscrit avec le volume de sa geometrie. Lecture seule ;
    /// rien n'est ajoute au BOQ.
    /// </summary>
    internal sealed class MaterialInventoryExtractor
    {
        public const string Name = "Inventaire des matériaux";

        private readonly ElementDescriber describer;
        private readonly InventorySettings settings;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public MaterialInventoryExtractor(ElementDescriber describer, InventorySettings settings, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings ?? new InventorySettings();
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            if (!settings.Enabled) return;
            Document doc = describer.Document;
            List<Element> elements = new FilteredElementCollector(doc).WhereElementIsNotElementType()
                .Where(e => e.Category != null && e.Category.CategoryType == CategoryType.Model &&
                            (e.Category.HasMaterialQuantities || settings.GenericCategories.Contains(ElementDescriber.CategoryName(e.Category))))
                .ToList();
            int index = 0;
            foreach (Element e in elements)
            {
                progress.Step(Name, ++index, elements.Count);
                if (e is Wall w && w.IsStackedWall) continue; // ses membres sont des murs a part entiere
                if (e is ElementType) continue;
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (describer.PhaseAllows(e, out string status, out _) && status == PhaseStatus.Demolished) continue;
                try
                {
                    ExtractElement(e, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Info, "INVENTAIRE_ILLISIBLE", "Matériaux illisibles pour l'élément " + e.Id.Value + " : " + ex.Message, e.Id.Value);
                }
            }
        }

        private void ExtractElement(Element e, ExtractionReport report)
        {
            var inv = CultureInfo.InvariantCulture;
            bool any = false;
            foreach (ElementId id in e.GetMaterialIds(false).OrderBy(i => i.Value))
            {
                double area = RevitUnits.ToSquareMeters(e.GetMaterialArea(id, false));
                double volume = RevitUnits.ToCubicMeters(e.GetMaterialVolume(id));
                if (area <= 1e-9 && volume <= 1e-9) continue;
                any = true;
                ElementQuantity q = describer.Describe(e);
                q.Kind = MaterialInventory.Kind;
                q.Unit = BoqUnit.CubicMeter;
                q.Value = volume;
                q.Material = describer.Name(id);
                q.MethodCode = "INVENTORY_MATERIAL";
                q.MethodDescription = "Surface (GetMaterialArea) et volume (GetMaterialVolume) du matériau dans l'élément, calculés par Revit.";
                q.Parameters[MaterialInventory.Area] = area.ToString("0.####", inv);
                report.Materials.Add(q);
            }

            // Couches « par categorie » d'un element multicouche : pas de materiau a interroger.
            if (describer.Document.GetElement(e.GetTypeId()) is HostObjAttributes host && host.GetCompoundStructure() is CompoundStructure cs &&
                cs.GetLayers().Any(l => l.MaterialId == ElementId.InvalidElementId && l.Width > 1e-9))
            {
                any = true;
                ElementQuantity q = describer.Describe(e);
                q.Kind = MaterialInventory.Kind;
                q.Unit = BoqUnit.CubicMeter;
                Parameter p = e.get_Parameter(BuiltInParameter.HOST_AREA_COMPUTED);
                double area = p != null && p.HasValue ? RevitUnits.ToSquareMeters(p.AsDouble()) : 0;
                double width = cs.GetLayers().Where(l => l.MaterialId == ElementId.InvalidElementId).Sum(l => l.Width);
                q.Value = area * RevitUnits.ToMillimeters(width) / 1000.0;
                q.Material = MaterialInventory.ByCategoryLabel;
                q.Reliability = Reliability.Estimated;
                q.MethodCode = "INVENTORY_BY_CATEGORY";
                q.MethodDescription = "Couche(s) sans matériau : surface de l'élément × épaisseur.";
                q.Parameters[MaterialInventory.Area] = area.ToString("0.####", inv);
                report.Materials.Add(q);
            }

            if (!any)
            {
                double volume = 0;
                foreach (Solid s in solids.Of(e)) volume += s.Volume;
                ElementQuantity q = describer.Describe(e);
                q.Kind = MaterialInventory.Kind;
                q.Unit = BoqUnit.CubicMeter;
                q.Value = RevitUnits.ToCubicMeters(volume);
                q.Material = MaterialInventory.NoMaterialLabel;
                q.MethodCode = "INVENTORY_NO_MATERIAL";
                q.MethodDescription = "Aucun matériau : volume de la géométrie de l'élément.";
                q.Parameters[MaterialInventory.Area] = "0";
                q.Parameters[MaterialInventory.NoMaterial] = "oui";
                report.Materials.Add(q);
            }
        }
    }
}
