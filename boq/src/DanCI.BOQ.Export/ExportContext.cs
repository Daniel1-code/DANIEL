using System;
using System.Collections.Generic;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Export
{
    /// <summary>Tout ce qu'un export doit contenir : resultat, audit, et les hypotheses qui l'ont produit.</summary>
    public sealed class ExportContext
    {
        public ProjectInfo Project { get; set; } = new ProjectInfo();
        public BoqResult Boq { get; set; }
        public AuditReport Audit { get; set; }
        public BoqConfiguration Configuration { get; set; }
        public IReadOnlyList<ElementQuantity> Quantities { get; set; } = new ElementQuantity[0];
        public string DocumentTitle { get; set; }
        public string ScopeDescription { get; set; }
        public DateTime GeneratedAt { get; set; } = DateTime.Now;

        /// <summary>Logo facultatif (PNG ou JPG) ; aucun logo n'est fabrique s'il est absent.</summary>
        public string LogoPath { get; set; }

        /// <summary>Empreinte SHA-256 du metre (forme courte sur les documents).</summary>
        public string Fingerprint => fingerprint ?? (fingerprint = DanCI.BOQ.Core.Validation.QuantityFingerprint.Compute(Quantities ?? new ElementQuantity[0]));

        private string fingerprint;

        public string ProjectTitle =>
            string.IsNullOrWhiteSpace(Project?.Name) ? (DocumentTitle ?? "Projet") : Project.Name;
    }
}
