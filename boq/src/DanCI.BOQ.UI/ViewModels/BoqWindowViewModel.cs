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
            FindingsView = CollectionViewSource.GetDefaultView(Findings);
            FindingsView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(FindingRow.SectionTitle)));

            AnalyzeCommand = new RelayCommand(_ => Analyze(), _ => !IsBusy);
            ExportCommand = new RelayCommand(_ => host.RequestExport(), _ => !IsBusy && outcome?.Boq != null);
            WriteParametersCommand = new RelayCommand(_ => host.RequestWriteParameters(), _ => !IsBusy && outcome?.Boq != null);
            SelectLineCommand = new RelayCommand(p => Select((p as LineRow)?.ElementRefs), p => p is LineRow);
            SelectContributionCommand = new RelayCommand(p => Select(p is ContributionRow c ? new[] { c.Ref } : null), p => p is ContributionRow);
            SelectFindingCommand = new RelayCommand(p => Select((p as FindingRow)?.ElementRefs), p => p is FindingRow f && f.CanSelect);
            SelectExcludedCommand = new RelayCommand(p => Select(p is ExcludedRow x ? new[] { x.Ref } : null), p => p is ExcludedRow);
            AddManualCommand = new RelayCommand(_ => ManualLines.Add(new ManualLineRow()));
            RemoveManualCommand = new RelayCommand(p => ManualLines.Remove(p as ManualLineRow), p => p is ManualLineRow);
            SaveManualCommand = new RelayCommand(_ => SaveProject(recompute: true), _ => !IsBusy);
            InitValidationCommands();
            InitPriceCommands();
        }

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
            PriceLibrary = RelativeToProject(PriceLibraryPath)
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

        public ICommand ValidationTemplateCommand { get; private set; }
        public ICommand CompareManualCommand { get; private set; }
        public ICommand CompareRunsCommand { get; private set; }

        /// <summary>Empreinte du metre de la derniere analyse (forme courte).</summary>
        public string Fingerprint => outcome == null ? "—" : DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(
            DanCI.BOQ.Core.Validation.QuantityFingerprint.Compute(outcome.Quantities));

        private void InitValidationCommands()
        {
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

        public string FooterText => outcome == null ? string.Empty :
            outcome.DocumentTitle + " — portée : " + outcome.ScopeDescription + " — " + outcome.Quantities.Count +
            " quantités — " + outcome.Duration.TotalSeconds.ToString("0.0", Fr) + " s — lecture seule, modèle non modifié";

        public ICommand AnalyzeCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand SelectLineCommand { get; }
        public ICommand SelectContributionCommand { get; }
        public ICommand SelectFindingCommand { get; }
        public ICommand SelectExcludedCommand { get; }

        public void SetLevels(IEnumerable<string> names)
        {
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
            if (result.Failure == null)
            {
                Status = "Analyse terminée : " + Lines.Count + " ligne(s), " + Findings.Count + " constat(s) d'audit.";
            }
            foreach (string p in new[] { nameof(Currency), nameof(WorksTotal), nameof(Markups), nameof(TotalExcludingVat), nameof(Vat),
                                         nameof(TotalIncludingVat), nameof(SecondaryTotal), nameof(IsIncomplete), nameof(IncompleteText),
                                         nameof(ReliabilityText), nameof(AuditHeader), nameof(ExcludedHeader), nameof(FooterText), nameof(Fingerprint) })
            {
                Raise(p);
            }
            CommandManager.InvalidateRequerySuggested();
        }

        private void Analyze()
        {
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
