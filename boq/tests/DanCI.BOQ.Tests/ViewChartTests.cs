using System.Linq;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ViewChartTests
    {
        [Fact]
        public void Par_lot_les_montants_et_lignes_egalent_le_BOQ()
        {
            ExportContext ctx = ExportTests.Context();
            ViewChart chart = ViewChart.Build(ctx.Boq, ColoringMode.Lot);

            Assert.Equal(ctx.Boq.Lots.Select(l => l.Code).Distinct().OrderBy(c => c, System.StringComparer.Ordinal),
                         chart.Bars.Select(b => b.Key));
            Assert.Equal(ctx.Boq.AllLines.Sum(l => l.Amount ?? 0m), chart.TotalAmount);
            Assert.Equal(ctx.Boq.AllLines.Count(), chart.Bars.Sum(b => b.LineCount));
            foreach (ChartBar bar in chart.Bars)
            {
                Assert.Equal(Palette.ForLot(bar.Key), bar.Color);
                var lines = ctx.Boq.AllLines.Where(l => l.LotCode == bar.Key).ToList();
                Assert.Equal(lines.Sum(l => l.Amount ?? 0m), bar.Amount);
                Assert.Equal(lines.Count(l => !l.Amount.HasValue), bar.LinesWithoutPrice);
                // Elements distincts, sans doublon.
                Assert.Equal(bar.HostElementIds.Count, bar.HostElementIds.Distinct().Count());
                // Quantites par unite = somme des lignes du lot pour cette unite.
                foreach (var q in bar.Quantities)
                {
                    Assert.Equal(lines.Where(l => l.Unit == q.Unit).Sum(l => l.Quantity), q.Value);
                }
            }
        }

        [Fact]
        public void Par_statut_et_par_prix_le_total_ne_change_pas()
        {
            ExportContext ctx = ExportTests.Context();
            decimal total = ViewChart.Build(ctx.Boq, ColoringMode.Lot).TotalAmount;
            ViewChart status = ViewChart.Build(ctx.Boq, ColoringMode.Reliability);
            ViewChart price = ViewChart.Build(ctx.Boq, ColoringMode.Price);
            Assert.Equal(total, status.TotalAmount);
            Assert.Equal(total, price.TotalAmount);
            Assert.All(status.Bars, b => Assert.Equal(Palette.ForReliability((Reliability)int.Parse(b.Key)), b.Color));
            ChartBar missing = price.Bars.Single(b => b.Key == "1");
            Assert.Equal(0m, missing.Amount);
            Assert.Equal(ctx.Boq.AllLines.Count(l => !l.Amount.HasValue), missing.LineCount);
        }

        [Fact]
        public void Sans_prix_la_grandeur_par_defaut_est_le_nombre_d_elements()
        {
            ExportContext ctx = ExportTests.Context();
            foreach (BoqLine l in ctx.Boq.AllLines) l.Amount = null;
            ViewChart chart = ViewChart.Build(ctx.Boq, ColoringMode.Lot);
            Assert.False(chart.HasAmounts);
            Assert.Equal(ChartMetric.Elements, chart.DefaultMetric);
            Assert.Equal(chart.Bars.Sum(b => b.ElementCount), chart.Total(ChartMetric.Elements));
        }

        [Fact]
        public void Barres_3D_devant_le_modele_hauteurs_proportionnelles()
        {
            ExportContext ctx = ExportTests.Context();
            ViewChart chart = ViewChart.Build(ctx.Boq, ColoringMode.Lot);
            var boxes = ChartLayout.Bars(chart, ChartMetric.Elements, 0, 0, 0, 100, 60, 30);

            Assert.Equal(ChartLayout.BaseColor, boxes[0].Color);                // socle
            var bars = boxes.Skip(1).ToList();
            Assert.Equal(chart.Bars.Count(b => b.ElementCount > 0), bars.Count);
            Assert.All(bars, b => Assert.True(b.MaxY < 0, "devant la facade sud (Y min)"));
            Assert.All(bars, b => Assert.True(b.MinX >= 0 && b.MaxX <= 100, "dans la largeur du modele"));
            Assert.All(bars, b => Assert.Equal(0, b.MinZ));
            // Pas de chevauchement, ordre gauche -> droite = ordre du panneau.
            Assert.True(bars.Zip(bars.Skip(1), (a, b) => a.MaxX < b.MinX).All(x => x));
            // La plus haute barre = la plus grande valeur ; hauteurs proportionnelles.
            ChartBar top = chart.Bars.OrderByDescending(b => b.ElementCount).First();
            double hMax = bars.Max(b => b.Height);
            foreach (ChartBox box in bars)
            {
                ChartBar bar = chart.Bars.First(b => b.Color == box.Color);
                double expected = System.Math.Max((double)bar.ElementCount / top.ElementCount, ChartLayout.MinBarFraction) * hMax;
                Assert.Equal(expected, box.Height, 6);
            }
        }

        [Fact]
        public void Aucune_barre_sans_donnees()
        {
            Assert.Empty(ChartLayout.Bars(null, ChartMetric.Amount, 0, 0, 0, 1, 1, 1));
            var empty = ViewChart.Build(new BoqResult(), ColoringMode.Lot);
            Assert.Empty(empty.Bars);
            Assert.Empty(ChartLayout.Bars(empty, ChartMetric.Amount, 0, 0, 0, 1, 1, 1));
        }
    }
}
