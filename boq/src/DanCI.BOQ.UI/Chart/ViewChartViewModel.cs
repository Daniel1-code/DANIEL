using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Windows.Input;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.Mvvm;

namespace DanCI.BOQ.UI.Chart
{
    /// <summary>
    /// Ce que le panneau « Diagramme » demande a Revit (implementation dans l'Addin, via ExternalEvent).
    /// </summary>
    public interface IChartHost
    {
        /// <summary>Isole temporairement ces elements (modele hote) dans la vue active.</summary>
        void RequestIsolate(IReadOnlyCollection<long> hostElementIds, string label);

        /// <summary>Selection et zoom dans Revit (elements des liens compris).</summary>
        void RequestChartSelection(IReadOnlyCollection<ElementRef> elements);

        /// <summary>Annule l'isolement temporaire de la vue active.</summary>
        void RequestResetIsolation();

        /// <summary>Vue 3D coloree (cree une vue : confirmation demandee).</summary>
        void RequestColorize(ColoringMode mode);

        /// <summary>Affiche (ou retire) les barres 3D dessinees dans la vue, sans rien ecrire dans le modele.</summary>
        void RequestChart3D(bool show);

        /// <summary>Ouvre la fenetre Metre &amp; BOQ (pour lancer l'analyse).</summary>
        void RequestOpenBoq();
    }

    public sealed class ChartBarRow
    {
        public const double BarMax = 200.0;

        public ChartBar Bar { get; set; }
        public string Label { get; set; }
        public string Designation { get; set; }
        public string Color { get; set; }
        public string ValueText { get; set; }
        public string ShareText { get; set; }
        public double BarWidth { get; set; }
        public string QuantitiesText { get; set; }
        public string ElementsText { get; set; }
        /// <summary>Types Revit de la barre (et, en VRD, le mot-cle qui les a classes).</summary>
        public string TypesText { get; set; }

        // Mini-Gantt (simulation 4D) : position de la tache sur la ligne de temps, avancement, curseur.
        public bool IsTimeline { get; set; }
        public bool IsBarChart => !IsTimeline;
        public double TimelineLeft { get; set; }
        public double TimelineWidth { get; set; }
        public double TimelineDone { get; set; }
        public double CursorX { get; set; }
        public bool IsRunning { get; set; }
        public string Tooltip { get; set; }
        public int Position { get; set; }
    }

