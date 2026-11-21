using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>Demande d'analyse : portee choisie et contexte Revit necessaire pour l'appliquer.</summary>
    public sealed class ExtractionRequest
    {
        public AnalysisScope Scope { get; set; } = new AnalysisScope();

        /// <summary>Vue active (portee VueActive).</summary>
        public View ActiveView { get; set; }

        /// <summary>Elements selectionnes (portee Selection).</summary>
        public ICollection<ElementId> Selection { get; set; } = new List<ElementId>();

        public bool CollectAuditFacts { get; set; } = true;
    }

    /// <summary>
    /// Point d'entree de l'extraction : beton, coffrage, maconnerie, menuiseries, armatures
    /// (modelisees, sinon ratio), charpente metallique, puis constats d'audit.
    /// LECTURE SEULE : aucune transaction, aucune modification du modele.
    /// Annulable : leve OperationCanceledException, et aucun resultat partiel n'est rendu.
    /// </summary>
    public static class ModelExtractor
    {
        public static ExtractionReport Extract(Document doc, ExtractionSettings settings, SteelProfileTable steelProfiles = null)
        {
            return Extract(doc, settings, steelProfiles, new ExtractionRequest(), NoProgress.Instance);
        }

        public static ExtractionReport Extract(Document doc, ExtractionSettings settings, SteelProfileTable steelProfiles,
            ExtractionRequest request, IProgressSink progress)
        {
            settings = settings ?? new ExtractionSettings();
            request = request ?? new ExtractionRequest();
            progress = progress ?? NoProgress.Instance;
            var report = new ExtractionReport();
            Stopwatch watch = Stopwatch.StartNew();

            ISet<long> scope = ScopeIds(doc, request);
            var describer = new ElementDescriber(doc, settings, string.Empty, scope);
            var materials = new MaterialClassifier(doc, settings);
            var solids = new SolidCache();

            new ConcreteExtractor(describer, materials, progress).Extract(report);
            List<ElementQuantity> concrete = report.Quantities.Where(q => q.Kind == QuantityKinds.ConcreteVolume).ToList();
            List<long> concreteIds = concrete.Select(q => q.ElementId).Distinct().ToList();

            new FormworkExtractor(describer, settings.Formwork, solids, progress).Extract(report, concreteIds);
            new MasonryExtractor(describer, materials, settings, progress).Extract(report);
            new JoineryExtractor(describer, progress).Extract(report);

            var rebar = new RebarExtractor(describer, progress);
            rebar.Extract(report);
            // Ratio seulement pour les elements en beton qui ne portent AUCUNE armature modelisee.
            report.Quantities.AddRange(RebarRatioRule.Apply(concrete, rebar.Hosts, settings.RebarRatios));

            new SteelExtractor(describer, steelProfiles, settings.Steel, progress).Extract(report);

            if (request.CollectAuditFacts)
            {
                new AuditFactCollector(describer, solids, progress).Collect(report, concreteIds);
            }

            ApplyLevelFilter(report, request.Scope);
            report.Duration = watch.Elapsed;
            return report;
        }

        /// <summary>ElementId admis par la portee ; null = modele entier.</summary>
        private static ISet<long> ScopeIds(Document doc, ExtractionRequest request)
        {
            switch (request.Scope.Mode)
            {
                case ScopeMode.VueActive:
                    if (request.ActiveView == null)
                    {
                        return new HashSet<long>();
                    }
                    return new HashSet<long>(new FilteredElementCollector(doc, request.ActiveView.Id)
                        .WhereElementIsNotElementType()
                        .ToElementIds()
                        .Select(id => id.Value));
                case ScopeMode.Selection:
                    var ids = new HashSet<long>();
                    foreach (ElementId id in request.Selection ?? new List<ElementId>())
                    {
                        ids.Add(id.Value);
                        // Un mur empile selectionne emporte ses membres.
                        if (doc.GetElement(id) is Wall w && w.IsStackedWall)
                        {
                            foreach (ElementId m in w.GetStackedWallMemberIds())
                            {
                                ids.Add(m.Value);
                            }
                        }
                    }
                    return ids;
                default:
                    return null;
            }
        }

        /// <summary>Filtre par niveau, applique APRES extraction (les contacts du coffrage voient tout le modele).</summary>
        private static void ApplyLevelFilter(ExtractionReport report, AnalysisScope scope)
        {
            if (scope?.Levels == null || scope.Levels.Count == 0)
            {
                return;
            }
            var levels = new HashSet<string>(scope.Levels);
            int before = report.Quantities.Count;
            report.Quantities.RemoveAll(q => !string.IsNullOrEmpty(q.Level) && !levels.Contains(q.Level));
            int removed = before - report.Quantities.Count;
            if (removed > 0)
            {
                report.Skipped["Hors des niveaux choisis (quantités)"] = removed;
            }
        }
    }
}
