using System.Diagnostics;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Point d'entree de l'extraction (etape 3 : beton, maconnerie, menuiseries).
    /// LECTURE SEULE : aucune transaction, aucune modification du modele.
    /// Portee de l'etape 3 : modele hote entier, toutes phases (phases, liens RVT et selection
    /// arrivent aux etapes 5 et 7).
    /// </summary>
    public static class ModelExtractor
    {
        public static ExtractionReport Extract(Document doc, ExtractionSettings settings)
        {
            settings = settings ?? new ExtractionSettings();
            var report = new ExtractionReport();
            Stopwatch watch = Stopwatch.StartNew();

            var describer = new ElementDescriber(doc, settings, string.Empty);
            var materials = new MaterialClassifier(doc, settings);

            new ConcreteExtractor(describer, materials).Extract(report);
            new MasonryExtractor(describer, materials, settings).Extract(report);
            new JoineryExtractor(describer).Extract(report);

            report.Duration = watch.Elapsed;
            return report;
        }
    }
}
