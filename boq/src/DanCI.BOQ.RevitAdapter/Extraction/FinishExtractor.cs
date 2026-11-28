using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Finitions : enduits, peintures, revetements de sol, etancheite, couverture. Pour chaque
    /// materiau de l'element reconnu par son nom (extraction.json, « finitions »), surface =
    /// Element.GetMaterialArea, calculee par Revit sur la geometrie (ouvertures percees). Les
    /// peintures posees avec l'outil « Peindre » sont lues aussi (GetMaterialIds(true)).
    /// Toits non beton sans materiau de couverture reconnu : surface reelle des faces de dessus
    /// (pente incluse, pas la projection).
    /// </summary>
    internal sealed class FinishExtractor
    {
        public const string Name = "Finitions";

        private static readonly BuiltInCategory[] Categories =
        {
            BuiltInCategory.OST_Walls, BuiltInCategory.OST_Floors, BuiltInCategory.OST_Ceilings, BuiltInCategory.OST_Roofs
        };

        private readonly ElementDescriber describer;
        private readonly MaterialClassifier materials;
        private readonly ExtractionSettings settings;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public FinishExtractor(ElementDescriber describer, MaterialClassifier materials, ExtractionSettings settings, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.materials = materials;
            this.settings = settings;
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
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
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (Demolished(e))
                {
                    continue; // pas de finitions pour un ouvrage demoli
                }
                try
                {
                    ExtractElement(e, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC", "Finitions illisibles pour l'élément " + e.Id.Value + " : " + ex.Message, e.Id.Value);
                }
            }
        }

        private bool Demolished(Element e) =>
            describer.PhaseAllows(e, out string status, out _) && status == PhaseStatus.Demolished;

        private void ExtractElement(Element e, ExtractionReport report)
        {
            bool roof = ElementDescriber.CategoryName(e.Category) == nameof(BuiltInCategory.OST_Roofs);
            bool roofCovered = false, roofConcrete = false;

            foreach (bool painted in new[] { false, true })
            {
                foreach (ElementId id in e.GetMaterialIds(painted).OrderBy(i => i.Value))
                {
                    if (materials.ConcreteReason(id) != null)
                    {
                        roofConcrete |= roof;
                        continue;
                    }
                    string name = describer.Name(id);
                    string kind = FinishClassifier.KindOf(name, settings.Finishes);
                    if (kind == null)
                    {
                        continue;
                    }
                    double area = RevitUnits.ToSquareMeters(e.GetMaterialArea(id, painted));
                    if (area <= 1e-6)
                    {
                        continue;
                    }
                    roofCovered |= roof && (kind == QuantityKinds.Roofing || kind == QuantityKinds.Waterproofing);
                    Add(report, e, kind, area, name + (painted ? " (peinture appliquée)" : string.Empty),
                        painted ? "PAINTED_MATERIAL_AREA" : "MATERIAL_AREA",
                        painted ? "Surface peinte avec l'outil « Peindre » (GetMaterialArea)."
                                : "Aire du matériau dans l'élément (GetMaterialArea), ouvertures percées par Revit.");
                }
            }

            if (roof && !roofCovered && !roofConcrete)
            {
                // Toit sans materiau de couverture reconnu : surface reelle des faces de dessus.
                double top = 0;
                foreach (Solid s in solids.Of(e))
                {
                    foreach (Face f in s.Faces)
                    {
                        if (f is PlanarFace pf && pf.FaceNormal.Z > 0.05)
                        {
                            top += f.Area;
                        }
                    }
                }
                if (top > 0)
                {
                    Add(report, e, QuantityKinds.Roofing, RevitUnits.ToSquareMeters(top), string.Empty, "TOP_FACES_AREA",
                        "Surface réelle des faces de dessus du toit (pente incluse, pas la projection) ; matériau de couverture non reconnu.");
                }
            }
        }

        private void Add(ExtractionReport report, Element e, string kind, double areaM2, string material, string method, string description)
        {
            ElementQuantity q = describer.Describe(e);
            q.Kind = kind;
            q.Unit = BoqUnit.SquareMeter;
            q.Value = areaM2;
            q.Material = material;
            q.MethodCode = method;
            q.MethodDescription = description;
            report.Quantities.Add(q);
        }
    }
}
