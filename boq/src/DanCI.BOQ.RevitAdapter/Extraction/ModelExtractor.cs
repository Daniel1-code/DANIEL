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
    /// (modelisees, sinon ratio), charpente metallique, finitions, terrassement et beton de
    /// proprete, reseaux et familles generiques, puis constats d'audit.
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
            AnalysisScope scope = request.Scope ?? new AnalysisScope();

            // Modele hote.
            ISet<long> hostScope = ScopeIds(doc, request);
            Phase phase = FindPhase(doc, scope.Phase);
            if (string.IsNullOrEmpty(scope.Phase))
            {
                report.Notes.Add(new BoqIssue(IssueSeverity.Warning, "PHASE_NON_CHOISIE",
                    "Aucune phase choisie : les éléments de TOUTES les phases sont comptés, existants et démolis compris."));
            }
            else if (phase == null)
            {
                report.Notes.Add(new BoqIssue(IssueSeverity.Error, "PHASE_INTROUVABLE",
                    "Phase « " + scope.Phase + " » introuvable dans le modèle : aucun filtre de phase appliqué."));
            }
            var hostDescriber = new ElementDescriber(doc, settings, string.Empty, hostScope, phase, scope.IncludeDemolition);
            RunDocument(hostDescriber, settings, steelProfiles, request.CollectAuditFacts, report, progress);

            // Liens RVT retenus : chaque INSTANCE est metree (deux instances = deux batiments).
            foreach (RevitLinkInstance link in SelectedLinks(doc, scope))
            {
                progress.Cancellation.ThrowIfCancellationRequested();
                string name = LinkName(doc, link);
                Document linkDoc = link.GetLinkDocument();
                if (linkDoc == null)
                {
                    continue; // lien decharge : deja signale par l'audit (LIEN_DECHARGE)
                }
                if (hostScope != null && !hostScope.Contains(link.Id.Value))
                {
                    report.Count(report.Skipped, "Lien hors de la portée choisie");
                    continue;
                }
                Phase linkPhase = FindPhase(linkDoc, scope.Phase);
                string source = name + " [" + link.Id.Value + "]";
                report.CurrentSource = source;
                report.CurrentLinkInstance = link.Id.Value;
                if (!string.IsNullOrEmpty(scope.Phase) && linkPhase == null)
                {
                    report.Notes.Add(new BoqIssue(IssueSeverity.Error, "PHASE_ABSENTE_DU_LIEN",
                        "Le lien « " + name + " » n'a pas de phase « " + scope.Phase + " » : ses éléments de toutes phases sont comptés."));
                }
                var linkDescriber = new ElementDescriber(linkDoc, settings, source, null, linkPhase, scope.IncludeDemolition,
                    link.GetTotalTransform(), link.Id.Value);
                RunDocument(linkDescriber, settings, steelProfiles, false, report, progress);
                report.CurrentSource = string.Empty;
                report.CurrentLinkInstance = null;
            }

            ApplyLevelFilter(report, scope);
            report.Duration = watch.Elapsed;
            return report;
        }

        /// <summary>Chaine d'extraction complete sur UN document (hote ou lien).</summary>
        private static void RunDocument(ElementDescriber describer, ExtractionSettings settings, SteelProfileTable steelProfiles,
            bool collectAuditFacts, ExtractionReport report, IProgressSink progress)
        {
            Document doc = describer.Document;
            var materials = new MaterialClassifier(doc, settings);
            var solids = new SolidCache();
            int start = report.Quantities.Count;

            new ConcreteExtractor(describer, materials, progress).Extract(report);
            List<ElementQuantity> concrete = report.Quantities.Skip(start).Where(q => q.Kind == QuantityKinds.ConcreteVolume).ToList();
            // Ni coffrage, ni armatures pour un ouvrage demoli ; le coffrage d'un element decoupe en
            // Parts se mesure sur l'element d'origine.
            List<ElementQuantity> built = concrete.Where(q => !IsDemolished(q)).ToList();
            List<long> formworkIds = built.Select(OriginalId).Distinct().ToList();

            new FormworkExtractor(describer, settings.Formwork, solids, progress).Extract(report, formworkIds);
            new MasonryExtractor(describer, materials, settings, progress).Extract(report);
            new JoineryExtractor(describer, progress).Extract(report);

            var rebar = new RebarExtractor(describer, progress);
            rebar.Extract(report);
            // Ratio seulement pour les elements en beton qui ne portent AUCUNE armature modelisee.
            report.Quantities.AddRange(RebarRatioRule.Apply(built, rebar.Hosts, settings.RebarRatios));

            new SteelExtractor(describer, steelProfiles, settings.Steel, progress).Extract(report);
            new FinishExtractor(describer, materials, settings, solids, progress).Extract(report);
            new LayerExtractor(describer, settings, progress).Extract(report);
            new EarthworkExtractor(describer, settings, solids, progress).Extract(report);
            new NetworkExtractor(describer, progress).Extract(report);
            new SubstructureExtractor(describer, settings, progress).Extract(report);
            new SecondWorkExtractor(describer, settings.SecondWork, progress, settings.Vrd).Extract(report);
            new GeneralEarthworkExtractor(describer, settings).Extract(report);
            // Pieux : quantites marquees (articles dedies), coffrage, fouille et proprete retires.
            PileRules.Apply(report.Quantities, settings.Piles, report.Notes);
            // VRD : sols exterieurs et clotures vers le lot 11, finitions des voiries retirees.
            VrdRules.Apply(report.Quantities, settings.Vrd, report.Notes);
            // Bilan des terres (remblai, evacuation) a partir des quantites de CE document.
            var docQuantities = report.Quantities.Skip(start).ToList();
            int before = docQuantities.Count;
            EarthworkBalance.Apply(docQuantities, settings.GeneralEarthworks, report.Notes);
            report.Quantities.AddRange(docQuantities.Skip(before));

            if (collectAuditFacts)
            {
                new AuditFactCollector(describer, solids, progress).Collect(report, formworkIds);
            }
        }

        private static bool IsDemolished(ElementQuantity q) =>
            q.Parameters != null && q.Parameters.TryGetValue(PhaseStatus.Parameter, out string s) && s == PhaseStatus.Demolished;

        private static long OriginalId(ElementQuantity q) =>
            q.Parameters != null && q.Parameters.TryGetValue(PhaseStatus.SourceElementParameter, out string s) &&
            long.TryParse(s, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long id)
                ? id
                : q.ElementId;

        public static Phase FindPhase(Document doc, string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }
            foreach (Phase p in doc.Phases)
            {
                if (string.Equals(p.Name, name, System.StringComparison.OrdinalIgnoreCase))
                {
                    return p;
                }
            }
            return null;
        }

        /// <summary>Phases du projet, dans l'ordre chronologique.</summary>
        public static IEnumerable<string> PhaseNames(Document doc)
        {
            foreach (Phase p in doc.Phases)
            {
                yield return p.Name;
            }
        }

        /// <summary>Noms des liens RVT charges (un nom par type de lien).</summary>
        public static IEnumerable<string> LoadedLinkNames(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Where(l => l.GetLinkDocument() != null)
                .Select(l => LinkName(doc, l))
                .Distinct()
                .OrderBy(n => n, System.StringComparer.CurrentCulture);

        private static IEnumerable<RevitLinkInstance> SelectedLinks(Document doc, AnalysisScope scope)
        {
            if (scope.Links == null || scope.Links.Count == 0)
            {
                return Enumerable.Empty<RevitLinkInstance>();
            }
            var wanted = new HashSet<string>(scope.Links, System.StringComparer.OrdinalIgnoreCase);
            return new FilteredElementCollector(doc).OfClass(typeof(RevitLinkInstance)).Cast<RevitLinkInstance>()
                .Where(l => wanted.Contains(LinkName(doc, l)) && !ElementDescriber.IsSecondaryOption(l))
                .OrderBy(l => l.Id.Value)
                .ToList();
        }

        private static string LinkName(Document doc, RevitLinkInstance link) =>
            (doc.GetElement(link.GetTypeId()) as RevitLinkType)?.Name ?? link.Name;

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
            report.Layers.RemoveAll(q => !string.IsNullOrEmpty(q.Level) && !levels.Contains(q.Level));
            int removed = before - report.Quantities.Count;
            if (removed > 0)
            {
                report.Skipped["Hors des niveaux choisis (quantités)"] = removed;
            }
        }
    }
}
