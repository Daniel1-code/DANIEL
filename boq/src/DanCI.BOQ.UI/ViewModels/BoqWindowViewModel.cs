using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.Mvvm;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>
    /// Fenetre de revue du BOQ : portee, analyse, arborescence Lot &gt; Article, detail du metre,
    /// audit, exclus, pied de devis. Toute action sur le modele passe par IBoqHost.
    /// </summary>
    public sealed class BoqWindowViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly IBoqHost host;

        private ScopeOption selectedScope;
        private bool isBusy;
        private string status = "Choisissez la portée puis cliquez sur « Analyser ».";
        private LineRow selectedLine;
        private AnalysisOutcome outcome;

        public BoqWindowViewModel(IBoqHost host)
        {
            this.host = host ?? throw new ArgumentNullException(nameof(host));
            ScopeOptions = new List<ScopeOption>
            {
                new ScopeOption { Mode = ScopeMode.Modele, Label = "Modèle entier" },
                new ScopeOption { Mode = ScopeMode.VueActive, Label = "Vue active" },
                new ScopeOption { Mode = ScopeMode.Selection, Label = "Sélection courante" }
            };
            selectedScope = ScopeOptions[0];

            LinesView = CollectionViewSource.GetDefaultView(Lines);
            LinesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LineRow.LotHeader)));
            LinesView.Filter = o => string.IsNullOrEmpty(lotFilter) || (o as LineRow)?.Line.LotCode == lotFilter;
            FindingsView = CollectionViewSource.GetDefaultView(Findings);
            FindingsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FindingRow.SectionTitle)));

            AnalyzeCommand = new RelayCommand(_ => Analyze(), _ => !IsBusy);
            ExportCommand = new RelayCommand(_ => host.RequestExport(), _ => !IsBusy && outcome?.Boq != null);
            PreviewCommand = new RelayCommand(_ =>
            {
                Status = "Préparation de l'aperçu (PDF et Excel)…";
                host.RequestPreview();
            }, _ => !IsBusy && outcome?.Boq != null);
            WriteParametersCommand = new RelayCommand(_ => host.RequestWriteParameters(), _ => !IsBusy && outcome?.Boq != null);
            SelectLineCommand = new RelayCommand(p => Select((p as LineRow)?.ElementRefs), p => p is LineRow);
            SelectContributionCommand = new RelayCommand(p => Select(p is ContributionRow c ? new[] { c.Ref } : null), p => p is ContributionRow);
            SelectFindingCommand = new RelayCommand(p => Select((p as FindingRow)?.ElementRefs), p => p is FindingRow f && f.CanSelect);
            SelectExcludedCommand = new RelayCommand(p => Select(p is ExcludedRow x ? new[] { x.Ref } : null), p => p is ExcludedRow);
            SelectLayerGroupCommand = new RelayCommand(p => Select((p as LayerGroupRow)?.Refs), p => p is LayerGroupRow);
            SelectLayerCommand = new RelayCommand(p => Select(p is LayerItemRow x ? new[] { x.Ref } : null), p => p is LayerItemRow);
            SelectInventoryGroupCommand = new RelayCommand(p => Select((p as InventoryGroupRow)?.Refs), p => p is InventoryGroupRow);
            SelectInventoryItemCommand = new RelayCommand(p => Select(p is InventoryItemRow x ? new[] { x.Ref } : null), p => p is InventoryItemRow);
            SelectGenericCommand = new RelayCommand(p => Select(p is GenericRow x ? new[] { x.Ref } : null), p => p is GenericRow);
            SelectAllGenericCommand = new RelayCommand(_ => Select(GenericFindings.Select(g => g.Ref).Distinct().ToList()), _ => GenericFindings.Count > 0);
            PreviousElementCommand = new RelayCommand(_ => StepElement(-1), _ => LevelElementRows.Count > 0);
            NextElementCommand = new RelayCommand(_ => StepElement(1), _ => LevelElementRows.Count > 0);
            AllLevelElementsCommand = new RelayCommand(_ => SelectedLevelArticle = null, _ => SelectedLevelArticle != null);
            ShowElementCommand = new RelayCommand(_ => ShowCurrentElement(), _ => SelectedLevelElement != null);
            SelectLevelArticleCommand = new RelayCommand(p => Select((p as LevelArticleRow)?.Refs), p => p is LevelArticleRow);
            SelectLevelCommand = new RelayCommand(_ => Select(SelectedLevelBlock?.Refs), _ => SelectedLevelBlock != null && SelectedLevelBlock.Elements > 0);
            AddManualCommand = new RelayCommand(_ => ManualLines.Add(new ManualLineRow()));
            RemoveManualCommand = new RelayCommand(p => ManualLines.Remove(p as ManualLineRow), p => p is ManualLineRow);
            SaveManualCommand = new RelayCommand(_ => SaveProject(recompute: true), _ => !IsBusy);
            InitValidationCommands();
            InitPriceCommands();
            SaveSettingsCommand = new RelayCommand(_ => SaveSettings(), _ => !IsBusy);
            DashboardNavigateCommand = new RelayCommand(DashboardNavigate);
            ShowChartCommand = new RelayCommand(_ => host.RequestShowChart());
            SchedulesCommand = new RelayCommand(_ => host.RequestSchedules(), _ => !IsBusy && outcome?.Boq != null);
            DashboardSelectCommand = new RelayCommand(DashboardSelect, p => p != null && outcome?.Boq != null);
            ClearLotFilterCommand = new RelayCommand(_ => LotFilter = null, _ => !string.IsNullOrEmpty(LotFilter));
            ToggleBoardCommand = new RelayCommand(_ => ShowBoard = !ShowBoard);
            ShowSettingsCommand = new RelayCommand(_ => SelectedTab = SettingsTab);
            ColorizeCommand = new RelayCommand(p =>
            {
                var mode = p as string == "statut" ? DanCI.BOQ.Core.Dashboard.ColoringMode.Reliability
                         : p as string == "prix" ? DanCI.BOQ.Core.Dashboard.ColoringMode.Price : DanCI.BOQ.Core.Dashboard.ColoringMode.Lot;
                host.RequestColorize(mode);
            }, _ => !IsBusy && outcome?.Boq != null);
        }

        // ------------------------------------------------------------ parametres du projet et tableau de bord

        /// <summary>Onglets : 0 Tableau de bord, 1 BOQ, ... , dernier Parametres du projet.</summary>
        public const int DashboardTab = 0;
        public const int BoqTab = 1;
        public const int LayersTab = 6;
        public const int LevelsTab = 7;
        public const int PlanningTab = 8;
        public const int SettingsTab = 9;

        private int selectedTab = BoqTab;

        public int SelectedTab
        {
            get => selectedTab;
            set => Set(ref selectedTab, value);
        }

        public SettingsViewModel Settings { get; } = new SettingsViewModel();

        private DashboardViewModel dashboard = DashboardViewModel.Empty;

        private System.Windows.Media.ImageSource dashboardImage;

        /// <summary>Planche dessinee du tableau de bord (null : tableau de bord simple en repli).</summary>
        public System.Windows.Media.ImageSource DashboardImage
        {
            get => dashboardImage;
            private set
            {
                if (Set(ref dashboardImage, value)) Raise(nameof(HasDashboardImage));
            }
        }

        public bool HasDashboardImage => dashboardImage != null;
        public bool HasNoDashboardImage => dashboardImage == null;

        // ------------------------------------------------------------ tableau de bord interactif
        private bool showBoard;

        /// <summary>Faux (defaut) : tableau de bord interactif ; vrai : planche dessinee (apercu du PDF).</summary>
        public bool ShowBoard
        {
            get => showBoard;
            set
            {
                if (Set(ref showBoard, value))
                {
                    Raise(nameof(ShowBoardView));
                    Raise(nameof(ShowInteractiveView));
                    Raise(nameof(BoardToggleLabel));
                }
            }
        }

        public bool ShowBoardView => showBoard && dashboardImage != null;
        public bool ShowInteractiveView => !ShowBoardView;
        public string BoardToggleLabel => showBoard ? "◧ Tableau de bord interactif" : "▣ Planche (aperçu du PDF)";

        public ICommand DashboardNavigateCommand { get; }
        public ICommand DashboardSelectCommand { get; }
        public ICommand ClearLotFilterCommand { get; }
        public ICommand ToggleBoardCommand { get; }

        private string lotFilter;

        /// <summary>Lot affiche seul dans l'onglet BOQ (clic sur un lot du tableau de bord) ; null = tous.</summary>
        public string LotFilter
        {
            get => lotFilter;
            set
            {
                if (Set(ref lotFilter, value))
                {
                    LinesView.Refresh();
                    Raise(nameof(LotFilterText));
                    Raise(nameof(HasLotFilter));
                    UpdateExcavations3D();
                }
            }
        }

        public bool HasLotFilter => !string.IsNullOrEmpty(lotFilter);

        // ------------------------------------------------------------ fouilles dessinees en 3D
        private bool showExcavations3D = true;
        private string excavationSignature = string.Empty;

        /// <summary>Dessine en 3D les fouilles du lot filtre (01) ou de la ligne choisie (affichage seulement).</summary>
        public bool ShowExcavations3D
        {
            get => showExcavations3D;
            set
            {
                if (Set(ref showExcavations3D, value)) UpdateExcavations3D(force: true);
            }
        }

        /// <summary>Fouilles a dessiner : celles de la ligne choisie si c'est une ligne de fouilles, sinon celles du lot 01 filtre.</summary>
        public List<ElementQuantity> ExcavationsToShow()
        {
            if (!showExcavations3D || outcome?.Boq == null) return new List<ElementQuantity>();
            bool IsPit(ElementQuantity q) => q.Kind == QuantityKinds.Excavation && q.Parameters != null && q.Parameters.ContainsKey(Excavation3D.Parameter);
            List<ElementQuantity> fromLine = SelectedLine?.Line.Contributions.Select(c => c.Element).Where(IsPit).ToList() ?? new List<ElementQuantity>();
            if (fromLine.Count > 0) return fromLine;
            if (lotFilter == "01")
            {
                return outcome.Boq.AllLines.Where(l => l.LotCode == "01").SelectMany(l => l.Contributions).Select(c => c.Element).Where(IsPit).ToList();
            }
            return new List<ElementQuantity>();
        }

        private void UpdateExcavations3D(bool force = false)
        {
            List<ElementQuantity> list = ExcavationsToShow();
            string signature = string.Join(",", list.Select(q => q.SourceDocument + ":" + q.ElementId + ":" + q.MethodCode));
            if (!force && signature == excavationSignature) return;
            excavationSignature = signature;
            host.RequestExcavation3D(list);
        }
        public string LotFilterText => HasLotFilter ? "Filtre : lot " + lotFilter + " seulement" : string.Empty;

        public const int SettingsIdentitySubTab = 0;
        public const int SettingsEstimateSubTab = 4;
        public const int SettingsPricesSubTab = 5;

        private int settingsSubTab;

        public int SettingsSubTab
        {
            get => settingsSubTab;
            set => Set(ref settingsSubTab, value);
        }

        /// <summary>Clic sur un element du tableau de bord : ouvre l'endroit ou lire ou corriger.</summary>
        private void DashboardNavigate(object p)
        {
            switch (p)
            {
                case LotBar lot:
                    LotFilter = lot.Code;
                    SelectedTab = BoqTab;
                    Status = "BOQ filtré sur le lot " + lot.Code + ". Effacez le filtre pour revoir tous les lots.";
                    return;
                case ParetoRow article:
                    LotFilter = null;
                    SelectedTab = BoqTab;
                    SelectedLine = Lines.FirstOrDefault(l => l.Code == article.Code);
                    Status = "Article " + article.Code + " : détail du métré ci-dessous ; double-clic pour le voir dans Revit.";
                    return;
                case OriginSegment origin:
                    Navigate(origin.Target);
                    return;
                case MissingPriceRow missing:
                    SelectedTab = SettingsTab;
                    SettingsSubTab = SettingsPricesSubTab;
                    Settings.OnlyMissing = false;
                    Settings.PriceFilter = missing.Code;
                    Status = "Prix unitaires : saisissez le prix de l'article " + missing.Code + ", puis enregistrez.";
                    return;
                case SignalRow signal:
                    Navigate(signal.Target);
                    return;
                case KpiTile tile:
                    Navigate(tile.Target);
                    return;
            }
        }

        private void Navigate(string target)
        {
            switch (target)
            {
                case "prix":
                    SelectedTab = SettingsTab;
                    SettingsSubTab = SettingsPricesSubTab;
                    Settings.PriceFilter = null;
                    Settings.OnlyMissing = true;
                    Status = "Prix unitaires : seuls les articles sans prix sont affichés.";
                    break;
                case "audit":
                    SelectedTab = 2;
                    break;
                case "forfaits":
                    SelectedTab = 3;
                    break;
                case "nc":
                    LotFilter = "NC";
                    SelectedTab = BoqTab;
                    break;
                case "devis":
                    SelectedTab = SettingsTab;
                    SettingsSubTab = SettingsEstimateSubTab;
                    break;
                case "identite":
                    SelectedTab = SettingsTab;
                    SettingsSubTab = SettingsIdentitySubTab;
                    break;
                default:
                    LotFilter = null;
                    SelectedTab = BoqTab;
                    break;
            }
        }

        /// <summary>Bouton ⊙ du tableau de bord : selectionne et zoome les elements dans Revit.</summary>
        private void DashboardSelect(object p)
        {
            IEnumerable<BoqLine> lines = Lines.Select(l => l.Line);
            List<ElementRef> refs;
            switch (p)
            {
                case LotBar lot:
                    refs = lines.Where(l => l.LotCode == lot.Code).SelectMany(l => l.ElementRefs).Distinct().ToList();
                    break;
                case ParetoRow article:
                    refs = lines.Where(l => l.Code == article.Code).SelectMany(l => l.ElementRefs).Distinct().ToList();
                    break;
                case KeyQuantityRow quantity:
                    refs = lines.SelectMany(l => l.Contributions).Where(c => c.Element.Kind == quantity.Kind)
                                .Select(c => ElementRef.Of(c.Element)).Distinct().ToList();
                    break;
                default:
                    return;
            }
            Select(refs);
        }

        /// <summary>Affiche la planche (PNG) rendue par l'hote ; null = repli sur le tableau de bord simple.</summary>
        public void SetDashboardImage(byte[] png)
        {
            if (png == null || png.Length == 0)
            {
                DashboardImage = null;
                Raise(nameof(HasNoDashboardImage));
                Raise(nameof(ShowBoardView));
                Raise(nameof(ShowInteractiveView));
                return;
            }
            var bmp = new System.Windows.Media.Imaging.BitmapImage();
            using (var ms = new System.IO.MemoryStream(png))
            {
                bmp.BeginInit();
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            DashboardImage = bmp;
            Raise(nameof(HasNoDashboardImage));
            Raise(nameof(ShowBoardView));
            Raise(nameof(ShowInteractiveView));
        }

        public DashboardViewModel Dashboard
        {
            get => dashboard;
            private set => Set(ref dashboard, value);
        }

        // Derniers parametres ENREGISTRES : ce sont eux qui servent au calcul et a l'export
        // (une saisie en cours, non enregistree, ne change jamais un devis en silence).
        private ProjectDetails savedDetails;
        private ProjectEstimate savedEstimate;
        private CoverSettings savedCover;
        private PartyInfo savedClient;
        private PartyInfo savedCompany;
        private DanCI.BOQ.Core.Planning.ProjectSchedule savedSchedule;
        private DanCI.BOQ.Core.Extraction.ProjectExtraction savedExtraction;

        /// <summary>Reglages du metre enregistres ; null = ceux de l'installation.</summary>
        public DanCI.BOQ.Core.Extraction.ProjectExtraction ProjectExtraction => savedExtraction;

        /// <summary>Reglages du metre modifies depuis la derniere analyse : une nouvelle analyse est necessaire.</summary>
        public bool ExtractionChangedSinceAnalysis { get; private set; }

        /// <summary>Echeancier enregistre ; null = pas d'echeancier dans les exports.</summary>
        public DanCI.BOQ.Core.Planning.ProjectSchedule ProjectSchedule => savedSchedule;

        /// <summary>Devise, TVA, majorations du projet ; null = reglages de l'installation.</summary>
        public ProjectEstimate ProjectEstimate => savedEstimate;

        /// <summary>Identite du projet : saisie enregistree, a defaut Informations sur le projet de Revit.</summary>
        public ProjectInfo ProjectIdentity => DanCI.BOQ.Core.Model.ProjectIdentity.Build(Settings.Revit, savedDetails,
            savedClient?.Resolve(ProjectFilePath), savedCompany);

        /// <summary>Page de garde enregistree, chemins absolus.</summary>
        public CoverSettings ProjectCover => savedCover?.Resolve(ProjectFilePath) ?? new CoverSettings();

        public ICommand SaveSettingsCommand { get; }
        public ICommand ShowSettingsCommand { get; }
        public ICommand ColorizeCommand { get; }

        /// <summary>Panneau « Diagramme du métré » ancre dans Revit, a cote de la vue 3D.</summary>
        public ICommand ShowChartCommand { get; }

        /// <summary>BOQ dans les nomenclatures du projet Revit.</summary>
        public ICommand SchedulesCommand { get; }

        /// <summary>Contexte des parametres (a l'ouverture de la fenetre).</summary>
        public void SetSettingsContext(ProjectFile project, ProjectInfo revitInfo, DanCI.BOQ.Core.Pricing.EstimateSettings installation,
            IEnumerable<DanCI.BOQ.Core.Pricing.PriceEdit> prices, string priceLibraryInfo, PartyInfo company = null)
        {
            savedDetails = project?.Details;
            savedEstimate = project?.Estimate;
            savedCover = project?.Cover;
            savedClient = project?.ClientParty;
            savedSchedule = project?.Schedule;
            savedExtraction = project?.Extraction;
            savedPlanning = project?.Planning ?? new DanCI.BOQ.Core.Planning.PlanOverrides();
            savedCompany = company ?? CompanyProfileStore.Default();
            Settings.ClientParty.Load(project?.ClientParty?.Resolve(ProjectFilePath));
            Settings.Company.Load(savedCompany);
            Settings.Load(project, ProjectFilePath, revitInfo, installation);
            Settings.SetPrices(prices, priceLibraryInfo);
        }

        private void SaveSettings()
        {
            var errors = new List<string>();
            ProjectDetails details = Settings.BuildDetails(errors);
            ProjectEstimate estimate = Settings.BuildEstimate(errors);
            CoverSettings cover = Settings.BuildCover();
            List<DanCI.BOQ.Core.Pricing.PriceEdit> prices = Settings.PricesChanged ? Settings.BuildPrices(errors) : null;
            DanCI.BOQ.Core.Planning.ProjectSchedule schedule = Settings.BuildSchedule(errors);
            DanCI.BOQ.Core.Extraction.ProjectExtraction extraction = Settings.BuildExtraction(errors);
            if (errors.Count > 0)
            {
                Status = "Paramètres NON enregistrés : " + string.Join(" | ", errors.Take(4)) + (errors.Count > 4 ? " | …" : string.Empty);
                return;
            }
            savedDetails = details;
            savedEstimate = estimate;
            savedCover = RelativeCover(cover);
            PartyInfo client = Settings.ClientParty.Build();
            client.Logo = RelativeToProject(client.Logo);
            savedClient = client.IsEmpty && string.IsNullOrEmpty(client.Logo) ? null : client;
            savedCompany = Settings.Company.Build();
            savedSchedule = schedule;
            string before = savedExtraction == null ? null : System.Text.Json.JsonSerializer.Serialize(savedExtraction);
            if (before != System.Text.Json.JsonSerializer.Serialize(extraction)) ExtractionChangedSinceAnalysis = outcome != null;
            savedExtraction = extraction;
            IsBusy = outcome != null;
            Status = "Enregistrement des paramètres du projet…";
            host.RequestSaveSettings(BuildProjectFile(), prices, savedCompany);
        }

        private CoverSettings RelativeCover(CoverSettings c) => new CoverSettings
        {
            Theme = c.Theme,
            Subtitle = c.Subtitle,
            ProjectImage = RelativeToProject(c.ProjectImage),
            ImportedPage = RelativeToProject(c.ImportedPage),
            Logo = RelativeToProject(c.Logo)
        };

        public const string AllPhases = "(Toutes les phases)";

        public ObservableCollection<string> Phases { get; } = new ObservableCollection<string>();

        private string selectedPhase = AllPhases;

        public string SelectedPhase
        {
            get => selectedPhase;
            set => Set(ref selectedPhase, value);
        }

        private bool includeDemolition;

        public bool IncludeDemolition
        {
            get => includeDemolition;
            set => Set(ref includeDemolition, value);
        }

        /// <summary>Liens RVT charges ; coches = inclus au metre.</summary>
        public ObservableCollection<LevelItem> Links { get; } = new ObservableCollection<LevelItem>();

        public ObservableCollection<ManualLineRow> ManualLines { get; } = new ObservableCollection<ManualLineRow>();

        public IReadOnlyList<string> UnitChoices => ManualLineRow.UnitChoices;
        public IReadOnlyList<string> CurrencyChoices => ManualLineRow.CurrencyChoices;

        private string priceLibraryPath;

        /// <summary>Bibliotheque de prix du projet (chemin absolu) ; null = celle de l'installation.</summary>
        public string PriceLibraryPath
        {
            get => priceLibraryPath;
            set
            {
                if (Set(ref priceLibraryPath, value))
                {
                    Raise(nameof(PriceLibraryLabel));
                }
            }
        }

        public string PriceLibraryLabel => string.IsNullOrEmpty(PriceLibraryPath)
            ? "bibliothèque de l'installation (config\\prix_unitaires.json)"
            : System.IO.Path.GetFileName(PriceLibraryPath);

        public ICommand ChoosePriceLibraryCommand { get; private set; }
        public ICommand ResetPriceLibraryCommand { get; private set; }
        public ICommand PriceTemplateCommand { get; private set; }

        private void InitPriceCommands()
        {
            ChoosePriceLibraryCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Bibliothèque de prix du projet",
                    Filter = "Bibliothèque DanCI (*.json)|*.json|Classeur de prix à importer (*.xlsx)|*.xlsx"
                };
                if (dialog.ShowDialog() == true)
                {
                    host.RequestPriceLibrary(dialog.FileName);
                }
            }, _ => !IsBusy);
            ResetPriceLibraryCommand = new RelayCommand(_ => host.RequestPriceLibrary(null), _ => !IsBusy && !string.IsNullOrEmpty(PriceLibraryPath));
            PriceTemplateCommand = new RelayCommand(_ => host.RequestPriceTemplate(), _ => !IsBusy);
        }

        private string projectFilePath;

        public string ProjectFilePath
        {
            get => projectFilePath;
            set => Set(ref projectFilePath, value);
        }

        /// <summary>Phases, liens et contenu du fichier projet (a l'ouverture de la fenetre).</summary>
        public void SetContext(IEnumerable<string> phases, IEnumerable<string> links, ProjectFile project, string projectPath)
        {
            Phases.Clear();
            Phases.Add(AllPhases);
            foreach (string p in phases)
            {
                Phases.Add(p);
            }
            // Par defaut : la derniere phase (en general « Nouvelle construction »).
            SelectedPhase = project?.Phase != null && Phases.Contains(project.Phase)
                ? project.Phase
                : (Phases.Count > 1 ? Phases[Phases.Count - 1] : AllPhases);
            IncludeDemolition = project?.IncludeDemolition ?? false;

            var wanted = new HashSet<string>(project?.Links ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
            Links.Clear();
            foreach (string l in links)
            {
                Links.Add(new LevelItem { Name = l, IsChecked = wanted.Contains(l) });
            }
            ManualLines.Clear();
            foreach (ManualLine m in project?.ManualLines ?? new List<ManualLine>())
            {
                ManualLines.Add(ManualLineRow.From(m));
            }
            ProjectFilePath = projectPath;
            PriceLibraryPath = project?.ResolvePriceLibrary(projectPath);
        }

        public ProjectFile BuildProjectFile() => new ProjectFile
        {
            Phase = SelectedPhase == AllPhases ? null : SelectedPhase,
            IncludeDemolition = IncludeDemolition,
            Links = Links.Where(l => l.IsChecked).Select(l => l.Name).ToList(),
            ManualLines = CurrentManualLines().ToList(),
            PriceLibrary = RelativeToProject(PriceLibraryPath),
            Details = savedDetails,
            Estimate = savedEstimate,
            Cover = savedCover,
            ClientParty = savedClient,
            Schedule = savedSchedule,
            Extraction = savedExtraction,
            Planning = savedPlanning == null || savedPlanning.IsEmpty ? null : savedPlanning
        };

        /// <summary>Chemin relatif au dossier du fichier projet si la bibliotheque y est (le projet reste deplacable).</summary>
        private string RelativeToProject(string path)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(ProjectFilePath))
            {
                return path;
            }
            string folder = System.IO.Path.GetDirectoryName(ProjectFilePath);
            string relative = System.IO.Path.GetRelativePath(folder, path);
            return relative.StartsWith("..", StringComparison.Ordinal) ? path : relative;
        }

        public IReadOnlyList<ManualLine> CurrentManualLines() => ManualLines.Select(r => r.ToManualLine()).ToList();

        public ObservableCollection<DanCI.BOQ.Core.Revisions.BoqSnapshot> Revisions { get; } = new ObservableCollection<DanCI.BOQ.Core.Revisions.BoqSnapshot>();

        private DanCI.BOQ.Core.Revisions.BoqSnapshot selectedRevision;

        public DanCI.BOQ.Core.Revisions.BoqSnapshot SelectedRevision
        {
            get => selectedRevision;
            set => Set(ref selectedRevision, value);
        }

        public ICommand CompareRevisionCommand { get; private set; }
        public ICommand FreezeRevisionCommand { get; private set; }

        public void SetRevisions(IEnumerable<DanCI.BOQ.Core.Revisions.BoqSnapshot> revisions)
        {
            Revisions.Clear();
            foreach (var r in revisions ?? Enumerable.Empty<DanCI.BOQ.Core.Revisions.BoqSnapshot>())
            {
                Revisions.Add(r);
            }
            SelectedRevision = Revisions.FirstOrDefault();
        }

        public ICommand ValidationTemplateCommand { get; private set; }
        public ICommand CompareManualCommand { get; private set; }
        public ICommand CompareRunsCommand { get; private set; }

        /// <summary>Empreinte du metre de la derniere analyse (forme courte).</summary>
        public string Fingerprint => outcome == null ? "—" : DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(
            DanCI.BOQ.Core.Validation.QuantityFingerprint.Compute(outcome.Quantities));

        private void InitValidationCommands()
        {
            CompareRevisionCommand = new RelayCommand(_ => host.RequestRevisionComparison(SelectedRevision.FilePath),
                _ => !IsBusy && outcome?.Boq != null && SelectedRevision != null);
            FreezeRevisionCommand = new RelayCommand(_ => host.RequestFreezeRevision(), _ => !IsBusy && outcome?.Boq != null);
            ValidationTemplateCommand = new RelayCommand(_ => host.RequestValidationTemplate(), _ => !IsBusy && outcome?.Boq != null);
            CompareManualCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Gabarit de métré manuel rempli",
                    Filter = "Classeur Excel (*.xlsx)|*.xlsx"
                };
                if (dialog.ShowDialog() == true)
                {
                    host.RequestManualComparison(dialog.FileName);
                }
            }, _ => !IsBusy && outcome?.Boq != null);
            CompareRunsCommand = new RelayCommand(_ =>
            {
                var a = new Microsoft.Win32.OpenFileDialog { Title = "Métré brut A (ex. analyse Revit 2026)", Filter = "Métré brut DanCI (*metre_brut*.csv)|*metre_brut*.csv|CSV (*.csv)|*.csv" };
                if (a.ShowDialog() != true) return;
                var b = new Microsoft.Win32.OpenFileDialog { Title = "Métré brut B (ex. analyse Revit 2027)", Filter = a.Filter };
                if (b.ShowDialog() != true) return;
                host.RequestRunComparison(a.FileName, b.FileName);
            });
        }

        public ICommand WriteParametersCommand { get; }
        public ICommand AddManualCommand { get; }
        public ICommand RemoveManualCommand { get; }
        public ICommand SaveManualCommand { get; }

        private void SaveProject(bool recompute)
        {
            host.RequestSaveProject(BuildProjectFile());
            if (recompute && outcome != null)
            {
                IsBusy = true;
                Status = "Recalcul du BOQ avec les forfaits…";
                host.RequestRecompute(CurrentManualLines());
            }
        }

        public IReadOnlyList<ScopeOption> ScopeOptions { get; }

        public ScopeOption SelectedScope
        {
            get => selectedScope;
            set => Set(ref selectedScope, value);
        }

        public ObservableCollection<LevelItem> Levels { get; } = new ObservableCollection<LevelItem>();

        public ObservableCollection<LineRow> Lines { get; } = new ObservableCollection<LineRow>();
        public ICollectionView LinesView { get; }

        public ObservableCollection<FindingRow> Findings { get; } = new ObservableCollection<FindingRow>();
        public ICollectionView FindingsView { get; }

        public ObservableCollection<ExcludedRow> Excluded { get; } = new ObservableCollection<ExcludedRow>();

        public LineRow SelectedLine
        {
            get => selectedLine;
            set
            {
                if (Set(ref selectedLine, value))
                {
                    Raise(nameof(Contributions));
                    Raise(nameof(SelectedLineInfo));
                    UpdateExcavations3D();
                }
            }
        }

        public IReadOnlyList<ContributionRow> Contributions => boqByLevel
            ? selectedLevelBoqRow?.Contributions ?? new ContributionRow[0]
            : SelectedLine?.Contributions ?? new ContributionRow[0];

        // ------------------------------------------------------------ BOQ par niveau (presentation principale)
        public ObservableCollection<LevelBoqRow> LevelBoqRows { get; } = new ObservableCollection<LevelBoqRow>();
        public ICollectionView LevelBoqView { get; private set; }
        private bool boqByLevel = true;

        /// <summary>Presentation du BOQ : vrai = niveaux puis sous-lots ; faux = lots.</summary>
        public bool BoqByLevel
        {
            get => boqByLevel;
            set
            {
                if (!Set(ref boqByLevel, value)) return;
                Raise(nameof(BoqByLot));
                Raise(nameof(Contributions));
                Raise(nameof(SelectedLineInfo));
            }
        }

        public bool BoqByLot
        {
            get => !boqByLevel;
            set => BoqByLevel = !value;
        }

        private LevelBoqRow selectedLevelBoqRow;

        public LevelBoqRow SelectedLevelBoqRow
        {
            get => selectedLevelBoqRow;
            set
            {
                if (!Set(ref selectedLevelBoqRow, value)) return;
                Raise(nameof(Contributions));
                Raise(nameof(SelectedLineInfo));
            }
        }

        private ICommand selectLevelBoqRowCommand;
        public ICommand SelectLevelBoqRowCommand => selectLevelBoqRowCommand ?? (selectLevelBoqRowCommand =
            new RelayCommand(p => Select((p as LevelBoqRow)?.ElementRefs), p => p is LevelBoqRow));

        private void LoadLevelBoq(AnalysisOutcome result)
        {
            if (LevelBoqView == null)
            {
                LevelBoqView = CollectionViewSource.GetDefaultView(LevelBoqRows);
                LevelBoqView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LevelBoqRow.LevelHeader)));
                LevelBoqView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LevelBoqRow.LotHeader)));
                Raise(nameof(LevelBoqView));
            }
            LevelBoqRows.Clear();
            SelectedLevelBoqRow = null;
            if (result?.Boq == null) return;
            boqByLevel = result.Configuration?.Settings?.IsByLevel ?? true;
            Raise(nameof(BoqByLevel));
            Raise(nameof(BoqByLot));
            string cur = result.Boq.Currency.ToString();
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            int chapter = 0;
            foreach (var b in DanCI.BOQ.Core.Engine.LevelBreakdown.Build(result.Boq, levelOrder, null, null, null,
                         DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(levelOrder, result.Configuration?.Extraction)))
            {
                chapter++;
                string header = "NIVEAU " + chapter + " — " + DanCI.BOQ.Core.Engine.LevelBreakdown.Label(b.IsWithoutLevel ? null : b.Level, result.LevelElevations) +
                                "   |   " + b.Total.ToString("N2", fr) + " " + cur + "   |   " + b.Lots.Count + " lot(s), " + b.ElementCount + " élément(s)";
                foreach (var lot in b.Lots)
                {
                    foreach (var a in lot.Articles) LevelBoqRows.Add(new LevelBoqRow(header, lot, a, cur));
                }
            }
        }

        public string SelectedLineInfo => boqByLevel
            ? (selectedLevelBoqRow == null
                ? "Sélectionnez une ligne pour voir les éléments de cet article sur ce niveau. Double-clic : les sélectionner dans Revit."
                : selectedLevelBoqRow.Code + " — " + selectedLevelBoqRow.ElementCount + " élément(s) sur ce niveau")
            : SelectedLine == null
            ? "Sélectionnez une ligne pour voir le détail du métré. Double-clic : sélectionner et zoomer les éléments dans Revit."
            : SelectedLine.Code + " — " + SelectedLine.ElementCount + " élément(s)" +
              (string.IsNullOrEmpty(SelectedLine.Assumptions) ? string.Empty : " — Hypothèses : " + SelectedLine.Assumptions);

        public bool IsBusy
        {
            get => isBusy;
            set
            {
                if (Set(ref isBusy, value))
                {
                    CommandManager.InvalidateRequerySuggested();
                }
            }
        }

        public string Status
        {
            get => status;
            set => Set(ref status, value);
        }

        public string Currency => outcome?.Boq?.Currency.ToString() ?? "USD";
        public string WorksTotal => Money(outcome?.Boq?.WorksTotal);
        public string Markups => outcome?.Boq == null ? string.Empty :
            string.Join("   ", outcome.Boq.Markups.Select(m => m.Designation + " " + m.Percent.ToString("0.##", Fr) + " % : " + m.Amount.ToString("N2", Fr)));
        public string TotalExcludingVat => Money(outcome?.Boq?.TotalExcludingVat);
        public string Vat => outcome?.Boq == null ? string.Empty : "TVA " + outcome.Boq.VatPercent.ToString("0.##", Fr) + " % : " + Money(outcome.Boq.Vat);
        public string TotalIncludingVat => Money(outcome?.Boq?.TotalIncludingVat);
        public string SecondaryTotal => outcome?.Boq?.TotalIncludingVatSecondary == null ? string.Empty :
            "soit " + outcome.Boq.TotalIncludingVatSecondary.Value.ToString("N2", Fr) + " " + outcome.Boq.SecondaryCurrency;

        /// <summary>Total TTC en toutes lettres.</summary>
        public string AmountWords => outcome?.Boq == null ? string.Empty
            : DanCI.BOQ.Core.Pricing.AmountInWords.Sentence(outcome.Boq.TotalIncludingVat, outcome.Boq.Currency);

        public bool IsIncomplete => outcome?.Boq != null && !outcome.Boq.IsComplete;

        public string IncompleteText => IsIncomplete
            ? "⚠️ Totaux INCOMPLETS : " + outcome.Boq.AllLines.Count(l => !l.Amount.HasValue) + " ligne(s) sans prix. Les montants sont des minorants."
            : string.Empty;

        public string ReliabilityText
        {
            get
            {
                if (outcome?.Audit == null)
                {
                    return string.Empty;
                }
                if (outcome.Audit.ReliabilityIndex.HasValue)
                {
                    return "Indice de fiabilité : " + (outcome.Audit.ReliabilityIndex.Value * 100m).ToString("0.0", Fr) +
                           " % du montant issu de quantités mesurées";
                }
                return "Indice de fiabilité : pas de montant (aucun prix). Part mesurée par nature : " +
                       string.Join(", ", outcome.Audit.MeasuredShareByKind.OrderBy(k => k.Key, StringComparer.Ordinal)
                           .Select(k => k.Key + " " + (k.Value * 100m).ToString("0", Fr) + " %"));
            }
        }

        public string AuditHeader => outcome?.Audit == null
            ? "Audit"
            : "Audit (" + outcome.Audit.ErrorCount + " erreur(s), " + outcome.Audit.WarningCount + " avert.)";

        public string ExcludedHeader => "Exclus (" + Excluded.Count + ")";

        // ------------------------------------------------------------ couches (murs, sols, toits composites)
        public ObservableCollection<LayerGroupRow> LayerGroups { get; } = new ObservableCollection<LayerGroupRow>();
        public ObservableCollection<LayerItemRow> LayerItems { get; } = new ObservableCollection<LayerItemRow>();
        public ICommand SelectLayerGroupCommand { get; }
        public ICommand SelectLayerCommand { get; }
        public string LayersHeader => "Couches (" + LayerGroups.Count + ")";
        public string LayersTotalText { get; private set; } = string.Empty;

        private LayerGroupRow selectedLayerGroup;

        public LayerGroupRow SelectedLayerGroup
        {
            get => selectedLayerGroup;
            set
            {
                if (Set(ref selectedLayerGroup, value))
                {
                    LayerItems.Clear();
                    if (value != null)
                    {
                        foreach (ElementQuantity q in value.Row.Items.OrderBy(q => q.TypeName, StringComparer.Ordinal).ThenBy(q => q.ElementId))
                        {
                            LayerItems.Add(new LayerItemRow(q));
                        }
                    }
                }
            }
        }

        // ------------------------------------------------------------ BOQ par niveau
        public ObservableCollection<LevelBlockRow> LevelBlocks { get; } = new ObservableCollection<LevelBlockRow>();
        public ObservableCollection<LevelArticleRow> LevelArticles { get; } = new ObservableCollection<LevelArticleRow>();
        public ObservableCollection<InventoryGroupRow> LevelLayers { get; } = new ObservableCollection<InventoryGroupRow>();
        public ICollectionView LevelArticlesView { get; private set; }
        public ICommand SelectLevelArticleCommand { get; }
        public ICommand SelectLevelCommand { get; }
        public string LevelsHeader => "Par niveau (" + LevelBlocks.Count + ")";
        public string LevelSummaryText { get; private set; } = string.Empty;

        private LevelBlockRow selectedLevelBlock;

        public LevelBlockRow SelectedLevelBlock
        {
            get => selectedLevelBlock;
            set
            {
                if (!Set(ref selectedLevelBlock, value)) return;
                LevelArticles.Clear();
                LevelLayers.Clear();
                if (value != null)
                {
                    foreach (var lot in value.Block.Lots)
                    {
                        foreach (var a in lot.Articles) LevelArticles.Add(new LevelArticleRow(lot, a, outcome?.Boq?.Currency.ToString() ?? "USD"));
                    }
                    foreach (var m in value.Block.Materials) LevelLayers.Add(new InventoryGroupRow(m));
                }
                LevelSummaryText = value == null ? string.Empty : value.Summary;
                selectedLevelArticle = null;
                Raise(nameof(SelectedLevelArticle));
                LoadLevelElements();
                Raise(nameof(LevelSummaryText));
                Raise(nameof(HasLevelLayers));
            }
        }

        public bool HasLevelLayers => LevelLayers.Count > 0;

        // ------------------------------------------------------------ elements du niveau, un par un
        public ObservableCollection<LevelElementRow> LevelElementRows { get; } = new ObservableCollection<LevelElementRow>();
        public ICommand PreviousElementCommand { get; }
        public ICommand NextElementCommand { get; }
        public ICommand AllLevelElementsCommand { get; }
        public ICommand ShowElementCommand { get; }

        private LevelArticleRow selectedLevelArticle;

        /// <summary>Article choisi dans le niveau : la liste des elements se limite a ses elements (null = tout le niveau).</summary>
        public LevelArticleRow SelectedLevelArticle
        {
            get => selectedLevelArticle;
            set
            {
                if (Set(ref selectedLevelArticle, value)) LoadLevelElements();
            }
        }

        private LevelElementRow selectedLevelElement;

        public LevelElementRow SelectedLevelElement
        {
            get => selectedLevelElement;
            set
            {
                if (!Set(ref selectedLevelElement, value)) return;
                Raise(nameof(ElementCounter));
                if (value != null && followInRevit) ShowCurrentElement();
            }
        }

        private bool followInRevit = true;

        /// <summary>Chaque element choisi est selectionne et zoome dans Revit.</summary>
        public bool FollowInRevit
        {
            get => followInRevit;
            set => Set(ref followInRevit, value);
        }

        private bool isolateElement;

        /// <summary>L'element choisi est isole (seul visible) dans la vue active, temporairement.</summary>
        public bool IsolateElement
        {
            get => isolateElement;
            set
            {
                if (!Set(ref isolateElement, value)) return;
                if (!value) host.RequestResetIsolation();          // decoche : la vue redevient normale
                else if (selectedLevelElement != null) ShowCurrentElement();
            }
        }

        public string LevelElementsHeader => "Éléments (" + LevelElementRows.Count + ")";

        public string ElementCounter => LevelElementRows.Count == 0 ? "aucun élément"
            : (selectedLevelElement == null ? "—" : selectedLevelElement.Position.ToString(CultureInfo.InvariantCulture)) + " / " + LevelElementRows.Count +
              (selectedLevelArticle == null ? "  (tout le niveau)" : "  (article " + selectedLevelArticle.Code + ")");

        private void LoadLevelElements()
        {
            LevelElementRows.Clear();
            selectedLevelElement = null;
            int i = 1;
            foreach (var e in DanCI.BOQ.Core.Engine.LevelElements.Build(selectedLevelBlock?.Block, selectedLevelArticle?.Code))
            {
                LevelElementRows.Add(new LevelElementRow(e, i++));
            }
            Raise(nameof(SelectedLevelElement));
            Raise(nameof(LevelElementsHeader));
            Raise(nameof(ElementCounter));
        }

        private void StepElement(int delta)
        {
            if (LevelElementRows.Count == 0) return;
            int index = selectedLevelElement == null ? (delta > 0 ? 0 : LevelElementRows.Count - 1)
                : (selectedLevelElement.Position - 1 + delta + LevelElementRows.Count) % LevelElementRows.Count;
            SelectedLevelElement = LevelElementRows[index];
        }

        private void ShowCurrentElement()
        {
            LevelElementRow row = selectedLevelElement;
            if (row == null) return;
            if (isolateElement && !row.Ref.IsLinked)
            {
                host.RequestIsolate(new[] { row.Ref.ElementId }, row.Label);
            }
            Select(new[] { row.Ref });
        }

        private void LoadLevels(AnalysisOutcome result)
        {
            if (LevelArticlesView == null)
            {
                LevelArticlesView = CollectionViewSource.GetDefaultView(LevelArticles);
                LevelArticlesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(LevelArticleRow.LotHeader)));
                Raise(nameof(LevelArticlesView));
            }
            string previous = selectedLevelBlock?.Name;
            LevelBlocks.Clear();
            if (result?.Boq != null)
            {
                string cur = result.Boq.Currency.ToString();
                foreach (var b in DanCI.BOQ.Core.Engine.LevelBreakdown.Build(result.Boq, levelOrder, result.Layers, result.Inventory, result.Configuration?.Extraction?.Inventory,
                             DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(levelOrder, result.Configuration?.Extraction)))
                {
                    LevelBlocks.Add(new LevelBlockRow(b, cur));
                }
            }
            SelectedLevelBlock = LevelBlocks.FirstOrDefault(b => b.Name == previous) ?? LevelBlocks.FirstOrDefault();
            Raise(nameof(LevelsHeader));
        }

        // ------------------------------------------------------------ inventaire de tous les materiaux
        public ObservableCollection<InventoryGroupRow> InventoryGroups { get; } = new ObservableCollection<InventoryGroupRow>();
        public ObservableCollection<InventoryItemRow> InventoryItems { get; } = new ObservableCollection<InventoryItemRow>();
        public ObservableCollection<GenericRow> GenericFindings { get; } = new ObservableCollection<GenericRow>();
        public ICommand SelectInventoryGroupCommand { get; }
        public ICommand SelectInventoryItemCommand { get; }
        public ICommand SelectGenericCommand { get; }
        public ICommand SelectAllGenericCommand { get; }
        public string MaterialsHeader => "Matériaux (" + InventoryGroups.Count + ")" + (GenericFindings.Count > 0 ? " ⚠" : string.Empty);
        public string InventoryHeader => "Tous les matériaux modélisés (" + InventoryGroups.Count + ")";
        public string LayersSubHeader => "Multicouches : par couche (" + LayerGroups.Count + ")";
        public string GenericHeader => "Éléments génériques (" + GenericFindings.Select(g => g.Ref).Distinct().Count() + ")";
        public string InventoryTotalText { get; private set; } = string.Empty;
        public string GenericSummaryText { get; private set; } = string.Empty;

        private InventoryGroupRow selectedInventoryGroup;

        public InventoryGroupRow SelectedInventoryGroup
        {
            get => selectedInventoryGroup;
            set
            {
                if (Set(ref selectedInventoryGroup, value))
                {
                    InventoryItems.Clear();
                    if (value != null)
                    {
                        foreach (ElementQuantity q in value.Row.Items.OrderBy(q => q.Category, StringComparer.Ordinal).ThenBy(q => q.TypeName, StringComparer.Ordinal).ThenBy(q => q.ElementId))
                        {
                            InventoryItems.Add(new InventoryItemRow(q));
                        }
                    }
                }
            }
        }

        private void LoadInventory(AnalysisOutcome result)
        {
            InventoryGroups.Clear();
            GenericFindings.Clear();
            SelectedInventoryGroup = null;
            var settings = result?.Configuration?.Extraction?.Inventory;
            var rows = DanCI.BOQ.Core.Extraction.MaterialInventory.Build(result?.Inventory, settings);
            foreach (var r in rows) InventoryGroups.Add(new InventoryGroupRow(r));
            foreach (var f in DanCI.BOQ.Core.Extraction.MaterialInventory.FindGeneric(result?.Inventory, settings)) GenericFindings.Add(new GenericRow(f));
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            InventoryTotalText = rows.Count == 0
                ? "Inventaire vide : lancez l'analyse (ou inventaire désactivé dans extraction.json, « inventaire »)."
                : rows.Count + " matériau(x) dans " + rows.Sum(r => r.ElementCount) + " élément(s)×matériau — " +
                  rows.Sum(r => r.VolumeM3).ToString("N3", fr) + " m³, " + rows.Sum(r => r.AreaM2).ToString("N2", fr) + " m² (tous matériaux confondus)." +
                  (rows.Any(r => r.IsGeneric) ? " " + rows.Count(r => r.IsGeneric) + " matériau(x) générique(s) ou manquant(s) : voir l'onglet « Éléments génériques »." : string.Empty);
            int elements = GenericFindings.Select(g => g.Ref).Distinct().Count();
            GenericSummaryText = elements == 0
                ? "✓ Aucun élément générique : tous les éléments analysés ont un type, une famille et des matériaux propres au projet."
                : "⚠ " + elements + " élément(s) à vérifier : " + string.Join(", ", GenericFindings.GroupBy(g => g.Problem).Select(g => g.Key + " (" + g.Select(x => x.Ref).Distinct().Count() + ")")) +
                  ". Leurs quantités sont celles du gabarit Revit : précisez le type, la famille ou le matériau, puis relancez l'analyse.";
            foreach (string p in new[] { nameof(MaterialsHeader), nameof(InventoryHeader), nameof(LayersSubHeader), nameof(GenericHeader), nameof(InventoryTotalText), nameof(GenericSummaryText) })
            {
                Raise(p);
            }
        }

        // ------------------------------------------------------------ planning des travaux (Gantt)
        private DanCI.BOQ.Core.Planning.PlanOverrides savedPlanning = new DanCI.BOQ.Core.Planning.PlanOverrides();
        private bool panelLinked;

        /// <summary>Modifications du Gantt (enregistrees dans le projet).</summary>
        public DanCI.BOQ.Core.Planning.PlanOverrides PlanOverrides => savedPlanning;

        private double ganttPxPerDay = 5.0;

        /// <summary>Zoom de l'echelle de temps (pixels par jour calendaire).</summary>
        public double GanttPxPerDay
        {
            get => ganttPxPerDay;
            set
            {
                if (Set(ref ganttPxPerDay, Math.Max(1.5, Math.Min(20, value)))) LoadPlanning(outcome, keepStatus: true);
            }
        }

        public ObservableCollection<CrewRow> CrewRows { get; } = new ObservableCollection<CrewRow>();

        // ---- liens et filtres du Gantt
        public ObservableCollection<GanttLinkView> GanttLinks { get; } = new ObservableCollection<GanttLinkView>();
        public const string AllLevelsFilter = "Tous les niveaux";
        public const string AllLotsFilter = "Tous les lots";
        public ObservableCollection<string> GanttLevelChoices { get; } = new ObservableCollection<string>();
        public ObservableCollection<string> GanttLotChoices { get; } = new ObservableCollection<string>();
        private string ganttLevelFilter = AllLevelsFilter, ganttLotFilter = AllLotsFilter;
        private bool ganttCriticalOnly, showGanttLinks = true;

        public string GanttLevelFilter
        {
            get => ganttLevelFilter;
            set { if (Set(ref ganttLevelFilter, value ?? AllLevelsFilter)) LoadPlanning(outcome, keepStatus: true); }
        }

        public string GanttLotFilter
        {
            get => ganttLotFilter;
            set { if (Set(ref ganttLotFilter, value ?? AllLotsFilter)) LoadPlanning(outcome, keepStatus: true); }
        }

        /// <summary>Seulement les taches du chemin critique.</summary>
        public bool GanttCriticalOnly
        {
            get => ganttCriticalOnly;
            set { if (Set(ref ganttCriticalOnly, value)) LoadPlanning(outcome, keepStatus: true); }
        }

        public bool ShowGanttLinks
        {
            get => showGanttLinks;
            set => Set(ref showGanttLinks, value);
        }

        private bool PassesGanttFilter(DanCI.BOQ.Core.Planning.PlanTask t) =>
            (ganttLevelFilter == AllLevelsFilter || (t.Level ?? "(général)") == ganttLevelFilter) &&
            (ganttLotFilter == AllLotsFilter || ("Lot " + t.Lot) == ganttLotFilter.Split(' ').Take(2).Aggregate((a, b) => a + " " + b)) &&
            (!ganttCriticalOnly || t.IsCritical);
        public ObservableCollection<GanttMilestoneView> GanttMilestones { get; } = new ObservableCollection<GanttMilestoneView>();
        public string LateSummary { get; private set; } = string.Empty;
        private ICommand freezeBaselineCommand, clearBaselineCommand;

        public ICommand FreezeBaselineCommand => freezeBaselineCommand ?? (freezeBaselineCommand = new RelayCommand(_ =>
        {
            WorkPlan.SaveBaselineTo(savedPlanning, DateTime.Now);
            PlanningChanged("Planning de référence figé (" + WorkPlan.Tasks.Count + " tâches) : les glissements seront mesurés par rapport à lui.");
        }, _ => WorkPlan != null));

        public ICommand ClearBaselineCommand => clearBaselineCommand ?? (clearBaselineCommand = new RelayCommand(_ =>
        {
            savedPlanning.Baseline.Clear();
            savedPlanning.BaselineDate = null;
            PlanningChanged("Planning de référence effacé.");
        }, _ => savedPlanning.Baseline.Count > 0));

        private void OnProgressChanged(GanttRowView row, string text)
        {
            if (row?.Task == null) return;
            string t = (text ?? string.Empty).Replace("%", string.Empty).Replace(',', '.').Trim();
            if (t.Length == 0)
            {
                if (!savedPlanning.Progress.Remove(row.Task.Id)) return;
                PlanningChanged(row.Title + " : avancement réel effacé.");
                return;
            }
            if (!double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out double p) || p < 0 || p > 100)
            {
                Status = "Avancement « " + text + " » : saisissez un pourcentage entre 0 et 100.";
                return;
            }
            savedPlanning.Progress[row.Task.Id] = p;
            PlanningChanged(row.Title + " : avancement réel " + p.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " %.");
        }

        /// <summary>Retards a la date du curseur (date d'etat) : reel saisi inferieur au prevu.</summary>
        private void UpdateLateness()
        {
            if (WorkPlan == null) return;
            foreach (var r in GanttRows.Where(x => x.Task != null))
            {
                r.IsLate = r.Task.IsLateAt(planCursorDay);
            }
            var late = WorkPlan.Tasks.Where(t => t.IsLateAt(planCursorDay)).ToList();
            LateSummary = late.Count == 0
                ? (WorkPlan.Tasks.Any(t => t.ActualPercent.HasValue) ? "✓ Aucune tâche en retard au " + CursorDateText + "." : string.Empty)
                : "⚠ " + late.Count + " tâche(s) en retard au " + CursorDateText + " : " + string.Join(", ", late.Take(5).Select(t => "Lot " + t.Lot + (t.Level != null ? " · " + t.Level : string.Empty) +
                  " (" + t.ActualPercent.Value.ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " % au lieu de " + t.PlannedPercentAt(planCursorDay).ToString("0", CultureInfo.GetCultureInfo("fr-FR")) + " %)"));
            Raise(nameof(LateSummary));
        }
        private ICommand resetPlanningCommand, selectTaskCommand, showPlanDateCommand;

        public ICommand ResetPlanningCommand => resetPlanningCommand ?? (resetPlanningCommand = new RelayCommand(_ =>
        {
            savedPlanning = new DanCI.BOQ.Core.Planning.PlanOverrides();
            PlanningChanged("Planning réinitialisé : durées calculées, aucun décalage, équipes de planning.json.");
        }, _ => !savedPlanning.IsEmpty));

        public ICommand SelectTaskCommand => selectTaskCommand ?? (selectTaskCommand = new RelayCommand(p =>
        {
            if (p is GanttRowView r && r.Task != null && r.Task.Elements.Count > 0) Select(r.Task.Elements);
        }));

        public ICommand ShowPlanDateCommand => showPlanDateCommand ?? (showPlanDateCommand = new RelayCommand(_ =>
        {
            if (WorkPlan == null) return;
            var ids = WorkPlan.BuiltAt(planCursorDay).Where(r => !r.IsLinked).Select(r => r.ElementId).ToList();
            if (ids.Count == 0) { Status = "À cette date, rien n'est encore construit."; return; }
            host.RequestIsolate(ids, "Planning au " + CursorDateText);
        }, _ => WorkPlan != null));

        private int planCursorDay;

        /// <summary>Curseur de date du Gantt (jour ouvre).</summary>
        public int PlanCursorDay
        {
            get => planCursorDay;
            set
            {
                if (!Set(ref planCursorDay, Math.Max(0, Math.Min(WorkPlan?.TotalDays ?? 0, value)))) return;
                Raise(nameof(CursorLeft));
                Raise(nameof(CursorDateText));
                UpdateLateness();
                // Meme date dans la simulation 4D du panneau Diagramme.
                var panel = DanCI.BOQ.UI.Chart.ViewChartViewModel.Current;
                if (panel.Day != planCursorDay) panel.Day = planCursorDay;
            }
        }

        public double CursorLeft => WorkPlan == null ? 0 : (CursorDate - WorkPlan.StartDate).TotalDays * ganttPxPerDay;
        private DateTime CursorDate => WorkPlan == null ? DateTime.Today : planCursorDay >= WorkPlan.TotalDays ? WorkPlan.FinishDate
            : new DanCI.BOQ.Core.Planning.WorkCalendar(WorkPlan.StartDate, WorkPlan.WorkingDaysPerWeek, WorkPlan.Holidays).DateOf(planCursorDay);
        public string CursorDateText => WorkPlan == null ? string.Empty : CursorDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR"));

        /// <summary>Glisser une barre : decalage du debut, en jours ouvres (jamais avant les predecesseurs).</summary>
        public void ShiftTask(GanttRowView row, double pixels)
        {
            if (row?.Task == null || WorkPlan == null) return;
            double perWorkingDay = ganttPxPerDay * 7.0 / WorkPlan.WorkingDaysPerWeek;
            int delta = (int)Math.Round(pixels / perWorkingDay);
            if (delta == 0) return;
            int delay = Math.Max(0, row.Task.DelayDays + delta);
            if (delay == 0) savedPlanning.Delays.Remove(row.Task.Id);
            else savedPlanning.Delays[row.Task.Id] = delay;
            PlanningChanged(row.Title + " : décalage " + delay + " j ouvré(s) après ses prédécesseurs.");
        }

        private void OnDurationChanged(GanttRowView row, string text)
        {
            if (row?.Task == null) return;
            string t = (text ?? string.Empty).Replace("j", string.Empty).Trim();
            if (t.Length == 0 || t == row.Task.ComputedDays.ToString(CultureInfo.InvariantCulture))
            {
                if (!savedPlanning.Durations.Remove(row.Task.Id)) return;
                PlanningChanged(row.Title + " : durée calculée rétablie (" + row.Task.ComputedDays + " j).");
                return;
            }
            if (!int.TryParse(t, NumberStyles.Integer, CultureInfo.InvariantCulture, out int d) || d < 1 || d > 2000)
            {
                Status = "Durée « " + text + " » : saisissez un nombre entier de jours ouvrés (1 à 2000), ou laissez vide pour la durée calculée.";
                return;
            }
            savedPlanning.Durations[row.Task.Id] = d;
            PlanningChanged(row.Title + " : durée imposée " + d + " j (calculée : " + row.Task.ComputedDays + " j).");
        }

        private void OnCrewsChanged(CrewRow row, int crews)
        {
            savedPlanning.Crews[row.Lot] = crews;
            PlanningChanged("Lot " + row.Lot + " : " + crews + " équipe(s).");
        }

        /// <summary>Recalcule tout le planning, l'enregistre dans le projet et met a jour la simulation 4D.</summary>
        private void PlanningChanged(string message)
        {
            LoadPlanning(outcome, keepStatus: true);
            if (!string.IsNullOrEmpty(ProjectFilePath)) host.RequestSaveProject(BuildProjectFile());
            Status = message + " Planning recalculé" + (WorkPlan != null ? " : réception le " + WorkPlan.FinishDate.ToString("dd/MM/yyyy", CultureInfo.GetCultureInfo("fr-FR")) : string.Empty) +
                     " — enregistré dans le projet (PDF et Excel suivront).";
        }
        public ObservableCollection<GanttRowView> GanttRows { get; } = new ObservableCollection<GanttRowView>();
        public ObservableCollection<GanttTick> GanttMonths { get; } = new ObservableCollection<GanttTick>();
        public ObservableCollection<GanttTick> GanttWeeks { get; } = new ObservableCollection<GanttTick>();
        public double GanttWidth { get; private set; } = 600;
        public string PlanningSummary { get; private set; } = "Lancez l'analyse : le planning se calcule à partir des quantités du BOQ par niveau.";
        public double GanttFinishLeft { get; private set; }
        public string GanttFinishLabel { get; private set; } = string.Empty;
        public DanCI.BOQ.Core.Planning.WorkPlan WorkPlan { get; private set; }

        // Tresorerie des travaux (montant par mois, courbe en S cumulee)
        public const double CashChartHeight = 120, CashColumnWidth = 64;
        public ObservableCollection<CashMonthRow> CashMonths { get; } = new ObservableCollection<CashMonthRow>();
        public string CashCurvePoints { get; private set; } = string.Empty;
        public double CashChartWidth { get; private set; }
        public bool HasCashFlow => CashMonths.Count > 0;
        public string PlanningHeader => "Planning" + (WorkPlan == null ? string.Empty : " (" + WorkPlan.TotalDays + " j)");

        private void LoadPlanning(AnalysisOutcome result, bool keepStatus = false)
        {
            GanttRows.Clear();
            GanttMonths.Clear();
            GanttWeeks.Clear();
            WorkPlan = null;
            if (result?.Boq != null && result.Boq.AllLines.Any())
            {
                DateTime start = ProjectSchedule?.StartDate?.Date ?? NextMonday();
                WorkPlan = DanCI.BOQ.Core.Planning.WorkPlanner.Build(result.Boq, levelOrder,
                    DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(levelOrder, result.Configuration?.Extraction),
                    result.Configuration?.Planning, start, savedPlanning, result.Inventory);
            }
            if (WorkPlan != null && WorkPlan.Tasks.Count > 0)
            {
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                DateTime d0 = WorkPlan.StartDate, d1 = WorkPlan.FinishDate.AddDays(1);
                double X(DateTime d) => (d - d0).TotalDays * GanttPxPerDay;
                GanttWidth = Math.Max(300, X(d1) + 20);
                for (var m = new DateTime(d0.Year, d0.Month, 1); m < d1; m = m.AddMonths(1))
                {
                    DateTime a = m < d0 ? d0 : m, b = m.AddMonths(1) > d1 ? d1 : m.AddMonths(1);
                    GanttMonths.Add(new GanttTick { Left = X(a), Width = X(b) - X(a), Label = fr.TextInfo.ToTitleCase(m.ToString("MMM yyyy", fr)) });
                }
                for (DateTime w = d0.AddDays(((int)DayOfWeek.Monday - (int)d0.DayOfWeek + 7) % 7); w < d1; w = w.AddDays(7))
                {
                    GanttWeeks.Add(new GanttTick { Left = X(w), Width = 7 * GanttPxPerDay, Label = w.ToString("dd/MM", fr) });
                }
                // Choix des filtres (niveaux, lots) d'apres le planning complet.
                string keepLevel = ganttLevelFilter, keepLot = ganttLotFilter;
                GanttLevelChoices.Clear();
                GanttLevelChoices.Add(AllLevelsFilter);
                foreach (string lv in WorkPlan.Tasks.Select(x => x.Level ?? "(général)").Distinct()) GanttLevelChoices.Add(lv);
                GanttLotChoices.Clear();
                GanttLotChoices.Add(AllLotsFilter);
                foreach (var lg in WorkPlan.Tasks.GroupBy(x => x.Lot).OrderBy(g => g.Key, StringComparer.Ordinal)) GanttLotChoices.Add("Lot " + lg.Key + " — " + lg.First().LotDesignation);
                ganttLevelFilter = GanttLevelChoices.Contains(keepLevel) ? keepLevel : AllLevelsFilter;
                ganttLotFilter = GanttLotChoices.Contains(keepLot) ? keepLot : AllLotsFilter;
                Raise(nameof(GanttLevelFilter));
                Raise(nameof(GanttLotFilter));
                var filtered = DanCI.BOQ.Core.Planning.GanttRows.Build(WorkPlan).Where(r => r.IsHeader || PassesGanttFilter(r.Task)).ToList();
                filtered = filtered.Where((r, i) => !r.IsHeader || (i + 1 < filtered.Count && !filtered[i + 1].IsHeader)).ToList();
                foreach (var r in filtered)
                {
                    if (r.IsHeader)
                    {
                        GanttRows.Add(new GanttRowView { IsHeader = true, Title = r.Title });
                        continue;
                    }
                    var t = r.Task;
                    var view = new GanttRowView
                    {
                        Task = t,
                        IsImposed = t.IsDurationImposed,
                        DelayText = t.DelayDays > 0 ? "+" + t.DelayDays + " j" : string.Empty,
                        Title = "Lot " + t.Lot + (t.Level != null ? " · " + t.Level : string.Empty) + " — " + t.LotDesignation +
                                (t.IsDurationImposed ? "  ✎" : string.Empty),
                        Duration = t.DurationDays + " j",
                        Start = t.Start.ToString("dd/MM/yy", fr),
                        Finish = t.Finish.ToString("dd/MM/yy", fr),
                        Float = t.IsCritical ? "critique" : t.FloatDays + " j",
                        BarLeft = X(t.Start),
                        BarWidth = Math.Max(3, X(t.Finish.AddDays(1)) - X(t.Start)),
                        Color = "#FF" + t.Color.TrimStart('#'),
                        IsCritical = t.IsCritical,
                        Tooltip = t.Name + "\n" + t.Start.ToString("dddd dd/MM/yyyy", fr) + " → " + t.Finish.ToString("dddd dd/MM/yyyy", fr) +
                                  (t.Predecessors.Count > 0 ? "\nAprès : " + string.Join(", ", t.Predecessors.Select(p => p.Replace("@", " · "))) : string.Empty) +
                                  (t.DelayDays > 0 ? "\nDécalage imposé : " + t.DelayDays + " j" : string.Empty) +
                                  "\n" + t.Basis + "\nGlisser la barre : décaler la tâche. Clic : voir ses éléments dans Revit."
                    };
                    view.DurationInput = t.DurationDays.ToString(CultureInfo.InvariantCulture);
                    view.DurationChanged = OnDurationChanged;
                    view.ProgressInput = t.ActualPercent.HasValue ? t.ActualPercent.Value.ToString("0", CultureInfo.InvariantCulture) : string.Empty;
                    view.ProgressChanged = OnProgressChanged;
                    view.ProgressWidth = t.ActualPercent.HasValue ? view.BarWidth * t.ActualPercent.Value / 100.0 : 0;
                    if (t.BaselineStart.HasValue && t.BaselineFinish.HasValue)
                    {
                        view.HasBaseline = true;
                        view.BaselineLeft = X(t.BaselineStart.Value);
                        view.BaselineWidth = Math.Max(3, X(t.BaselineFinish.Value.AddDays(1)) - X(t.BaselineStart.Value));
                        int slip = t.SlipDays ?? 0;
                        view.SlipText = slip == 0 ? "= réf." : (slip > 0 ? "+" : "") + slip + " j / réf.";
                    }
                    GanttRows.Add(view);
                }
                // Liens de precedence entre les taches visibles (fin -> debut), coudes, fleche ; rouges sur le chemin critique.
                GanttLinks.Clear();
                var index = new Dictionary<string, int>();
                for (int i = 0; i < GanttRows.Count; i++) if (GanttRows[i].Task != null) index[GanttRows[i].Task.Id] = i;
                var inv = CultureInfo.InvariantCulture;
                foreach (var row in GanttRows.Where(x => x.Task != null))
                {
                    foreach (string pid in row.Task.Predecessors)
                    {
                        if (!index.TryGetValue(pid, out int pi)) continue;
                        var pred = GanttRows[pi];
                        double x1 = pred.BarLeft + pred.BarWidth, y1 = pi * 26 + 12, x2 = row.BarLeft, y2 = index[row.Task.Id] * 26 + 12;
                        double xm = Math.Max(x1 + 4, Math.Min(x2 - 5, x1 + 8));
                        bool critical = pred.Task.IsCritical && row.Task.IsCritical;
                        GanttLinks.Add(new GanttLinkView
                        {
                            Data = string.Format(inv, "M {0:0.#},{1:0.#} H {2:0.#} V {3:0.#} H {4:0.#}", x1, y1, xm, y2, x2 - 1),
                            Arrow = string.Format(inv, "M {0:0.#},{1:0.#} L {2:0.#},{3:0.#} L {0:0.#},{4:0.#} Z", x2 - 6, y2 - 3.5, x2, y2, y2 + 3.5),
                            Color = critical ? "#FFFF4D6D" : "#FF8A99AD",
                            Thickness = critical ? 1.6 : 1
                        });
                    }
                }
                GanttFinishLeft = X(d1) - 6;
                GanttFinishLabel = "Réception " + WorkPlan.FinishDate.ToString("dd/MM/yyyy", fr);
                PlanningSummary = "Du " + d0.ToString("dddd dd/MM/yyyy", fr) + " au " + WorkPlan.FinishDate.ToString("dddd dd/MM/yyyy", fr) + " — " +
                                  WorkPlan.TotalDays + " jours ouvrés (" + WorkPlan.WorkingDaysPerWeek + " j/semaine), " + WorkPlan.CalendarDays + " jours calendaires, " +
                                  WorkPlan.Tasks.Count + " tâches dont " + WorkPlan.Tasks.Count(x => x.IsCritical) + " critiques." +
                                  (WorkPlan.Notes.Count > 0 ? " " + string.Join(" ", WorkPlan.Notes) : string.Empty);
            }
            GanttMilestones.Clear();
            if (WorkPlan != null && WorkPlan.Tasks.Count > 0)
            {
                int k = 0;
                foreach (var m in WorkPlan.Milestones.OrderBy(x => x.Date))
                {
                    GanttMilestones.Add(new GanttMilestoneView
                    {
                        Left = (m.Date.AddDays(1) - WorkPlan.StartDate).TotalDays * ganttPxPerDay - 7,
                        Top = (k++ % 3) * 18,
                        Label = m.Name + " · " + m.Date.ToString("dd/MM/yy", CultureInfo.GetCultureInfo("fr-FR")),
                        Tooltip = m.Name + " — " + m.Date.ToString("dddd dd MMMM yyyy", CultureInfo.GetCultureInfo("fr-FR"))
                    });
                }
            }
            CrewRows.Clear();
            if (WorkPlan != null)
            {
                foreach (var g in WorkPlan.Tasks.Where(t => t.Lot != "00").GroupBy(t => t.Lot).OrderBy(g => g.Key, StringComparer.Ordinal))
                {
                    var c = new CrewRow { Lot = g.Key, Label = "Lot " + g.Key + " — " + g.First().LotDesignation, Color = "#FF" + g.First().Color.TrimStart('#') };
                    c.Init(g.First().Crews);
                    c.Changed = OnCrewsChanged;
                    CrewRows.Add(c);
                }
                planCursorDay = Math.Min(planCursorDay, WorkPlan.TotalDays);
                DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.SetPlan(WorkPlan);
                if (!panelLinked)
                {
                    panelLinked = true;
                    // La simulation 4D du panneau fait avancer le curseur du Gantt (et inversement).
                    DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.PropertyChanged += (o, e) =>
                    {
                        if (e.PropertyName == nameof(DanCI.BOQ.UI.Chart.ViewChartViewModel.Day))
                        {
                            int d = DanCI.BOQ.UI.Chart.ViewChartViewModel.Current.Day;
                            if (d != planCursorDay) PlanCursorDay = d;
                        }
                    };
                }
                UpdateLateness();
            }
            Raise(nameof(CursorLeft));
            Raise(nameof(CursorDateText));
            Raise(nameof(PlanCursorDay));
            CashMonths.Clear();
            CashCurvePoints = string.Empty;
            if (WorkPlan != null && WorkPlan.TotalAmount > 0m)
            {
                var fr = CultureInfo.GetCultureInfo("fr-FR");
                var months = WorkPlan.CashFlow();
                decimal max = months.Max(m => m.Amount);
                var pts = new List<string> { "0," + CashChartHeight.ToString(CultureInfo.InvariantCulture) };
                for (int i = 0; i < months.Count; i++)
                {
                    var m = months[i];
                    CashMonths.Add(new CashMonthRow
                    {
                        Month = fr.TextInfo.ToTitleCase(m.Month.ToString("MMM yy", fr)),
                        Amount = m.Amount.ToString("N0", fr),
                        Cumulative = m.Cumulative.ToString("N0", fr),
                        Share = (m.CumulativeShare * 100m).ToString("0", fr) + " %",
                        BarHeight = max > 0 ? (double)(m.Amount / max) * (CashChartHeight - 10) : 0
                    });
                    double x = (i + 1) * CashColumnWidth, y = CashChartHeight - (double)m.CumulativeShare * (CashChartHeight - 6);
                    pts.Add(x.ToString("0.#", CultureInfo.InvariantCulture) + "," + y.ToString("0.#", CultureInfo.InvariantCulture));
                }
                CashCurvePoints = string.Join(" ", pts);
                CashChartWidth = months.Count * CashColumnWidth;
            }
            foreach (string p in new[] { nameof(CashCurvePoints), nameof(CashChartWidth), nameof(HasCashFlow) }) Raise(p);
            foreach (string p in new[] { nameof(GanttWidth), nameof(PlanningSummary), nameof(GanttFinishLeft), nameof(GanttFinishLabel), nameof(WorkPlan), nameof(PlanningHeader) })
            {
                Raise(p);
            }
        }

        private static DateTime NextMonday()
        {
            DateTime d = DateTime.Today.AddDays(1);
            while (d.DayOfWeek != DayOfWeek.Monday) d = d.AddDays(1);
            return d;
        }

        private void LoadLayers(IReadOnlyList<ElementQuantity> layers)
        {
            LayerGroups.Clear();
            SelectedLayerGroup = null;
            var rows = DanCI.BOQ.Core.Extraction.LayerSummary.Build(layers ?? new ElementQuantity[0]);
            foreach (var r in rows)
            {
                LayerGroups.Add(new LayerGroupRow(r));
            }
            LayersTotalText = rows.Count == 0
                ? "Aucun mur, sol, toit ou plafond composite dans la portée analysée (ou décomposition désactivée dans extraction.json, « couches »)."
                : rows.Count + " matériau(x) dans " + rows.Sum(r => r.LayerCount) + " couche(s) d'éléments — total " +
                  rows.Sum(r => r.AreaM2).ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " m², " +
                  rows.Sum(r => r.VolumeM3).ToString("N3", CultureInfo.GetCultureInfo("fr-FR")) + " m³ (tous matériaux confondus).";
            Raise(nameof(LayersHeader));
            Raise(nameof(LayersTotalText));
        }

        public string FooterText => outcome == null ? string.Empty :
            outcome.DocumentTitle + " — portée : " + outcome.ScopeDescription + " — " + outcome.Quantities.Count +
            " quantités — " + outcome.Duration.TotalSeconds.ToString("0.0", Fr) + " s — lecture seule, modèle non modifié";

        public ICommand AnalyzeCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand SelectLineCommand { get; }
        public ICommand SelectContributionCommand { get; }
        public ICommand SelectFindingCommand { get; }
        public ICommand SelectExcludedCommand { get; }

        /// <summary>Niveaux dans l'ordre des altitudes (onglet « Par niveau »).</summary>
        private List<string> levelOrder = new List<string>();

        public void SetLevels(IEnumerable<string> names)
        {
            levelOrder = (names ?? Enumerable.Empty<string>()).Distinct().ToList();
            var previous = new HashSet<string>(Levels.Where(l => l.IsChecked).Select(l => l.Name));
            Levels.Clear();
            foreach (string n in names.Distinct().OrderBy(n => n, StringComparer.CurrentCulture))
            {
                Levels.Add(new LevelItem { Name = n, IsChecked = previous.Contains(n) });
            }
        }

        public AnalysisScope CurrentScope() => new AnalysisScope
        {
            Mode = SelectedScope?.Mode ?? ScopeMode.Modele,
            Levels = Levels.Where(l => l.IsChecked).Select(l => l.Name).ToList(),
            Phase = SelectedPhase == AllPhases ? null : SelectedPhase,
            IncludeDemolition = IncludeDemolition,
            Links = Links.Where(l => l.IsChecked).Select(l => l.Name).ToList()
        };

        /// <summary>Affiche le resultat d'une analyse (appele par l'hote, sur le fil de la fenetre).</summary>
        public void Apply(AnalysisOutcome result)
        {
            IsBusy = false;
            if (result == null)
            {
                return;
            }
            if (result.Failure != null)
            {
                Status = result.Failure;
                if (result.Boq == null)
                {
                    return;
                }
            }
            outcome = result;
            Lines.Clear();
            Findings.Clear();
            Excluded.Clear();
            string currency = result.Boq?.Currency.ToString() ?? "USD";
            if (result.Boq != null)
            {
                foreach (BoqLotResult lot in result.Boq.Lots)
                {
                    foreach (BoqLine line in lot.Lines)
                    {
                        Lines.Add(new LineRow(lot, line, currency));
                    }
                }
                foreach (ExcludedQuantity x in result.Boq.Excluded)
                {
                    Excluded.Add(new ExcludedRow(x));
                }
            }
            foreach (var f in result.Audit?.Findings ?? new List<DanCI.BOQ.Core.Audit.AuditFinding>())
            {
                Findings.Add(new FindingRow(f));
            }
            SelectedLine = null;
            LoadLayers(result.Layers);
            Settings.SetVrdTypes(result.Boq);
            LoadInventory(result);
            LoadLevels(result);
            LoadLevelBoq(result);
            LoadPlanning(result);
            Dashboard = result.Boq == null ? DashboardViewModel.Empty : DashboardViewModel.From(
                DanCI.BOQ.Core.Dashboard.BoqDashboard.Build(result.Boq, result.Audit, result.Quantities, ProjectIdentity,
                    new DanCI.BOQ.Core.Dashboard.DashboardOptions { ExchangeRateDate = result.Configuration?.Settings?.ExchangeRate?.Date }),
                ProjectIdentity?.Name ?? result.DocumentTitle);
            if (result.Failure == null)
            {
                Status = "Analyse terminée : " + Lines.Count + " ligne(s), " + Findings.Count + " constat(s) d'audit.";
            }
            foreach (string p in new[] { nameof(Currency), nameof(WorksTotal), nameof(Markups), nameof(TotalExcludingVat), nameof(Vat),
                                         nameof(TotalIncludingVat), nameof(SecondaryTotal), nameof(AmountWords), nameof(IsIncomplete), nameof(IncompleteText),
                                         nameof(ReliabilityText), nameof(AuditHeader), nameof(ExcludedHeader), nameof(FooterText), nameof(Fingerprint) })
            {
                Raise(p);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void Analyze()
        {
            ExtractionChangedSinceAnalysis = false;
            IsBusy = true;
            Status = "Analyse en cours…";
            host.RequestAnalysis(CurrentScope(), CurrentManualLines());
        }

        private void Select(IReadOnlyCollection<ElementRef> ids)
        {
            if (ids == null || ids.Count == 0)
            {
                return;
            }
            Status = "Sélection de " + ids.Count + " élément(s) dans Revit…";
            host.RequestSelection(ids);
        }

        private string Money(decimal? value) => value.HasValue ? value.Value.ToString("N2", Fr) + " " + Currency : string.Empty;
    }
}
