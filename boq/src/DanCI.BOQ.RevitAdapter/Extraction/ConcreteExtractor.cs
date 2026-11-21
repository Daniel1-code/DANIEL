using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Volume de beton PAR MATERIAU : Element.GetMaterialVolume, calcule par Revit sur la
    /// geometrie de l'element APRES jointures (un poteau qui coupe une poutre n'est compte
    /// qu'une fois, dans l'element qui garde la matiere selon l'ordre de jointure de Revit).
    /// Une quantite par element et par materiau beton.
    /// </summary>
    internal sealed class ConcreteExtractor
    {
        public const string Name = "Béton";

        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRuns,
            BuiltInCategory.OST_StairsLandings
        };

        private const double MinVolumeM3 = 1e-9;

        private readonly ElementDescriber describer;
        private readonly MaterialClassifier materials;

        public ConcreteExtractor(ElementDescriber describer, MaterialClassifier materials)
        {
            this.describer = describer;
            this.materials = materials;
        }

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;
            IEnumerable<Element> elements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(Categories))
                .WhereElementIsNotElementType();

            var done = new HashSet<long>();
            foreach (Element e in StackedWalls.Expand(doc, elements))
            {
                if (!done.Add(e.Id.Value))
                {
                    continue; // membre de mur empile deja traite via son parent
                }
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (e is Wall wall && (wall.WallType.Kind == WallKind.Curtain || wall.IsStackedWall))
                {
                    // Mur-rideau : menuiserie. Mur empile : ses membres sont des murs, comptes un par un.
                    continue;
                }
                if (e is Stairs stairs && stairs.GetStairsRuns().Count > 0)
                {
                    // Escalier par composants : la geometrie est portee par les volees et paliers.
                    continue;
                }

                try
                {
                    ExtractElement(e, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC",
                        "Volume de béton illisible pour l'élément " + e.Id.Value + " : " + ex.Message, e.Id.Value);
                }
            }
        }

        private void ExtractElement(Element e, ExtractionReport report)
        {
            ICollection<ElementId> materialIds = e.GetMaterialIds(false);
            bool hasParts = PartUtils.HasAssociatedParts(describer.Document, e.Id);
            int found = 0;

            foreach (ElementId materialId in materialIds.OrderBy(id => id.Value))
            {
                string reason = materials.ConcreteReason(materialId);
                if (reason == null)
                {
                    continue;
                }
                double volume = RevitUnits.ToCubicMeters(e.GetMaterialVolume(materialId));
                if (volume < MinVolumeM3)
                {
                    continue;
                }
                found++;
                string materialName = describer.Name(materialId);
                Add(report, e, volume, materialName, reason, hasParts,
                    "MATERIAL_VOLUME",
                    "Volume du matériau dans l'élément (GetMaterialVolume), géométrie après jointures.");
            }

            if (found > 0 || materialIds.Count > 0)
            {
                return;
            }

            // Aucun materiau : on ne mesure que si la famille se declare en beton.
            if (e is FamilyInstance fi && fi.StructuralMaterialType == StructuralMaterialType.Concrete)
            {
                double volume = RevitUnits.ToCubicMeters(SolidVolume(e));
                if (volume >= MinVolumeM3)
                {
                    Add(report, e, volume, string.Empty, "TypeMateriauStructurel", hasParts,
                        "SOLID_VOLUME_NO_MATERIAL",
                        "Somme des volumes des solides de l'élément : aucun matériau affecté, famille déclarée en béton.");
                }
                report.Note(IssueSeverity.Warning, "SANS_MATERIAU",
                    "Élément " + e.Id.Value + " en béton sans matériau : volume mesuré, classe de béton inconnue.", e.Id.Value);
            }
        }

        private void Add(ExtractionReport report, Element e, double volumeM3, string materialName, string reason,
            bool hasParts, string method, string description)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.ConcreteVolume;
            q.Unit = BoqUnit.CubicMeter;
            q.Value = volumeM3;
            q.Material = materialName;
            q.MethodCode = method;
            q.MethodDescription = description;
            q.Parameters["ReconnaissanceBeton"] = reason;

            string concreteClass = ConcreteClass.Parse(materialName);
            string classSource = "Materiau";
            if (concreteClass == null)
            {
                concreteClass = ConcreteClass.Parse(q.TypeName);
                classSource = concreteClass != null ? "Type" : string.Empty;
            }
            q.Parameters["ClasseBeton"] = concreteClass ?? string.Empty;
            q.Parameters["SourceClasseBeton"] = classSource;

            if (e is FamilyInstance fi && fi.Host != null)
            {
                q.Parameters["HoteCategorie"] = ElementDescriber.CategoryName(fi.Host.Category);
            }

            if (hasParts)
            {
                q.Reliability = Reliability.Estimated;
                q.Assumption = "Élément découpé en pièces (Parts) : métré sur l'élément d'origine. " +
                               "La prise en charge des Parts arrive à l'étape 7.";
            }
            report.Quantities.Add(q);
        }

        internal static double SolidVolume(Element e)
        {
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false };
            return SolidVolume(e.get_Geometry(options));
        }

        private static double SolidVolume(GeometryElement geometry)
        {
            double total = 0;
            if (geometry == null)
            {
                return total;
            }
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid solid && solid.Volume > 0)
                {
                    total += solid.Volume;
                }
                else if (obj is GeometryInstance instance)
                {
                    total += SolidVolume(instance.GetInstanceGeometry());
                }
            }
            return total;
        }
    }
}
