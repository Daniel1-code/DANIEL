using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Menuiseries : comptage des portes et fenetres, une quantite de 1 U par instance, avec
    /// largeur et hauteur NOMINALES (dimensions de la designation) en parametres.
    /// </summary>
    internal sealed class JoineryExtractor
    {
        public const string Name = "Menuiseries";

        private readonly ElementDescriber describer;

        private readonly DanCI.BOQ.Core.Extraction.IProgressSink progress;

        public JoineryExtractor(ElementDescriber describer, DanCI.BOQ.Core.Extraction.IProgressSink progress)
        {
            this.describer = describer;
            this.progress = progress ?? DanCI.BOQ.Core.Extraction.NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            List<Element> elements = new FilteredElementCollector(describer.Document)
                .WherePasses(new ElementMulticategoryFilter(new[] { BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows }))
                .WhereElementIsNotElementType()
                .OfClass(typeof(FamilyInstance))
                .ToList();

            int index = 0;
            foreach (Element e in elements)
            {
                DanCI.BOQ.Core.Extraction.ProgressExtensions.Step(progress, "Menuiseries", ++index, elements.Count);
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(e, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                var fi = (FamilyInstance)e;
                bool isDoor = ElementDescriber.CategoryName(e.Category) == nameof(BuiltInCategory.OST_Doors);

                ElementQuantity q = describer.Describe(e);
                q.Kind = QuantityKinds.Count;
                q.Unit = BoqUnit.Each;
                q.Value = 1;
                q.MethodCode = "COUNT";
                q.MethodDescription = "Comptage de l'instance.";

                double? w = describer.ReadLengthMm(fi, isDoor ? BuiltInParameter.DOOR_WIDTH : BuiltInParameter.WINDOW_WIDTH,
                    BuiltInParameter.FAMILY_WIDTH_PARAM, BuiltInParameter.GENERIC_WIDTH);
                double? h = describer.ReadLengthMm(fi, isDoor ? BuiltInParameter.DOOR_HEIGHT : BuiltInParameter.WINDOW_HEIGHT,
                    BuiltInParameter.FAMILY_HEIGHT_PARAM, BuiltInParameter.GENERIC_HEIGHT);
                q.Parameters["LargeurMm"] = w.HasValue ? Math.Round(w.Value).ToString(CultureInfo.InvariantCulture) : string.Empty;
                q.Parameters["HauteurMm"] = h.HasValue ? Math.Round(h.Value).ToString(CultureInfo.InvariantCulture) : string.Empty;
                if (fi.Host != null)
                {
                    q.Parameters["HoteCategorie"] = ElementDescriber.CategoryName(fi.Host.Category);
                    q.Parameters["HoteId"] = fi.Host.Id.Value.ToString(CultureInfo.InvariantCulture);
                }
                if (!w.HasValue || !h.HasValue)
                {
                    report.Note(IssueSeverity.Warning, "MENUISERIE_SANS_DIMENSIONS",
                        "Menuiserie " + e.Id.Value + " (" + q.TypeName + ") : largeur ou hauteur illisible.", e.Id.Value);
                }
                report.Quantities.Add(q);
            }
        }
    }
}
