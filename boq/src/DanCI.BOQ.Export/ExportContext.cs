using System;
using System.Collections.Generic;
using System.Linq;
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
        /// <summary>Decomposition des elements composites couche par couche (feuille « Matériaux par couche »).</summary>
        public IReadOnlyList<ElementQuantity> Layers { get; set; } = new ElementQuantity[0];
        /// <summary>Inventaire de tous les materiaux modelises (element x materiau), informatif.</summary>
        public IReadOnlyList<ElementQuantity> Inventory { get; set; } = new ElementQuantity[0];
        /// <summary>Cote de chaque niveau Revit, en metres (affichee : « Étage 1 (+3,20 m) »).</summary>
        public IReadOnlyDictionary<string, double> LevelElevations { get; set; } = new Dictionary<string, double>();
        /// <summary>Niveaux du modele dans l'ordre des altitudes (feuille « BOQ par niveau »).</summary>
        public IReadOnlyList<string> LevelOrder { get; set; } = new string[0];
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

        /// <summary>Liste des materiaux a approvisionner (hypotheses de materiaux.json) ; null sans BOQ.</summary>
        public DanCI.BOQ.Core.Materials.MaterialList Materials => Boq == null ? null : materials ?? (materials =
            DanCI.BOQ.Core.Materials.MaterialsCalculator.Build(Boq, Configuration?.Materials ?? new DanCI.BOQ.Core.Materials.MaterialSettings()));

        private DanCI.BOQ.Core.Materials.MaterialList materials;

        /// <summary>Revisions enregistrees (plus recente d'abord) : tendances de la planche du tableau de bord.</summary>
        public IReadOnlyList<DanCI.BOQ.Core.Revisions.BoqSnapshot> RevisionHistory { get; set; } = new DanCI.BOQ.Core.Revisions.BoqSnapshot[0];

        /// <summary>Planning financier du projet ; null = pas d'echeancier dans les exports.</summary>
        public DanCI.BOQ.Core.Planning.ProjectSchedule Schedule { get; set; }

        public DanCI.BOQ.Core.Planning.ScheduleResult ScheduleResult => Boq == null || Schedule == null ? null : scheduleResult ?? (scheduleResult =
            DanCI.BOQ.Core.Planning.ScheduleCalculator.Build(Boq, Schedule));

        private DanCI.BOQ.Core.Planning.ScheduleResult scheduleResult;

        /// <summary>
        /// Planification des travaux (du niveau bas vers le niveau haut, Gantt). Demarrage : date de
        /// l'echeancier si renseignee, sinon le lundi qui suit la generation.
        /// </summary>
        public DanCI.BOQ.Core.Planning.WorkPlan WorkPlan => Boq == null || !Boq.AllLines.Any() ? null : workPlan ?? (workPlan =
            DanCI.BOQ.Core.Planning.WorkPlanner.Build(Boq, LevelOrder,
                DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(LevelOrder, Configuration?.Extraction),
                Configuration?.Planning, PlanningStart, PlanOverrides, Inventory));

        private DanCI.BOQ.Core.Planning.WorkPlan workPlan;

        /// <summary>Modifications du Gantt enregistrees dans le projet (durees, decalages, equipes).</summary>
        public DanCI.BOQ.Core.Planning.PlanOverrides PlanOverrides { get; set; }

        public DateTime PlanningStart
        {
            get
            {
                if (Schedule?.StartDate != null) return Schedule.StartDate.Value.Date;
                DateTime d = (GeneratedAt.Year < 2000 ? DateTime.Today : GeneratedAt.Date).AddDays(1);
                while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
                return d;
            }
        }

        /// <summary>Notre entreprise (emetteur du devis) : fiche Entreprise, a defaut DanCI.</summary>
        public PartyInfo Company => Project?.Company ?? CompanyProfileStore.Default();

        /// <summary>Logo de l'emetteur : celui du projet, sinon celui de la fiche Entreprise, sinon config\logo.png.</summary>
        public string EffectiveLogo =>
            Existing(Cover?.Logo) ?? Existing(Project?.Company?.Logo) ?? LogoPath;

        /// <summary>Logo du client (fiche Client du projet), ou null.</summary>
        public string ClientLogo => Existing(Project?.ClientParty?.Logo);

        private static string Existing(string path) =>
            !string.IsNullOrWhiteSpace(path) && System.IO.File.Exists(path) ? path : null;

        /// <summary>
        /// Images de la conception extraites du modele Revit au moment de l'export (vue 3D, plans par
        /// niveau) : pages « Conception » du PDF et feuille « Conception » du classeur. Vide = rien d'ajoute.
        /// </summary>
        public IReadOnlyList<DesignImage> DesignImages { get; set; } = new DesignImage[0];

        /// <summary>Image de la page de garde : celle choisie par l'utilisateur, sinon la vue 3D du modele.</summary>
        public string EffectiveProjectImage =>
            Existing(Cover?.ProjectImage) ?? (DesignImages ?? new DesignImage[0]).Where(d => d.Is3D).Select(d => Existing(d.Path)).FirstOrDefault(p => p != null);

        /// <summary>Images de conception lisibles (fichier present).</summary>
        public IReadOnlyList<DesignImage> ExistingDesignImages =>
            (DesignImages ?? new DesignImage[0]).Where(d => d != null && Existing(d.Path) != null).ToList();

        public string ProjectTitle =>
            string.IsNullOrWhiteSpace(Project?.Name) ? (DocumentTitle ?? "Projet") : Project.Name;
    }

    /// <summary>Image de la conception extraite du modele (fichier PNG).</summary>
    public sealed class DesignImage
    {
        /// <summary>Titre affiche (ex. « Vue 3D du modèle », « Plan — RDC »).</summary>
        public string Title { get; set; }
        /// <summary>Precision sous le titre (nom de la vue Revit, cote du niveau...).</summary>
        public string Caption { get; set; }
        public string Path { get; set; }
        public bool Is3D { get; set; }
    }
}
