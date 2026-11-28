using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class LevelBreakdownTests
    {
        private static readonly string[] Levels = { "Fondation", "RDC", "Étage 1", "Toiture" };

        /// <summary>Devis d'essai avec les elements repartis sur 4 niveaux (+ quelques-uns sans niveau).</summary>
        internal static ExportContext Context()
        {
            ExportContext ctx = ExportTests.Context();
            int i = 0;
            foreach (ElementQuantity q in ctx.Quantities)
            {
                q.Level = q.ElementId == 6000 ? null : Levels[i++ % Levels.Length];
            }
            return ctx;
        }

        [Fact]
        public void Somme_des_niveaux_egale_exactement_chaque_ligne_du_BOQ()
        {
            ExportContext ctx = Context();
            List<LevelBlock> blocks = LevelBreakdown.Build(ctx.Boq, Levels);
            foreach (BoqLine line in ctx.Boq.AllLines)
            {
                var parts = blocks.SelectMany(b => b.Lots).SelectMany(l => l.Articles).Where(a => a.Line == line).ToList();
                Assert.Equal(line.Quantity, parts.Sum(p => p.Quantity));
                if (line.Amount.HasValue) Assert.Equal(line.Amount.Value, parts.Sum(p => p.Amount.Value));
                else Assert.All(parts, p => Assert.Null(p.Amount));
                Assert.Equal(line.Contributions.Count, parts.Sum(p => p.Elements.Count));
            }
            Assert.Equal(ctx.Boq.Lots.Sum(l => l.Total), blocks.Sum(b => b.Total));
        }

        [Fact]
        public void Niveaux_dans_l_ordre_des_altitudes_puis_sans_niveau()
        {
            List<LevelBlock> blocks = LevelBreakdown.Build(Context().Boq, Levels);
            Assert.Equal(new[] { "Fondation", "RDC", "Étage 1", "Toiture", LevelBreakdown.NoLevel }, blocks.Select(b => b.Level));
            Assert.True(blocks.Last().IsWithoutLevel);
        }

        [Fact]
        public void Chaque_niveau_ne_contient_que_ses_elements()
        {
            List<LevelBlock> blocks = LevelBreakdown.Build(Context().Boq, Levels);
            foreach (LevelBlock b in blocks.Where(x => !x.IsWithoutLevel))
            {
                Assert.All(b.Lots.SelectMany(l => l.Articles).SelectMany(a => a.Elements), e => Assert.Equal(b.Level, e.Level));
                foreach (LevelArticle a in b.Lots.SelectMany(l => l.Articles))
                {
                    Assert.Equal(a.Elements.Sum(e => e.Value), (double)a.NetQuantity, 6);
                }
            }
        }

        [Fact]
        public void Repartition_des_arrondis_sans_perte()
        {
            List<decimal> r = LevelBreakdown.Allocate(100m, new[] { 1m / 3, 1m / 3, 1m / 3 }, 2);
            Assert.Equal(100m, r.Sum());
            Assert.Equal(new[] { 33.34m, 33.33m, 33.33m }, r);
            Assert.Equal(new[] { 10m }, LevelBreakdown.Allocate(10m, new[] { 1m }, 2));
        }

        [Fact]
        public void Couches_rangees_par_niveau()
        {
            var layers = LayerTests.SampleLayers();
            layers[5].Level = "Fondation";
            List<LevelBlock> blocks = LevelBreakdown.Build(Context().Boq, Levels, layers);
            Assert.Contains(blocks.Single(b => b.Level == "Fondation").Layers, r => r.Material == "Béton C25/30");
            Assert.DoesNotContain(blocks.Single(b => b.Level == "Fondation").Layers, r => r.Material == "Laine de roche");
        }

        [Fact]
        public void Feuille_Excel_BOQ_par_niveau()
        {
            ExportContext ctx = Context();
            ctx.LevelOrder = Levels;
            var layers = LayerTests.SampleLayers();
            layers.ForEach(l => l.Level = "RDC");
            layers[5].Level = "Fondation";
            ctx.Layers = layers;
            string path = System.Environment.GetEnvironmentVariable("DANCI_KEEP_LEVELS")
                          ?? System.IO.Path.Combine(System.IO.Path.GetTempPath(), "danci_niveaux_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            DanCI.BOQ.Export.Excel.BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new ClosedXML.Excel.XLWorkbook(path))
            {
                var ws = wb.Worksheet(DanCI.BOQ.Export.Excel.BoqWorkbookWriter.LevelsSheetName);
                string all = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
                Assert.Contains("NIVEAU : Fondation", all);
                Assert.Contains("NIVEAU : Toiture", all);
                Assert.Contains("RÉCAPITULATIF", all);
                // Total travaux du recapitulatif = total du BOQ.
                var total = ws.CellsUsed().First(c => c.GetString() == "TOTAL TRAVAUX HT");
                decimal grand = (decimal)total.Worksheet.Row(total.Address.RowNumber).CellsUsed().Last().GetDouble();
                Assert.Equal((double)ctx.Boq.Lots.Sum(l => l.Total), (double)grand, 2);
            }
            if (System.Environment.GetEnvironmentVariable("DANCI_KEEP_LEVELS") == null) System.IO.File.Delete(path);
        }
    }
}
