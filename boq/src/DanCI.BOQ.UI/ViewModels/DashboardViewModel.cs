using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Couleurs du tableau de bord (theme nuit), en texte : WPF les convertit en pinceaux.</summary>
    public static class Neon
    {
        public const string Cyan = "#FF00E5FF";
        public const string Orange = "#FFFF8A00";
        public const string Green = "#FF21E6A1";
        public const string Yellow = "#FFFFC233";
        public const string Red = "#FFFF4D6D";
        public const string Muted = "#FF8AA4C8";

        public static string For(SignalLevel level)
        {
            switch (level)
            {
                case SignalLevel.Critique: return Red;
                case SignalLevel.Attention: return Yellow;
                case SignalLevel.Info: return Cyan;
                default: return Green;
            }
        }
    }

    public sealed class KpiTile
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string Sub { get; set; }
        public string Color { get; set; } = "#FFE6F1FF";
        /// <summary>Jauge 0..1 (null = pas de jauge).</summary>
        public double? Gauge { get; set; }
        public double GaugeWidth => (Gauge ?? 0) * 180.0;
        public bool HasGauge => Gauge.HasValue;
    }

    public sealed class LotBar
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public string Amount { get; set; }
        public string Share { get; set; }
        public double BarWidth { get; set; }
        public string Color { get; set; }
    }

    public sealed class SignalRow
    {
        public string Color { get; set; }
        public string Level { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Action { get; set; }
    }

    public sealed class ParetoRow
    {
        public string Rank { get; set; }
        public string Code { get; set; }
        public string Designation { get; set; }
        public string Amount { get; set; }
        public string Cumulative { get; set; }
        public double CumulativeWidth { get; set; }
    }

    public sealed class KeyQuantityRow
    {
        public string Label { get; set; }
        public string Value { get; set; }
        public string Measured { get; set; }
        public string MeasuredColor { get; set; }
    }

    /// <summary>Tableau de bord affiche dans la fenetre (calcule par Core : aucune valeur ajoutee ici).</summary>
    public sealed class DashboardViewModel
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        public const double LotBarMax = 260.0;
        public const double ParetoBarMax = 120.0;

        public static DashboardViewModel Empty { get; } = new DashboardViewModel();

        private DashboardViewModel()
        {
        }

        public bool HasData { get; private set; }
        public string Title { get; private set; } = "Lancez l'analyse : le tableau de bord se remplit à partir du BOQ, de l'audit et du métré.";
        public int Score { get; private set; }
        public string ScoreText => HasData ? Score + " / 100" : "—";
        public string ScoreColor { get; private set; } = Neon.Muted;
        public double ScoreAngleWidth => HasData ? Score * 1.8 : 0;
        public List<KpiTile> Tiles { get; } = new List<KpiTile>();
        public List<LotBar> Lots { get; } = new List<LotBar>();
        public List<SignalRow> Signals { get; } = new List<SignalRow>();
        public List<ParetoRow> Pareto { get; } = new List<ParetoRow>();
        public List<KeyQuantityRow> KeyQuantities { get; } = new List<KeyQuantityRow>();
        public string MissingPricesText { get; private set; } = string.Empty;

        /// <summary>Message quand le Pareto est vide (aucun montant) : un panneau vide n'est jamais muet.</summary>
        public string ParetoEmpty => HasData && Pareto.Count == 0
            ? "Aucun montant pour l'instant : saisissez les prix unitaires (Paramètres du projet > Prix unitaires), puis enregistrez."
            : string.Empty;

        public static DashboardViewModel From(BoqDashboard d, string projectTitle)
        {
            if (d == null)
            {
                return Empty;
            }
            string cur = d.Currency;
            var vm = new DashboardViewModel
            {
                HasData = true,
                Title = projectTitle,
                Score = d.HealthScore,
                ScoreColor = d.HealthScore >= 85 ? Neon.Green : d.HealthScore >= 60 ? Neon.Yellow : Neon.Red
            };
            vm.Tiles.Add(new KpiTile { Label = "TOTAL TRAVAUX HT", Value = Money(d.WorksTotal) + " " + cur, Sub = "majorations " + Money(d.MarkupsTotal) });
            vm.Tiles.Add(new KpiTile
            {
                Label = "TOTAL TTC",
                Value = Money(d.TotalIncludingVat) + " " + cur,
                Sub = d.TotalIncludingVatSecondary.HasValue ? "soit " + Money(d.TotalIncludingVatSecondary.Value) + " " + d.SecondaryCurrency
                    : "TVA " + d.VatPercent.ToString("0.##", Fr) + " % : " + Money(d.Vat),
                Color = d.IsComplete ? Neon.Cyan : Neon.Yellow
            });
            vm.Tiles.Add(new KpiTile
            {
                Label = "COÛT TTC AU M²",
                Value = d.CostPerSquareMeter.HasValue ? Money(d.CostPerSquareMeter.Value) + " " + cur : "—",
                Sub = d.ReferenceArea.HasValue ? Money(d.ReferenceArea.Value) + " m² " + d.ReferenceAreaLabel : "surface de référence à saisir"
            });
            vm.Tiles.Add(new KpiTile
            {
                Label = "PRIX RENSEIGNÉS",
                Value = Percent(d.PriceCoverage),
                Sub = d.PricedLineCount + " / " + d.LineCount + " lignes",
                Color = d.PriceCoverage >= 1m ? Neon.Green : Neon.Red,
                Gauge = (double)d.PriceCoverage
            });
            vm.Tiles.Add(new KpiTile
            {
                Label = "FIABILITÉ",
                Value = d.ReliabilityIndex.HasValue ? Percent(d.ReliabilityIndex.Value) : "—",
                Sub = "part du montant issue de quantités mesurées",
                Color = d.ReliabilityIndex >= 0.8m ? Neon.Green : Neon.Yellow,
                Gauge = d.ReliabilityIndex.HasValue ? (double)d.ReliabilityIndex.Value : (double?)null
            });
            vm.Tiles.Add(new KpiTile
            {
                Label = "AUDIT",
                Value = d.AuditErrors + " err. · " + d.AuditWarnings + " av.",
                Sub = d.ElementCount + " éléments · " + d.ExcludedCount + " exclus",
                Color = d.AuditErrors > 0 ? Neon.Red : d.AuditWarnings > 0 ? Neon.Yellow : Neon.Green
            });
            vm.Tiles.Add(new KpiTile
            {
                Label = "RATIO ACIER",
                Value = d.RebarRatio.HasValue ? d.RebarRatio.Value.ToString("N0", Fr) + " kg/m³" : "—",
                Sub = "armatures / béton (nets)"
            });

            decimal max = d.Lots.Count == 0 ? 0m : d.Lots.Max(l => l.Share);
            foreach (LotShare l in d.Lots)
            {
                vm.Lots.Add(new LotBar
                {
                    Code = l.Code,
                    Designation = l.Designation + (l.IsComplete ? string.Empty : "  (" + l.LinesWithoutPrice + " sans prix)"),
                    Amount = Money(l.Amount),
                    Share = Percent(l.Share),
                    BarWidth = max > 0m ? (double)(l.Share / max) * LotBarMax : 0,
                    Color = l.IsComplete ? Neon.Cyan : Neon.Yellow
                });
            }
            foreach (DashboardSignal s in d.Signals)
            {
                vm.Signals.Add(new SignalRow
                {
                    Color = Neon.For(s.Level),
                    Level = s.Level.ToString().ToUpperInvariant(),
                    Title = s.Title,
                    Detail = s.Detail,
                    Action = s.Action
                });
            }
            foreach (ParetoLine p in d.Pareto)
            {
                vm.Pareto.Add(new ParetoRow
                {
                    Rank = p.Rank.ToString(Fr),
                    Code = p.Code,
                    Designation = p.Designation + (p.Reliability == Reliability.Estimated ? " (E)" : string.Empty),
                    Amount = Money(p.Amount),
                    Cumulative = Percent(p.CumulativeShare),
                    CumulativeWidth = (double)p.CumulativeShare * ParetoBarMax
                });
            }
            foreach (KeyQuantity k in d.KeyQuantities)
            {
                vm.KeyQuantities.Add(new KeyQuantityRow
                {
                    Label = k.Label,
                    Value = k.Value.ToString(k.Unit == BoqUnit.Kilogram ? "N0" : "N2", Fr) + " " + k.Unit.Symbol(),
                    Measured = Percent(k.MeasuredShare) + " mesuré",
                    MeasuredColor = k.MeasuredShare >= 1m ? Neon.Green : Neon.Yellow
                });
            }
            vm.MissingPricesText = d.MissingPrices.Count == 0 ? string.Empty :
                "Prix à saisir : " + string.Join(", ", d.MissingPrices.Take(15).Select(m => m.Code)) + (d.MissingPrices.Count > 15 ? "…" : string.Empty);
            return vm;
        }

        private static string Money(decimal v) => v.ToString("N2", Fr);
        private static string Percent(decimal share) => (share * 100m).ToString("0.0", Fr) + " %";
    }
}
