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

        /// <summary>Page de garde (theme, image du projet, page importee) ; chemins absolus.</summary>
        public CoverSettings Cover { get; set; } = new CoverSettings();

        /// <summary>Tableau de bord de pilotage, calcule une fois a partir du BOQ, de l'audit et du metre.</summary>
        public DanCI.BOQ.Core.Dashboard.BoqDashboard Dashboard => Boq == null ? null : dashboard ?? (dashboard =
            DanCI.BOQ.Core.Dashboard.BoqDashboard.Build(Boq, Audit, Quantities, Project, new DanCI.BOQ.Core.Dashboard.DashboardOptions
            {
                Today = GeneratedAt.Date,
                ExchangeRateDate = Configuration?.Settings?.ExchangeRate?.Date
            }));

        private DanCI.BOQ.Core.Dashboard.BoqDashboard dashboard;

        /// <summary>Notre entreprise (emetteur du devis) : fiche Entreprise, a defaut DanCI.</summary>
        public PartyInfo Company => Project?.Company ?? CompanyProfileStore.Default();

        /// <summary>Logo de l'emetteur : celui du projet, sinon celui de la fiche Entreprise, sinon config\logo.png.</summary>
        public string EffectiveLogo =>
            Existing(Cover?.Logo) ?? Existing(Project?.Company?.Logo) ?? LogoPath;

        /// <summary>Logo du client (fiche Client du projet), ou null.</summary>
        public string ClientLogo => Existing(Project?.ClientParty?.Logo);

        private static string Existing(string path) =>
            !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path) ? path : null;

        public string ProjectTitle =>
            string.IsNullOrWhiteSpace(Project?.Name) ? (DocumentTitle ?? "Projet") : Project.Name;
    }
}