    /// <summary>Panneau « Diagramme du métré » ancre dans Revit, a cote de la vue 3D.</summary>
    public sealed class ViewChartViewModel : ObservableObject
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Panneau unique de la session Revit (cree au demarrage de l'extension).</summary>
        public static ViewChartViewModel Current { get; } = new ViewChartViewModel();

        private IChartHost host;
        private BoqResult boq;
        private string currency = string.Empty;
        private ColoringMode mode = ColoringMode.Lot;
        private ChartGroup group = ChartGroup.Lot;
        private IReadOnlyList<ElementQuantity> inventory = new ElementQuantity[0];
        private IReadOnlyList<string> levelOrder = new string[0];
        private string levelFilter;
        private string groundLevel;
        private DanCI.BOQ.Core.Planning.WorkPlan plan;
        private int day;
        private readonly System.Windows.Threading.DispatcherTimer player = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1300) };
        private string lastShown;
        private ChartMetric metric = ChartMetric.Amount;
        private bool metricChosen;
        private bool show3D;
        private string status = "Lancez l'analyse (Métré & BOQ) : le diagramme se remplit avec les quantités du BOQ.";

        public ViewChartViewModel()
        {
            IsolateCommand = new RelayCommand(p => Isolate(p as ChartBarRow), p => host != null && p is ChartBarRow r && r.Bar.HostElementIds.Count > 0);
            SelectCommand = new RelayCommand(p => Select(p as ChartBarRow), p => host != null && p is ChartBarRow r && r.Bar.ElementCount > 0);
            ResetCommand = new RelayCommand(_ => host?.RequestResetIsolation(), _ => host != null);
            ColorizeCommand = new RelayCommand(_ => host?.RequestColorize(mode), _ => host != null && HasData && CanColorize);
            OpenBoqCommand = new RelayCommand(_ => host?.RequestOpenBoq(), _ => host != null);
            SetModeCommand = new RelayCommand(p => SetGroup(p as string));
            SetMetricCommand = new RelayCommand(p => { metricChosen = true; Metric = ParseMetric(p as string); });
            PlayCommand = new RelayCommand(_ => TogglePlay(), _ => plan != null && plan.TotalDays > 0);
            StepCommand = new RelayCommand(p => Day = Math.Max(0, Math.Min(MaxDay, day + (p as string == "-" ? -StepDays : StepDays))), _ => plan != null);
            ShowDateCommand = new RelayCommand(_ => ShowBuilt(force: true), _ => host != null && plan != null);
            player.Tick += (s2, e2) =>
            {
                if (day >= MaxDay) { TogglePlay(); return; }
                Day = Math.Min(MaxDay, day + StepDays);
            };
        }

        public ObservableCollection<ChartBarRow> Rows { get; } = new ObservableCollection<ChartBarRow>();
        public ViewChart Chart { get; private set; }

        public ICommand IsolateCommand { get; }
        public ICommand SelectCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand ColorizeCommand { get; }
        public ICommand OpenBoqCommand { get; }
        public ICommand SetModeCommand { get; }
        public ICommand SetMetricCommand { get; }

        public bool HasData => boq != null && Rows.Count > 0;
        public bool HasNoData => !HasData;

        public string Status
        {
            get => status;
            set => Set(ref status, value);
        }

        public ColoringMode Mode
        {
            get => mode;
            set
            {
                if (Set(ref mode, value))
                {
                    RaiseModeFlags();
                    Rebuild();
                }
            }
        }

        public bool IsLotMode => group == ChartGroup.Lot;
        public bool IsStatusMode => group == ChartGroup.Statut;
        public bool IsPriceMode => group == ChartGroup.Prix;
        public bool IsLevelMode => group == ChartGroup.Niveau;
        public bool IsMaterialMode => group == ChartGroup.Materiau;
        public bool IsPlanningMode => group == ChartGroup.Planning;
        public bool IsNotPlanningMode => !IsPlanningMode;

        // ------------------------------------------------------------ simulation 4D
        public ICommand PlayCommand { get; }
        public ICommand StepCommand { get; }
        public ICommand ShowDateCommand { get; }

        /// <summary>Pas de la lecture et des boutons : une semaine de travail.</summary>
        public int StepDays => plan?.WorkingDaysPerWeek ?? 6;
        public int MaxDay => plan?.TotalDays ?? 0;
        public bool IsPlaying => player.IsEnabled;
        public string PlayLabel => player.IsEnabled ? "⏸ Pause" : "▶ Lecture";
        public bool HasPlan => plan != null && plan.Tasks.Count > 0;

        /// <summary>Jour ouvre affiche (0 = demarrage) ; la vue 3D suit (isolement temporaire).</summary>
        public int Day
        {
            get => day;
            set
            {
                int v = Math.Max(0, Math.Min(MaxDay, value));
                if (!Set(ref day, v)) return;
                Raise(nameof(DateText));
                if (group == ChartGroup.Planning) Rebuild();
            }
        }

        public string DateText => plan == null ? string.Empty
            : DateAt(day).ToString("dddd dd MMMM yyyy", Fr) + "  —  jour " + day + " / " + plan.TotalDays;

        private DateTime DateAt(int d) =>
            d >= plan.TotalDays ? plan.FinishDate : new DanCI.BOQ.Core.Planning.WorkCalendar(plan.StartDate, plan.WorkingDaysPerWeek, plan.Holidays).DateOf(d);

        /// <summary>Planning a simuler (calcule par la fenetre ou l'Addin).</summary>
        public void SetPlan(DanCI.BOQ.Core.Planning.WorkPlan workPlan)
        {
            plan = workPlan;
            day = Math.Min(day, MaxDay);
            foreach (string n in new[] { nameof(MaxDay), nameof(HasPlan), nameof(DateText), nameof(Day), nameof(StepDays) }) Raise(n);
            if (group == ChartGroup.Planning) Rebuild();
        }

        private void TogglePlay()
        {
            if (player.IsEnabled) player.Stop();
            else
            {
                if (day >= MaxDay) Day = 0;
                player.Start();
            }
            Raise(nameof(IsPlaying));
            Raise(nameof(PlayLabel));
        }

        /// <summary>Vue 3D a la date : elements des taches finies ou en cours (isolement temporaire).</summary>
        private void ShowBuilt(bool force = false)
        {
            if (host == null || plan == null) return;
            var ids = plan.BuiltAt(day).Where(r => !r.IsLinked).Select(r => r.ElementId).ToList();
            string signature = ids.Count + ":" + string.Join(",", ids.Take(50));
            if (!force && signature == lastShown) return;
            lastShown = signature;
            if (ids.Count == 0)
            {
                Status = "Au " + DateAt(day).ToString("dd/MM/yyyy", Fr) + " : rien n'est encore construit (installation de chantier).";
                return;
            }
            host.RequestIsolate(ids, "Planning au " + DateAt(day).ToString("dd/MM/yyyy", Fr));
        }

        /// <summary>Ligne a montrer pendant la lecture (premiere tache en cours) : le panneau y fait defiler la liste.</summary>
        public int FocusIndex { get; private set; } = -1;

        private void BuildPlanningRows()
        {
            const double width = ChartBarRow.BarMax;
            double scale = width / Math.Max(1, plan.TotalDays);
            int focus = -1;
            int i = 1;
            foreach (var t in plan.Tasks.OrderBy(x => x.StartDay).ThenBy(x => x.Lot, StringComparer.Ordinal))
            {
                var state = DanCI.BOQ.Core.Planning.WorkPlan.StateAt(t, day);
                double progress = DanCI.BOQ.Core.Planning.WorkPlan.ProgressAt(t, day);
                var bar = new ChartBar { Key = t.Id, Label = t.Name, Color = t.Color };
                foreach (var r in t.Elements)
                {
                    if (r.IsLinked) bar.LinkedElements.Add(r); else bar.HostElementIds.Add(r.ElementId);
                }
                if (focus < 0 && state == DanCI.BOQ.Core.Planning.TaskState.InProgress) focus = i - 1;
                Rows.Add(new ChartBarRow
                {
                    IsTimeline = true,
                    TimelineLeft = t.StartDay * scale,
                    TimelineWidth = Math.Max(2, t.DurationDays * scale),
                    TimelineDone = Math.Max(0, Math.Min(t.DurationDays, day - t.StartDay)) * scale,
                    CursorX = Math.Min(width, day * scale),
                    IsRunning = state == DanCI.BOQ.Core.Planning.TaskState.InProgress,
                    Bar = bar,
                    Position = i++,
                    Label = (state == DanCI.BOQ.Core.Planning.TaskState.Done ? "✓ " : state == DanCI.BOQ.Core.Planning.TaskState.InProgress ? "▶ " : "· ") +
                            "Lot " + t.Lot + (t.Level != null ? " · " + t.Level : string.Empty),
                    Designation = t.LotDesignation + (t.IsCritical ? "  (critique)" : string.Empty),
                    Color = state == DanCI.BOQ.Core.Planning.TaskState.NotStarted ? "#FF5F7AA0" : "#FF" + t.Color.TrimStart('#'),
                    ValueText = (progress * 100).ToString("0", Fr) + " %",
                    ShareText = state == DanCI.BOQ.Core.Planning.TaskState.Done ? "terminé" : state == DanCI.BOQ.Core.Planning.TaskState.InProgress ? "en cours" : "à venir",
                    BarWidth = progress * ChartBarRow.BarMax,
                    QuantitiesText = t.Start.ToString("dd/MM/yy", Fr) + " → " + t.Finish.ToString("dd/MM/yy", Fr) + " · " + t.DurationDays + " j",
                    ElementsText = t.Elements.Count + " élément(s)" + (t.AttachedElements > 0 ? " dont " + t.AttachedElements + " hors BOQ (génériques, familles…)" : string.Empty),
                    Tooltip = "Clic : isoler les éléments de cette tâche.\n" + t.Basis
                });
            }
            FocusIndex = focus;
            Raise(nameof(FocusIndex));
            int done = plan.Tasks.Count(t => DanCI.BOQ.Core.Planning.WorkPlan.StateAt(t, day) == DanCI.BOQ.Core.Planning.TaskState.Done);
            int running = plan.Tasks.Count(t => DanCI.BOQ.Core.Planning.WorkPlan.StateAt(t, day) == DanCI.BOQ.Core.Planning.TaskState.InProgress);
            decimal spent = plan.Tasks.Sum(t => t.Amount * (decimal)DanCI.BOQ.Core.Planning.WorkPlan.ProgressAt(t, day));
            TotalText = DateAt(day).ToString("dd/MM/yyyy", Fr) + " · " + done + " tâche(s) terminée(s), " + running + " en cours · avancement " +
                        (plan.TotalAmount > 0 ? (spent / plan.TotalAmount * 100m).ToString("0", Fr) + " % du montant" : (day * 100 / Math.Max(1, plan.TotalDays)) + " % du délai");
            LegendText = string.Empty;
            Status = "Faites glisser la date ou cliquez ▶ : la vue 3D montre ce qui est construit (tâches terminées et en cours) — isolement temporaire, « Afficher tout » pour revenir.";
        }
        public ChartGroup Group => group;
        public bool CanColorize => group == ChartGroup.Lot || group == ChartGroup.Statut || group == ChartGroup.Prix;
        public bool HasAmountMetric => group != ChartGroup.Materiau && group != ChartGroup.Planning;
        public bool HasInventoryMetrics => group == ChartGroup.Materiau || group == ChartGroup.Niveau;
        public bool HasMetrics => group != ChartGroup.Planning;

        /// <summary>Niveaux proposes en mode Materiaux (le premier = tous).</summary>
        public ObservableCollection<string> LevelChoices { get; } = new ObservableCollection<string>();
        public const string AllLevels = "Tous les niveaux (3D : une rangée par niveau)";

        public string LevelChoice
        {
            get => levelFilter ?? AllLevels;
            set
            {
                string v = value == AllLevels ? null : value;
                if (levelFilter == v) return;
                levelFilter = v;
                Raise();
                Rebuild();
            }
        }

        /// <summary>Niveau filtre en mode Materiaux ; null = tous.</summary>
        public string LevelFilter => levelFilter;
        public IReadOnlyList<ElementQuantity> Inventory => inventory;
        public IReadOnlyList<string> LevelOrder => levelOrder;

        private void SetGroup(string p)
        {
            ChartGroup g;
            switch (p)
            {
                case "statut": g = ChartGroup.Statut; break;
                case "prix": g = ChartGroup.Prix; break;
                case "niveau": g = ChartGroup.Niveau; break;
                case "materiaux": g = ChartGroup.Materiau; break;
                case "planning": g = ChartGroup.Planning; break;
                default: g = ChartGroup.Lot; break;
            }
            if (g == group) return;
            group = g;
            if (g != ChartGroup.Planning && player.IsEnabled) TogglePlay();
            lastShown = null;
            if (g == ChartGroup.Lot || g == ChartGroup.Statut || g == ChartGroup.Prix)
            {
                mode = g == ChartGroup.Statut ? ColoringMode.Reliability : g == ChartGroup.Prix ? ColoringMode.Price : ColoringMode.Lot;
                if (metric == ChartMetric.Volume || metric == ChartMetric.Area) metric = DefaultMetric();
            }
            else if (g == ChartGroup.Materiau && metric == ChartMetric.Amount)
            {
                metric = ChartMetric.Volume;
            }
            foreach (string n in new[] { nameof(IsLotMode), nameof(IsStatusMode), nameof(IsPriceMode), nameof(IsLevelMode), nameof(IsMaterialMode), nameof(IsPlanningMode), nameof(IsNotPlanningMode),
                                         nameof(CanColorize), nameof(HasAmountMetric), nameof(HasInventoryMetrics), nameof(HasMetrics), nameof(Metric),
                                         nameof(IsAmountMetric), nameof(IsElementsMetric), nameof(IsVolumeMetric), nameof(IsAreaMetric) })
            {
                Raise(n);
            }
            Rebuild();
        }

        private ChartMetric DefaultMetric() => boq != null && ViewChart.Build(boq, mode).HasAmounts ? ChartMetric.Amount : ChartMetric.Elements;

        private static ChartMetric ParseMetric(string p)
        {
            switch (p)
            {
                case "elements": return ChartMetric.Elements;
                case "volume": return ChartMetric.Volume;
                case "surface": return ChartMetric.Area;
                default: return ChartMetric.Amount;
            }
        }

        public ChartMetric Metric
        {
            get => metric;
            set
            {
                if (Set(ref metric, value))
                {
                    Raise(nameof(IsAmountMetric));
                    Raise(nameof(IsElementsMetric));
                    Raise(nameof(IsVolumeMetric));
                    Raise(nameof(IsAreaMetric));
                    Rebuild();
                }
            }
        }

        public bool IsAmountMetric => metric == ChartMetric.Amount;
        public bool IsElementsMetric => metric == ChartMetric.Elements;
        public bool IsVolumeMetric => metric == ChartMetric.Volume;
        public bool IsAreaMetric => metric == ChartMetric.Area;

        /// <summary>Barres 3D dans la vue (affichage temporaire, rien n'est ecrit dans le modele).</summary>
        public bool Show3D
        {
            get => show3D;
            set
            {
                if (Set(ref show3D, value))
                {
                    host?.RequestChart3D(value);
                }
            }
        }

        public string Title { get; private set; } = "DIAGRAMME DU MÉTRÉ";
        public string TotalText { get; private set; } = string.Empty;
        public string LegendText { get; private set; } = string.Empty;

        /// <summary>Branche le panneau sur Revit (appele par l'Addin quand l'ExternalEvent est pret).</summary>
        public void Attach(IChartHost chartHost) => host = chartHost;

        /// <summary>Nouveau BOQ (apres analyse ou recalcul) ; null = plus de donnees.</summary>
        public void SetBoq(BoqResult result, string currencyCode, IReadOnlyList<ElementQuantity> materials = null, IReadOnlyList<string> levels = null,
            string ground = null)
        {
            groundLevel = ground;
            inventory = materials ?? new ElementQuantity[0];
            levelOrder = levels ?? new string[0];
            string keep = levelFilter;
            LevelChoices.Clear();
            LevelChoices.Add(AllLevels);
            foreach (string l in DanCI.BOQ.Core.Engine.LevelBreakdown.Build(result, levelOrder, null, inventory, null, groundLevel).Select(b => b.Level)) LevelChoices.Add(l);
            levelFilter = keep != null && LevelChoices.Contains(keep) ? keep : null;
            Raise(nameof(LevelChoice));
            boq = result;
            currency = currencyCode ?? string.Empty;
            if (boq != null && !metricChosen)
            {
                // Sans prix, la hauteur des barres porte le nombre d'elements.
                Metric = ViewChart.Build(boq, mode).DefaultMetric;
            }
            Rebuild();
        }

        private void Rebuild()
        {
            Rows.Clear();
            switch (group)
            {
                case ChartGroup.Niveau: Chart = boq == null ? null : ViewChart.ByLevel(boq, levelOrder, inventory, groundLevel); break;
                case ChartGroup.Materiau: Chart = inventory.Count == 0 ? null : ViewChart.ByMaterial(inventory, levelFilter); break;
                case ChartGroup.Planning: Chart = null; break;
                default: Chart = boq == null ? null : ViewChart.Build(boq, mode); break;
            }
            if (group == ChartGroup.Planning && plan != null && plan.Tasks.Count > 0)
            {
                BuildPlanningRows();
                ShowBuilt();
            }
            else if (Chart != null)
            {
                decimal max = Chart.Max(metric);
                decimal total = Chart.Total(metric);
                int i = 1;
                foreach (ChartBar b in Chart.Bars)
                {
                    decimal v = b.Value(metric);
                    Rows.Add(new ChartBarRow
                    {
                        Bar = b,
                        Position = i++,
                        Label = b.Label,
                        Designation = b.Designation + (b.LinesWithoutPrice > 0 && group != ChartGroup.Prix && group != ChartGroup.Materiau ? "  (" + b.LinesWithoutPrice + " sans prix)" : string.Empty),
                        Color = "#FF" + b.Color.TrimStart('#'),
                        ValueText = Format(v),
                        ShareText = total > 0m ? (v / total * 100m).ToString("0.0", Fr) + " %" : "—",
                        BarWidth = max > 0m ? (double)(v / max) * ChartBarRow.BarMax : 0,
                        QuantitiesText = group == ChartGroup.Niveau
                            ? b.Volume.ToString("N2", Fr) + " m³ de matériaux  ·  " + b.Area.ToString("N1", Fr) + " m²"
                            : string.Join("  ·  ", b.Quantities.Take(3).Select(q => q.Value.ToString(q.Unit == BoqUnit.Kilogram ? "N0" : q.Unit == BoqUnit.CubicMeter ? "N3" : "N1", Fr) + " " + q.Unit.Symbol())),
                        ElementsText = b.ElementCount + " élément(s)" + (b.LinkedElements.Count > 0 ? " dont " + b.LinkedElements.Count + " de liens" : string.Empty) + " · " + b.LineCount + " ligne(s)",
                        TypesText = b.Types.Count == 0 ? string.Empty
                            : "Types : " + string.Join(", ", b.Types.Take(5)) + (b.Types.Count > 5 ? " … (+" + (b.Types.Count - 5) + ")" : string.Empty),
                        Tooltip = "Clic : isoler ces " + b.HostElementIds.Count + " élément(s) dans la vue active (temporaire)." +
                                  (b.Types.Count == 0 ? string.Empty : "\n" + string.Join("\n", b.Types))
                    });
                }
                TotalText = "Total : " + Format(total) + "  ·  " + Chart.Bars.Count +
                            (group == ChartGroup.Lot ? " lot(s)" : group == ChartGroup.Niveau ? " niveau(x)" : group == ChartGroup.Materiau ? " matériau(x)" + (levelFilter == null ? string.Empty : " — niveau " + levelFilter) : " groupe(s)");
                LegendText = group == ChartGroup.Materiau && levelFilter == null
                    ? "Barres 3D : une rangée par niveau, posée à l'altitude du niveau, devant la façade sud (Y min). Même colonne et même couleur = même matériau : " +
                      string.Join(", ", Rows.Select(r => r.Position + "=" + r.Label)) + "."
                    : group == ChartGroup.Niveau
                        ? "Barres 3D : une barre par niveau, posée à l'altitude du niveau, devant la façade sud (Y min)."
                        : "Barres 3D de gauche à droite, devant la façade sud (Y min) du modèle" + (levelFilter != null && group == ChartGroup.Materiau ? ", à l'altitude du niveau « " + levelFilter + " »" : string.Empty) + " : " +
                          string.Join(", ", Rows.Select(r => r.Position + "=" + r.Label)) + ".";
                Status = "Cliquez une barre pour l'isoler dans la vue active ; ⊙ pour sélectionner et zoomer.";
            }
            else
            {
                TotalText = LegendText = string.Empty;
            }
            switch (group)
            {
                case ChartGroup.Statut: Title = "DIAGRAMME DU MÉTRÉ — PAR STATUT"; break;
                case ChartGroup.Prix: Title = "DIAGRAMME DU MÉTRÉ — PAR PRIX"; break;
                case ChartGroup.Niveau: Title = "DIAGRAMME DU MÉTRÉ — PAR NIVEAU"; break;
                case ChartGroup.Materiau: Title = "MATÉRIAUX MODÉLISÉS" + (levelFilter == null ? " — TOUS NIVEAUX" : " — " + levelFilter.ToUpperInvariant()); break;
                case ChartGroup.Planning: Title = "SIMULATION 4D — PLANNING DES TRAVAUX"; break;
                default: Title = "DIAGRAMME DU MÉTRÉ — PAR LOT"; break;
            }
            Raise(nameof(Title));
            Raise(nameof(TotalText));
            Raise(nameof(LegendText));
            Raise(nameof(HasData));
            Raise(nameof(HasNoData));
            if (show3D)
            {
                host?.RequestChart3D(true);
            }
        }

        private string Format(decimal v)
        {
            switch (metric)
            {
                case ChartMetric.Elements: return v.ToString("N0", Fr) + " élém.";
                case ChartMetric.Volume: return v.ToString("N2", Fr) + " m³";
                case ChartMetric.Area: return v.ToString("N1", Fr) + " m²";
                default: return v.ToString("N0", Fr) + (currency.Length > 0 ? " " + currency : string.Empty);
            }
        }

        private void Isolate(ChartBarRow row)
        {
            if (row == null) return;
            host.RequestIsolate(row.Bar.HostElementIds, row.Label);
        }

        private void Select(ChartBarRow row)
        {
            if (row == null) return;
            var refs = row.Bar.HostElementIds.Select(id => new ElementRef(id)).Concat(row.Bar.LinkedElements).ToList();
            host.RequestChartSelection(refs);
        }

        private void RaiseModeFlags()
        {
            Raise(nameof(IsLotMode));
            Raise(nameof(IsStatusMode));
            Raise(nameof(IsPriceMode));
        }

        private static ColoringMode ParseMode(string p)
        {
            switch (p)
            {
                case "statut": return ColoringMode.Reliability;
                case "prix": return ColoringMode.Price;
                default: return ColoringMode.Lot;
            }
        }
    }
}
