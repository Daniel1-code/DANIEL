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
        public const int SettingsTab = 8;

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
            Extraction = savedExtraction
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

        public IReadOnlyList<ContributionRow> Contributions => SelectedLine?.Contributions ?? new ContributionRow[0];

        public string SelectedLineInfo => SelectedLine == null
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
                Raise(nameof(LevelSummaryText));
                Raise(nameof(HasLevelLayers));
            }
        }

        public bool HasLevelLayers => LevelLayers.Count > 0;

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
