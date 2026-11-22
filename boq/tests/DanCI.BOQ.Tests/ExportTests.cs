using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ExportTests
    {
        /// <summary>Devis realiste : prix avec decimales, coefficient, majorations en cascade, TVA, CDF, une ligne sans prix.</summary>
        internal static ExportContext Context(bool withMissingPrice = true)
        {
            BoqConfiguration config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 237.45m));
            config.Prices.Prices.Add(TestData.Price("03.02", BoqUnit.CubicMeter, 241.15m));
            config.Prices.Prices.Add(TestData.Price("05.01", BoqUnit.SquareMeter, 18.37m));
            if (!withMissingPrice)
            {
                config.Prices.Prices.Add(TestData.Price("03.08", BoqUnit.Kilogram, 2.13m));
            }
            config.Coefficients.Items.Add(new Coefficient { Code = "PERTE", Designation = "Pertes", Value = 0.035m, AppliesTo = TestData.List("03.0?"), NeedsValidation = true });
            config.Settings.Markups.Add(new Markup { Code = "IMP", Designation = "Imprévus", Percent = 5m, Base = MarkupBase.Travaux });
            config.Settings.Markups.Add(new Markup { Code = "FG", Designation = "Frais généraux", Percent = 12.5m, Base = MarkupBase.Cumul });
            config.Settings.SecondaryCurrency = Currency.CDF;
            config.Settings.ExchangeRate = new ExchangeRate { CdfPerUsd = 2843.55m, Date = new DateTime(2026, 11, 21), Source = "test" };
            config.Settings.QuantityDecimalsByUnit[BoqUnit.Kilogram] = 0;

            var random = new Random(7);
            var q = new List<ElementQuantity>();
            for (int i = 1; i <= 40; i++)
            {
                q.Add(TestData.Concrete(i, 0.1 + random.NextDouble(), i % 2 == 0 ? "OST_StructuralColumns" : "OST_StructuralFraming"));
                q.Add(TestData.Rebar(1000 + i, random.NextDouble() * 120, i % 3 == 0 ? Reliability.Estimated : Reliability.Measured));
            }
            q.Add(new ElementQuantity { ElementId = 5000, Kind = "Maconnerie", Category = "OST_Walls", TypeName = "Bloc 20", Value = 37.4561, Unit = BoqUnit.SquareMeter });
            q.Add(new ElementQuantity { ElementId = 6000, Kind = "Volume", Category = "OST_GenericModel", Value = 1.2, Unit = BoqUnit.CubicMeter });

            BoqResult boq = new BoqEngine().Build(q, config);
            return new ExportContext
            {
                Project = new ProjectInfo { Name = "Immeuble R+3 Gombe", Number = "DAN-2026-014", Client = "Maître d'ouvrage test", Location = "Kinshasa", Revision = "B" },
                Boq = boq,
                Audit = BoqAuditor.Run(q, null, boq, config.Extraction),
                Configuration = config,
                Quantities = q,
                DocumentTitle = "test.rvt",
                ScopeDescription = "modèle entier"
            };
        }

        private static decimal Num(IXLWorksheet ws, string address)
        {
            XLCellValue v = ws.Cell(address).Value;
            Assert.True(v.IsNumber, address + " n'est pas un nombre : " + v);
            return (decimal)v.GetNumber();
        }

        [Fact]
        public void Les_formules_Excel_retombent_au_centime_sur_le_moteur()
        {
            ExportContext ctx = Context();
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Build(wb, ctx);
                IXLWorksheet ws = wb.Worksheet("BOQ");

                foreach (BoqLine line in ctx.Boq.AllLines.Where(l => l.Amount.HasValue))
                {
                    Assert.Equal(line.Amount.Value, Num(ws, map.LineAmounts[line.Code]));
                }
                foreach (BoqLotResult lot in ctx.Boq.Lots)
                {
                    Assert.Equal(lot.Total, Num(ws, map.LotSubtotals[lot.Code]));
                }
                Assert.Equal(ctx.Boq.WorksTotal, Num(ws, map.WorksTotal));
                Assert.Equal(ctx.Boq.Markups.Select(m => m.Amount).ToArray(), map.Markups.Select(a => Num(ws, a)).ToArray());
                Assert.Equal(ctx.Boq.TotalExcludingVat, Num(ws, map.TotalExcludingVat));
                Assert.Equal(ctx.Boq.Vat, Num(ws, map.Vat));
                Assert.Equal(ctx.Boq.TotalIncludingVat, Num(ws, map.TotalIncludingVat));
                Assert.Equal(ctx.Boq.TotalIncludingVatSecondary.Value, Num(ws, map.TotalSecondary));
            }
        }

        [Fact]
        public void Les_montants_sont_des_formules_vivantes()
        {
            ExportContext ctx = Context();
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Build(wb, ctx);
                IXLWorksheet ws = wb.Worksheet("BOQ");
                string first = map.LineAmounts.Values.First();
                Assert.StartsWith("IF(E", ws.Cell(first).FormulaA1);
                Assert.Contains("ROUND(D", ws.Cell(first).FormulaA1);
                Assert.StartsWith("SUM(F", ws.Cell(map.LotSubtotals.Values.First()).FormulaA1);
                Assert.Contains("ROUND(", ws.Cell(map.Vat).FormulaA1);

                // Modifier un PU dans Excel recalcule la ligne et le total.
                int row = ws.Cell(first).Address.RowNumber;
                decimal before = Num(ws, map.TotalIncludingVat);
                ws.Cell(row, 5).Value = (double)((decimal)ws.Cell(row, 5).Value.GetNumber() + 1m);
                Assert.NotEqual(before, Num(ws, map.TotalIncludingVat));
            }
        }

        [Fact]
        public void Ligne_sans_prix_montant_vide_et_avertissement()
        {
            ExportContext ctx = Context();
            BoqLine unpriced = ctx.Boq.AllLines.First(l => !l.Amount.HasValue && !l.IsUnclassified);
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Build(wb, ctx);
                IXLWorksheet ws = wb.Worksheet("BOQ");
                Assert.Equal(string.Empty, ws.Cell(map.LineAmounts[unpriced.Code]).Value.ToString());
                Assert.Contains("MINORANTS", ws.Cell("A4").GetString());
            }
        }

        [Fact]
        public void Les_sept_feuilles_et_la_mise_en_page()
        {
            ExportContext ctx = Context(withMissingPrice: false);
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.Build(wb, ctx);
                Assert.Equal(new[] { "Page de garde", "Synthèse visuelle", "Tableau de bord", "BOQ", "Récapitulatif", "Matériaux", "Détail du métré", "Audit", "Hypothèses", "Exclus" },
                    wb.Worksheets.Select(w => w.Name).ToArray());
                IXLWorksheet boq = wb.Worksheet("BOQ");
                Assert.Equal(XLPaperSize.A4Paper, boq.PageSetup.PaperSize);
                Assert.Equal(1, boq.PageSetup.PagesWide);
                Assert.True(boq.PageSetup.FirstRowToRepeatAtTop == 5);
                string firstAmount = BoqWorkbookWriter.Build(new XLWorkbook(), ctx).LineAmounts.Values.First();
                Assert.Equal(BoqWorkbookWriter.MoneyFormat, boq.Cell(firstAmount).Style.NumberFormat.Format);

                int contributions = ctx.Boq.AllLines.Sum(l => l.Contributions.Count);
                Assert.Equal(contributions + 1, wb.Worksheet("Détail du métré").LastRowUsed().RowNumber());
                Assert.Contains(wb.Worksheet("Hypothèses").CellsUsed(), c => c.GetString() == "À VALIDER");
                Assert.Contains(wb.Worksheet("Hypothèses").CellsUsed(), c => c.GetString().Contains("BRUTE"));
                Assert.Contains(wb.Worksheet("Audit").CellsUsed(), c => c.GetString() == "ARMATURES_PAR_RATIO" || c.GetString() == "NON_CLASSE");
                Assert.Contains(wb.Worksheet("Page de garde").CellsUsed(), c => string.Equals(c.GetString(), "Immeuble R+3 Gombe", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void Le_recapitulatif_reprend_les_sous_totaux()
        {
            ExportContext ctx = Context(withMissingPrice: false);
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.Build(wb, ctx);
                IXLWorksheet ws = wb.Worksheet("Récapitulatif");
                int row = 4;
                foreach (BoqLotResult lot in ctx.Boq.Lots)
                {
                    Assert.Equal(lot.Total, (decimal)ws.Cell(row, 3).Value.GetNumber());
                    row++;
                }
                Assert.Equal(ctx.Boq.WorksTotal, (decimal)ws.Cell(row, 3).Value.GetNumber());
            }
        }

        [Fact]
        public void Le_PDF_est_genere()
        {
            byte[] pdf = BoqPdfWriter.Generate(Context());
            Assert.True(pdf.Length > 5000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
        }

        [Fact]
        public void Export_complet_quatre_fichiers()
        {
            string dir = Path.Combine(Path.GetTempPath(), "danci-boq-export-" + Guid.NewGuid().ToString("N"));
            try
            {
                ExportResult r = BoqExporter.ExportAll(Context(), dir, "test");
                Assert.Empty(r.Errors);
                Assert.Equal(4, r.Files.Count);
                Assert.All(r.Files, f => Assert.True(new FileInfo(f).Length > 0, f));
                using (var wb = new XLWorkbook(r.Files.Single(f => f.EndsWith(".xlsx"))))
                {
                    Assert.Equal(10, wb.Worksheets.Count); // le fichier ecrit se relit
                }
                Assert.Contains(",", File.ReadAllLines(r.Files.Single(f => f.EndsWith("_BOQ_lignes_brut.csv")))[0]);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
