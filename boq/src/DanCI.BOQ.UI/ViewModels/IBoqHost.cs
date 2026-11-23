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

        /// <summary>Apercu avant export : pages du PDF et feuilles du classeur, sans rien ecrire.</summary>
        void RequestPreview();

        /// <summary>Affiche le panneau « Diagramme du métré » dans Revit, a cote de la vue 3D.</summary>
        void RequestShowChart();

        /// <summary>Cree / met a jour les nomenclatures « DanCI — BOQ » et « DanCI — BOQ par niveau » du projet (apres confirmation).</summary>
        void RequestSchedules();

        /// <summary>Dessine (liste non vide) ou retire (liste vide) les fouilles en 3D dans les vues 3D, sans rien ecrire dans le modele.</summary>
        void RequestExcavation3D(IReadOnlyList<ElementQuantity> excavations);

        /// <summary>Isole temporairement ces elements (modele hote) dans la vue active.</summary>
        void RequestIsolate(IReadOnlyCollection<long> hostElementIds, string label);

        /// <summary>Annule l'isolement temporaire de la vue active.</summary>
        void RequestResetIsolation();

        /// <summary>
        /// Studio 4D : affiche une image de la simulation dans la vue 3D dediee « DanCI — Planning 4D »
        /// (creee si besoin, activee) : elements termines visibles, en cours colores, a venir masques ;
        /// rotation du batiment. Vue seulement (isolement temporaire et remplacements graphiques de CETTE vue).
        /// </summary>
        void RequestSimulation4D(Sim4DFrame frame);

        /// <summary>Fin du Studio 4D : couleurs et isolement de la vue 4D retires.</summary>
        void RequestEndSimulation4D();

        /// <summary>Enregistre le fichier projet .danciboq.json (forfaits, phase, liens).</summary>
        void RequestSaveProject(ProjectFile project);

        /// <summary>Ecrit DanCI_BOQ_Code / DanCI_BOQ_Lot dans le modele (apres confirmation).</summary>
        void RequestWriteParameters();

        /// <summary>Choisit la bibliotheque de prix du projet (.json, ou .xlsx a importer) ; null = celle de l'installation.</summary>
        void RequestPriceLibrary(string path);

        /// <summary>
        /// Enregistre les parametres du projet (identite, devis, page de garde) dans le fichier
        /// projet et, si <paramref name="prices"/> n'est pas null, les prix saisis dans la
        /// bibliotheque de prix du projet ; puis recalcule le BOQ s'il existe.
        /// </summary>
        /// <param name="company">Fiche de notre entreprise (commune a tous les projets) ; null = inchangee.</param>
        void RequestSaveSettings(ProjectFile project, IReadOnlyList<DanCI.BOQ.Core.Pricing.PriceEdit> prices, PartyInfo company);

        /// <summary>Exporte le modele Excel des prix (tous les articles, prix existants).</summary>
        void RequestPriceTemplate();

        /// <summary>Compare la derniere analyse a une revision enregistree (instantane JSON) ; classeur des ecarts.</summary>
        void RequestRevisionComparison(string snapshotPath);

        /// <summary>Vue 3D coloree dans Revit (par lot, statut ou prix) : MODIFIE le modele (vue), apres confirmation.</summary>
        void RequestColorize(DanCI.BOQ.Core.Dashboard.ColoringMode mode);

        /// <summary>Enregistre l'etat actuel du devis comme revision, sans exporter.</summary>
        void RequestFreezeRevision();

        /// <summary>Etape 8 : gabarit Excel de metre manuel (sans les quantites du modele).</summary>
        void RequestValidationTemplate();

        /// <summary>Etape 8 : compare la derniere analyse a un gabarit rempli.</summary>
        void RequestManualComparison(string filledTemplatePath);

        /// <summary>Etape 8 : compare deux metres bruts CSV du meme modele (ex. Revit 2026 / 2027).</summary>
        void RequestRunComparison(string rawCsvA, string rawCsvB);
    }

    /// <summary>Resultat d'une analyse, transmis a la fenetre.</summary>
    public sealed class AnalysisOutcome
    {
        public string DocumentTitle { get; set; }
        public string ScopeDescription { get; set; }
        public BoqResult Boq { get; set; }
        public AuditReport Audit { get; set; }
        public IReadOnlyList<ElementQuantity> Quantities { get; set; } = new ElementQuantity[0];
        /// <summary>Decomposition couche par couche (informatif, hors BOQ).</summary>
        public IReadOnlyList<ElementQuantity> Layers { get; set; } = new ElementQuantity[0];
        /// <summary>Inventaire de tous les materiaux modelises (element x materiau), informatif.</summary>
        public IReadOnlyList<ElementQuantity> Inventory { get; set; } = new ElementQuantity[0];
        /// <summary>Cote de chaque niveau Revit, en metres (affichee : « Étage 1 (+3,20 m) »).</summary>
        public IReadOnlyDictionary<string, double> LevelElevations { get; set; } = new Dictionary<string, double>();
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

    /// <summary>Une image de la simulation 4D envoyee a Revit.</summary>
    public sealed class Sim4DFrame
    {
        /// <summary>Elements des taches terminees (aspect normal).</summary>
        public IReadOnlyCollection<long> Built { get; set; } = new long[0];
        /// <summary>Elements des taches en cours -> couleur de leur lot (#RRGGBB).</summary>
        public IReadOnlyDictionary<long, string> Running { get; set; } = new Dictionary<long, string>();
        /// <summary>Tous les elements du planning (cadrage fixe de la camera).</summary>
        public IReadOnlyCollection<long> All { get; set; } = new long[0];
        /// <summary>Orientation de la camera autour du batiment (degres) ; null = ne pas tourner.</summary>
        public double? AngleDeg { get; set; }
        /// <summary>Hauteur de la camera au-dessus de l'horizon (degres) ; null = 25°. 0 = vue de face.</summary>
        public double? ElevationDeg { get; set; }
        public string Label { get; set; }
        /// <summary>
        /// Appele par Revit quand l'image est dessinee (ou abandonnee) : le Studio n'avance qu'a ce
        /// signal, pour une cadence reguliere sans image sautee.
        /// </summary>
        public Action Applied { get; set; }

        /// <summary>Video en cours d'enregistrement (chemin du fichier .avi) ; null = pas d'enregistrement (ferme la video en cours).</summary>
        public string Record { get; set; }
        /// <summary>Images par seconde de la video.</summary>
        public int RecordFps { get; set; } = 8;
        /// <summary>Avancement du chantier (0..1) : barre en haut de la video.</summary>
        public double Progress { get; set; }
        /// <summary>Titre incruste au-dessus de la date (projet).</summary>
        public string Title { get; set; }

        /// <summary>Instances de liens RVT qui portent des elements du planning : masquees (leurs elements ne s'isolent pas un par un).</summary>
        public IReadOnlyCollection<long> PlannedLinks { get; set; } = new long[0];

        /// <summary>Taches en cours : leurs elements se construisent progressivement (ordre de chantier, assises).</summary>
        public IReadOnlyList<Sim4DTask> Tasks { get; set; } = new Sim4DTask[0];
    }

    /// <summary>Une tache en cours dans une image 4D : ses elements, sa couleur, son avancement (0..1).</summary>
    public sealed class Sim4DTask
    {
        public string Color { get; set; }
        public double Progress { get; set; }
        public IReadOnlyCollection<long> Elements { get; set; } = new long[0];
    }
}
