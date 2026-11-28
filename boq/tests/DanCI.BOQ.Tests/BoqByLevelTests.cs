using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class BoqByLevelTests
    {
        [Fact]
        public void Libelle_du_niveau_avec_sa_cote_en_metres()
        {
            var z = new Dictionary<string, double> { ["RDC"] = 0, ["Étage 1"] = 3.2, ["Fondation"] = -1.5 };
            Assert.Equal("RDC (± 0,00 m)", LevelBreakdown.Label("RDC", z));
            Assert.Equal("Étage 1 (+3,20 m)", LevelBreakdown.Label("Étage 1", z));
            Assert.Equal("Fondation (−1,50 m)", LevelBreakdown.Label("Fondation", z));
            Assert.Equal("Toiture", LevelBreakdown.Label("Toiture", z));
            Assert.Equal(LevelBreakdown.NoLevel, LevelBreakdown.Label(null, z));
        }

        [Fact]
        public void Classeur_BOQ_par_niveau_totaux_egaux_au_moteur()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            ctx.LevelOrder = new[] { "Fondation", "RDC", "Étage 1", "Toiture" };
            ctx.LevelElevations = new Dictionary<string, double> { ["RDC"] = 0, ["Étage 1"] = 3.2 };
            Assert.True(ctx.Configuration.Settings.IsByLevel);
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "danci_lvl_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("BOQ");
                string all = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
                Assert.Contains("ÉTAGE 1 (+3,20 M)", all);
                Assert.Contains("RÉCAPITULATIF PAR NIVEAU", all);
                Assert.Contains("RÉCAPITULATIF PAR LOT ET PAR ARTICLE", all);
                // Total des travaux (formule) = moteur ; somme des totaux de niveaux = total des travaux.
                var works = ws.CellsUsed().First(c => c.GetString() == "TOTAL DES TRAVAUX HT");
                double total = ws.Cell(works.Address.RowNumber, 6).GetDouble();
                Assert.Equal((double)ctx.Boq.WorksTotal, total, 2);
                double levels = ws.CellsUsed().Where(c => c.GetString().StartsWith("TOTAL NIVEAU "))
                    .Sum(c => ws.Cell(c.Address.RowNumber, 6).GetDouble());
                Assert.Equal(total, levels, 2);
            }
            ctx.Configuration.Settings.Presentation = DanCI.BOQ.Core.Pricing.BoqPresentation.ByLot;
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                Assert.DoesNotContain("RÉCAPITULATIF PAR NIVEAU", string.Join("|", wb.Worksheet("BOQ").CellsUsed().Select(c => c.GetString())));
            }
            System.IO.File.Delete(path);
        }

        [Fact]
        public void Part_et_plus_grosse_part_par_article()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            var blocks = LevelBreakdown.Build(ctx.Boq, new[] { "Fondation", "RDC", "Étage 1", "Toiture" });
            foreach (var g in blocks.SelectMany(b => b.Lots).SelectMany(l => l.Articles).GroupBy(a => a.Line))
            {
                Assert.Equal(1m, decimal.Round(g.Sum(a => a.Share), 6));
                Assert.Single(g.Where(a => a.IsLargestShare));
            }
        }
    }
}
