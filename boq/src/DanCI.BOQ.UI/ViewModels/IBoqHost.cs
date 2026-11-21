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
        void RequestAnalysis(AnalysisScope scope);

        void RequestSelection(IReadOnlyCollection<long> elementIds);

        void RequestExport();
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
        public TimeSpan Duration { get; set; }

        /// <summary>Message si l'analyse n'a pas abouti (annulation, configuration invalide...).</summary>
        public string Failure { get; set; }
    }
}
