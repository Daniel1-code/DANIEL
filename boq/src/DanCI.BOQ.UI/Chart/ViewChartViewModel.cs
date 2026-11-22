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
        private ChartMetric metric = ChartMetric.Amount;
        private bool metricChosen;
        private bool show3D;
        private string status = "Lancez l'analyse (Métré & BOQ) : le diagramme se remplit avec les quantités du BOQ.";

        public ViewChartViewModel()
        {
            IsolateCommand = new RelayCommand(p => Isolate(p as ChartBarRow), p => host != null && p is ChartBarRow r && r.Bar.HostElementIds.Count > 0);
            SelectCommand = new RelayCommand(p => Select(p as ChartBarRow), p => host != null && p is ChartBarRow r && r.Bar.ElementCount > 0);
            ResetCommand = new RelayCommand(_ => host?.RequestResetIsolation(), _ => host != null);
            ColorizeCommand = new RelayCommand(_ => host?.RequestColorize(mode), _ => host != null && HasData);
            OpenBoqCommand = new RelayCommand(_ => host?.RequestOpenBoq(), _ => host != null);
            SetModeCommand = new RelayCommand(p => Mode = ParseMode(p as string));
            SetMetricCommand = new RelayCommand(p => { metricChosen = true; Metric = (p as string) == "elements" ? ChartMetric.Elements : ChartMetric.Amount; });
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

        public bool IsLotMode => mode == ColoringMode.Lot;
        public bool IsStatusMode => mode == ColoringMode.Reliability;
        public bool IsPriceMode => mode == ColoringMode.Price;

        public ChartMetric Metric
        {
            get => metric;
            set
            {
                if (Set(ref metric, value))
                {
                    Raise(nameof(IsAmountMetric));
                    Raise(nameof(IsElementsMetric));
                    Rebuild();
                }
            }
        }

        public bool IsAmountMetric => metric == ChartMetric.Amount;
        public bool IsElementsMetric => metric == ChartMetric.Elements;

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
        public void SetBoq(BoqResult result, string currencyCode)
        {
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
            Chart = boq == null ? null : ViewChart.Build(boq, mode);
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
                        Designation = b.Designation + (b.LinesWithoutPrice > 0 && mode != ColoringMode.Price ? "  (" + b.LinesWithoutPrice + " sans prix)" : string.Empty),
                        Color = "#FF" + b.Color.TrimStart('#'),
                        ValueText = Format(v),
                        ShareText = total > 0m ? (v / total * 100m).ToString("0.0", Fr) + " %" : "—",
                        BarWidth = max > 0m ? (double)(v / max) * ChartBarRow.BarMax : 0,
                        QuantitiesText = string.Join("  ·  ", b.Quantities.Take(3).Select(q => q.Value.ToString(q.Unit == BoqUnit.Kilogram ? "N0" : "N1", Fr) + " " + q.Unit.Symbol())),
                        ElementsText = b.ElementCount + " élément(s)" + (b.LinkedElements.Count > 0 ? " dont " + b.LinkedElements.Count + " de liens" : string.Empty) + " · " + b.LineCount + " ligne(s)",
                        Tooltip = "Clic : isoler ces " + b.HostElementIds.Count + " élément(s) dans la vue active (temporaire)."
                    });
                }
                TotalText = "Total : " + Format(total) + "  ·  " + Chart.Bars.Count + (mode == ColoringMode.Lot ? " lot(s)" : " groupe(s)");
                LegendText = "Barres 3D de gauche à droite, devant la façade sud (Y min) du modèle : " +
                             string.Join(", ", Rows.Select(r => r.Position + "=" + r.Label)) + ".";
                Status = "Cliquez une barre pour l'isoler dans la vue active ; ⊙ pour sélectionner et zoomer.";
            }
            else
            {
                TotalText = LegendText = string.Empty;
            }
            Title = mode == ColoringMode.Lot ? "DIAGRAMME DU MÉTRÉ — PAR LOT"
                  : mode == ColoringMode.Reliability ? "DIAGRAMME DU MÉTRÉ — PAR STATUT" : "DIAGRAMME DU MÉTRÉ — PAR PRIX";
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

        private string Format(decimal v) => metric == ChartMetric.Elements
            ? v.ToString("N0", Fr) + " élém."
            : v.ToString("N0", Fr) + (currency.Length > 0 ? " " + currency : string.Empty);

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
