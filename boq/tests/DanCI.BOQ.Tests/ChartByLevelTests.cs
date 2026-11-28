using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ChartByLevelTests
    {
        private static readonly string[] Levels = { "Fondation", "RDC", "Étage 1", "Toiture" };

        [Fact]
        public void Une_barre_par_niveau_montants_egaux_au_BOQ()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            ViewChart chart = ViewChart.ByLevel(ctx.Boq, Levels, InventoryTests.Sample());
            Assert.Equal(ChartGroup.Niveau, chart.Group);
            Assert.Equal("Fondation", chart.Bars[0].Key);
            Assert.Equal(ctx.Boq.Lots.Sum(l => l.Total), chart.TotalAmount);
            ChartBar rdc = chart.Bars.Single(b => b.Key == "RDC");
            Assert.Equal((decimal)InventoryTests.Sample().Where(q => q.Level == "RDC").Sum(q => q.Value), rdc.Volume, 6);
        }

        [Fact]
        public void Une_barre_par_materiau_tous_niveaux_ou_un_niveau_couleur_stable()
        {
            var inv = InventoryTests.Sample();
            ViewChart all = ViewChart.ByMaterial(inv);
            ViewChart rdc = ViewChart.ByMaterial(inv, "RDC");
            Assert.Equal(ChartGroup.Materiau, all.Group);
            Assert.Equal("RDC", rdc.Level);
            Assert.DoesNotContain(rdc.Bars, b => b.Key == "(matériau « par catégorie »)");   // Etage 1 seulement
            // Meme materiau, meme couleur, quel que soit le niveau affiche.
            Assert.Equal(all.Bars.Single(b => b.Key == "Béton C25/30").Color, rdc.Bars.Single(b => b.Key == "Béton C25/30").Color);
            Assert.Equal(4, all.Bars.Single(b => b.Key == "Béton C25/30").ElementCount);
            Assert.Equal(ChartMetric.Volume, ChartMetric.Volume);
            Assert.Equal(all.Bars.Single(b => b.Key == "Béton C25/30").Volume, all.Bars.Single(b => b.Key == "Béton C25/30").Value(ChartMetric.Volume));
        }

        [Fact]
        public void Barres_3D_par_niveau_posees_a_l_altitude_de_chaque_niveau()
        {
            var rows = new List<ChartLevelRow>
            {
                new ChartLevelRow { Level = "Fondation", ElevationFt = -5 },
                new ChartLevelRow { Level = "RDC", ElevationFt = 0 },
                new ChartLevelRow { Level = "Étage 1", ElevationFt = 10 },
            };
            rows[0].Bars.Add(("Béton", 8m, "#00E5FF"));
            rows[1].Bars.Add(("Béton", 4m, "#00E5FF"));
            rows[1].Bars.Add(("Bloc", 2m, "#FF8A00"));
            rows[2].Bars.Add(("Bloc", 0m, "#FF8A00"));
            List<ChartBox> boxes = ChartLayout.Grouped(rows, 0, 0, -5, 100, 50, 20);
            var bars = boxes.Where(b => b.Color != ChartLayout.BaseColor).ToList();
            Assert.Equal(3, bars.Count);                                      // valeur nulle : pas de barre
            Assert.Equal(3, boxes.Count(b => b.Color == ChartLayout.BaseColor)); // un socle par niveau
            Assert.Equal(-5, bars[0].MinZ);
            Assert.Equal(0, bars[1].MinZ);
            // Plus grande barre = 80 % du plus petit ecart entre niveaux (5 pieds) ; meme echelle partout.
            Assert.Equal(4.0, bars[0].Height, 9);
            Assert.Equal(2.0, bars[1].Height, 9);
            Assert.All(bars, b => Assert.True(b.MaxY < 0));
        }
    }
}
