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
        public ChartGroup Group => group;
        public bool CanColorize => group == ChartGroup.Lot || group == ChartGroup.Statut || group == ChartGroup.Prix;
        public bool HasAmountMetric => group != ChartGroup.Materiau;
        public bool HasInventoryMetrics => group == ChartGroup.Materiau || group == ChartGroup.Niveau;

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
                default: g = ChartGroup.Lot; break;
            }
            if (g == group) return;
            group = g;
            if (g == ChartGroup.Lot || g == ChartGroup.Statut || g == ChartGroup.Prix)
            {
                mode = g == ChartGroup.Statut ? ColoringMode.Reliability : g == ChartGroup.Prix ? ColoringMode.Price : ColoringMode.Lot;
                if (metric == ChartMetric.Volume || metric == ChartMetric.Area) metric = DefaultMetric();
            }
            else if (g == ChartGroup.Materiau && metric == ChartMetric.Amount)
            {
                metric = ChartMetric.Volume;
            }
            foreach (string n in new[] { nameof(IsLotMode), nameof(IsStatusMode), nameof(IsPriceMode), nameof(IsLevelMode), nameof(IsMaterialMode),
                                         nameof(CanColorize), nameof(HasAmountMetric), nameof(HasInventoryMetrics), nameof(Metric),
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
        public void SetBoq(BoqResult result, string currencyCode, IReadOnlyList<ElementQuantity> materials = null, IReadOnlyList<string> levels = null)
        {
            inventory = materials ?? new ElementQuantity[0];
            levelOrder = levels ?? new string[0];
            string keep = levelFilter;
            LevelChoices.Clear();
            LevelChoices.Add(AllLevels);
            foreach (string l in DanCI.BOQ.Core.Engine.LevelBreakdown.Build(result, levelOrder, null, inventory).Select(b => b.Level)) LevelChoices.Add(l);
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
                case ChartGroup.Niveau: Chart = boq == null ? null : ViewChart.ByLevel(boq, levelOrder, inventory); break;
                case ChartGroup.Materiau: Chart = inventory.Count == 0 ? null : ViewChart.ByMaterial(inventory, levelFilter); break;
                default: Chart = boq == null ? null : ViewChart.Build(boq, mode); break;
            }
            if (Chart != null)
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
                        Tooltip = "Clic : isoler ces " + b.HostElementIds.Count + " élément(s) dans la vue active (temporaire)."
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
