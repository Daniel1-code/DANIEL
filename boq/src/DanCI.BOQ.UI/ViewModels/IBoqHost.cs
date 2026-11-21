using System;
using System.Collections.Generic;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>
    /// Ce que la fenetre demande a Revit. L'implementation (projet Addin) passe par un
    /// ExternalEvent : la fenetre n'appelle JAMAIS l'API Revit directement.
    /// </summary>
    public interface IBoqHost
    {
        /// <summary>Extraction + BOQ + audit, avec les forfaits saisis.</summary>
        void RequestAnalysis(AnalysisScope scope, IReadOnlyList<ManualLine> manualLines);

        /// <summary>Recalcule BOQ et audit avec de nouveaux forfaits, SANS reextraire le modele.</summary>
        void RequestRecompute(IReadOnlyList<ManualLine> manualLines);

        /// <summary>Selection et zoom dans Revit (elements des liens compris).</summary>
        void RequestSelection(IReadOnlyCollection<ElementRef> elements);

        void RequestExport();

        /// <summary>Enregistre le fichier projet .danciboq.json (forfaits, phase, liens).</summary>
        void RequestSaveProject(ProjectFile project);

        /// <summary>Ecrit DanCI_BOQ_Code / DanCI_BOQ_Lot dans le modele (apres confirmation).</summary>
        void RequestWriteParameters();
    }

    /// <summary>Resultat d'une analyse, transmis a la fenetre.</summary>
    public sealed class AnalysisOutcome
    {
        public string DocumentTitle { get; set; }
        public string ScopeDescription { get; set; }
        public BoqResult Boq { get; set; }
        public AuditReport Audit { get; set; }
        public IReadOnlyList<ElementQuantity> Quantities { get; set; } = new ElementQuantity[0];
        public IReadOnlyList<BoqIssue> ConfigurationIssues { get; set; } = new BoqIssue[0];

        /// <summary>Configuration ayant produit ce resultat (reprise dans les hypotheses de l'export).</summary>
        public DanCI.BOQ.Core.Configuration.BoqConfiguration Configuration { get; set; }

        /// <summary>Constats du modele (extraction, configuration) : reutilises par un recalcul.</summary>
        public IReadOnlyList<BoqIssue> ModelFacts { get; set; } = new BoqIssue[0];

        public IReadOnlyList<ManualLine> ManualLines { get; set; } = new ManualLine[0];
        public TimeSpan Duration { get; set; }

        /// <summary>Message si l'analyse n'a pas abouti (annulation, configuration invalide...).</summary>
        public string Failure { get; set; }
    }
}
