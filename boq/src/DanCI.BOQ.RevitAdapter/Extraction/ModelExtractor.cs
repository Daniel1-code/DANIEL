using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Point d'entree de l'extraction : beton, coffrage, maconnerie, menuiseries, armatures
    /// (modelisees, sinon ratio), charpente metallique.
    /// LECTURE SEULE : aucune transaction, aucune modification du modele.
    /// Portee de l'etape 3 : modele hote entier, toutes phases (phases, liens RVT et selection
    /// arrivent aux etapes 5 et 7).
    /// </summary>
    public static class ModelExtractor
    {
        public static ExtractionReport Extract(Document doc, ExtractionSettings settings, SteelProfileTable steelProfiles = null)
        {
            settings = settings ?? new ExtractionSettings();
            var report = new ExtractionReport();
            Stopwatch watch = Stopwatch.StartNew();

            var describer = new ElementDescriber(doc, settings, string.Empty);
            var materials = new MaterialClassifier(doc, settings);

            new ConcreteExtractor(describer, materials).Extract(report);
            List<ElementQuantity> concrete = report.Quantities.Where(q => q.Kind == QuantityKinds.ConcreteVolume).ToList();

            new FormworkExtractor(describer, settings.Formwork).Extract(report, concrete.Select(q => q.ElementId));
            new MasonryExtractor(describer, materials, settings).Extract(report);
            new JoineryExtractor(describer).Extract(report);

            var rebar = new RebarExtractor(describer);
            rebar.Extract(report);
            // Ratio seulement pour les elements en beton qui ne portent AUCUNE armature modelisee.
            report.Quantities.AddRange(RebarRatioRule.Apply(concrete, rebar.Hosts, settings.RebarRatios));

            new SteelExtractor(describer, steelProfiles, settings.Steel).Extract(report);

            report.Duration = watch.Elapsed;
            return report;
        }
    }
}
