using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ProjectSettingsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-param-" + Guid.NewGuid().ToString("N"));

        public ProjectSettingsTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        /// <summary>PNG 1 x 1 valide (image d'essai pour la page de garde).</summary>
        internal static string Png(string folder, string name)
        {
            string path = Path.Combine(folder, name);
            File.WriteAllBytes(path, Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg=="));
            return path;
        }

        [Fact]
        public void Identite_la_saisie_prime_le_vide_reprend_Revit()
        {
            var revit = new ProjectInfo { Name = "Nom Revit", Client = "Client Revit", Location = "Gombe", Author = "Auteur Revit" };
            var details = new ProjectDetails { Name = "  Immeuble Horizon  ", Client = " ", Purpose = "Bureaux R+3", ReferenceArea = 1250m };
            ProjectInfo merged = details.Merge(revit);
            Assert.Equal("Immeuble Horizon", merged.Name);
            Assert.Equal("Client Revit", merged.Client);
            Assert.Equal("Gombe", merged.Location);
            Assert.Equal("Bureaux R+3", merged.Purpose);
            Assert.Equal(1250m, merged.ReferenceArea);
        }

        [Fact]
        public void Devis_du_projet_remplace_TVA_et_majorations_garde_les_decimales()
        {
            BoqConfiguration config = TestData.Config();
            config.Settings.AmountDecimals = 3;
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 200m));
            var load = new LoadResult { Configuration = config };
            ProjectEstimate estimate = ProjectEstimate.From(config.Settings);
            estimate.VatPercent = 0m;
            estimate.Markups.Add(new Markup { Code = "BEN", Designation = "Bénéfice", Percent = 10m, Base = MarkupBase.Travaux });

            ProjectOverrides.ApplyEstimate(load, estimate);
            Assert.False(load.HasErrors);
            Assert.Equal(3, load.Configuration.Settings.AmountDecimals);
            Assert.Equal(PricedQuantity.Brute, load.Configuration.Settings.PricedQuantity);

            BoqResult boq = new BoqEngine().Build(new[] { TestData.Concrete(1, 2.0) }, load.Configuration);
            Assert.Equal(0m, boq.VatPercent);
            Assert.Equal(400m, boq.WorksTotal);
            Assert.Equal(440m, boq.TotalIncludingVat);
        }

        [Fact]
        public void Devis_du_projet_invalide_est_une_erreur_signalee()
        {
            var load = new LoadResult { Configuration = TestData.Config() };
            ProjectEstimate estimate = ProjectEstimate.From(load.Configuration.Settings);
            estimate.VatPercent = 160m;
            estimate.SecondaryCurrency = Currency.CDF;
            ProjectOverrides.ApplyEstimate(load, estimate);
            Assert.Contains(load.Issues, i => i.Code == "TVA_INVALIDE" && i.File == ProjectOverrides.Source);
            Assert.Contains(load.Issues, i => i.Code == "TAUX_ABSENT");
            Assert.True(load.HasErrors);
        }

        [Fact]
        public void Fichier_projet_aller_retour_et_ancien_fichier_compatible()
        {
            string path = Path.Combine(dir, "p.danciboq.json");
            var file = new ProjectFile
            {
                Details = new ProjectDetails { Name = "Horizon", Revision = "C", ReferenceArea = 980.5m, ValidityDays = "90" },
                Estimate = new ProjectEstimate
                {
                    VatPercent = 16m,
                    SecondaryCurrency = Currency.CDF,
                    ExchangeRate = new ExchangeRate { CdfPerUsd = 2850m, Date = new DateTime(2026, 11, 20), Source = "BCC" },
                    Markups = { new Markup { Code = "FG", Designation = "Frais généraux", Percent = 12m, Base = MarkupBase.Cumul } }
                },
                Cover = new CoverSettings { Theme = CoverTheme.Classique, Subtitle = "APD", ProjectImage = "rendu.png" }
            };
            ProjectFileStore.Save(path, file);
            var issues = new List<BoqIssue>();
            ProjectFile back = ProjectFileStore.Load(path, issues);
            Assert.Empty(issues);
            Assert.Equal("C", back.Details.Revision);
            Assert.Equal(980.5m, back.Details.ReferenceArea);
            Assert.Equal(2850m, back.Estimate.ExchangeRate.CdfPerUsd);
            Assert.Equal(MarkupBase.Cumul, back.Estimate.Markups[0].Base);
            Assert.Equal(CoverTheme.Classique, back.Cover.Theme);
            Assert.Equal(Path.Combine(dir, "rendu.png"), back.Cover.Resolve(path).ProjectImage);

            File.WriteAllText(path, "{ \"schemaVersion\": 1, \"forfaits\": [], \"liens\": [] }");
            ProjectFile old = ProjectFileStore.Load(path, issues);
            Assert.Empty(issues);
            Assert.Null(old.Details);
            Assert.Null(old.Estimate);
            Assert.Null(old.Cover);
        }

        [Fact]
        public void Saisie_des_prix_conserve_les_prix_par_type_et_remplace_le_general()
        {
            BoqConfiguration config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 210m));
            config.Prices.Prices.Add(new PriceEntry { Code = "03.01", Unit = BoqUnit.CubicMeter, UnitPrice = 260m, Currency = Currency.USD, TypeName = TestData.List("*C30*") });
            config.Prices.Prices.Add(TestData.Price("05.01", BoqUnit.SquareMeter, 18m));

            List<PriceEdit> rows = PriceEditor.Rows(config.Mapping, config.Prices);
            Assert.Equal(config.Mapping.Articles.Select(a => a.Code), rows.Select(r => r.Code));
            PriceEdit beton = rows.Single(r => r.Code == "03.01");
            Assert.Equal(210m, beton.UnitPrice);
            Assert.Equal(1, beton.SpecificPrices);

            beton.UnitPrice = 225.5m;
            rows.Single(r => r.Code == "05.01").UnitPrice = null;                // prix retire
            PriceEdit acier = rows.Single(r => r.Code == "03.08");
            acier.UnitPrice = 2.1m;                                              // nouveau prix
            acier.Materials = 1.6m; acier.Labour = 0.4m; acier.Equipment = 0.1m;

            var issues = new List<BoqIssue>();
            PriceLibrary lib = PriceEditor.Apply(config.Prices, rows, issues);
            Assert.Empty(issues);
            Assert.Equal(225.5m, lib.Find("03.01").UnitPrice);
            Assert.Equal(260m, lib.Find("03.01", null, "Béton C30/37").UnitPrice);
            Assert.Null(lib.Find("05.01"));
            Assert.Equal(2.1m, lib.Find("03.08").Breakdown.Total);
            Assert.Equal(210m, config.Prices.Find("03.01").UnitPrice);         // l'original n'est pas modifie

            config.Prices = lib;
            Assert.DoesNotContain(ConfigurationValidator.Validate(config), i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Saisie_des_prix_refuse_decomposition_fausse_et_prix_negatif()
        {
            BoqConfiguration config = TestData.Config();
            List<PriceEdit> rows = PriceEditor.Rows(config.Mapping, config.Prices);
            rows[0].UnitPrice = 100m;
            rows[0].Materials = 60m;
            rows[0].Labour = 30m;
            rows[1].UnitPrice = -1m;
            rows[2].Labour = 5m;
            var issues = new List<BoqIssue>();
            Assert.Null(PriceEditor.Apply(config.Prices, rows, issues));
            Assert.Contains(issues, i => i.Code == "PRIX_DECOMPOSITION" && i.ArticleCode == rows[0].Code);
            Assert.Contains(issues, i => i.Code == "PRIX_NEGATIF");
            Assert.Contains(issues, i => i.Code == "PRIX_DECOMPOSITION_SANS_PU");
        }

        [Fact]
        public void Tableau_de_bord_reprend_le_BOQ_sans_rien_ajouter()
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Project.ReferenceArea = 500m;
            BoqDashboard d = BoqDashboard.Build(ctx.Boq, ctx.Audit, ctx.Quantities, ctx.Project,
                new DashboardOptions { Today = new DateTime(2027, 2, 1), ExchangeRateDate = new DateTime(2026, 11, 21) });

            Assert.Equal(ctx.Boq.TotalIncludingVat, d.TotalIncludingVat);
            Assert.Equal(ctx.Boq.Lots.Sum(l => l.Total), d.Lots.Sum(l => l.Amount));
            Assert.Equal(ctx.Boq.AllLines.Count(l => !l.Amount.HasValue), d.MissingPrices.Count);
            Assert.Equal(d.LineCount - d.MissingPrices.Count, d.PricedLineCount);
            Assert.Equal(ctx.Boq.TotalIncludingVat / 500m, d.CostPerSquareMeter);

            // Pareto trie, cumul croissant et borne.
            Assert.Equal(d.Pareto.Select(p => p.Amount).OrderByDescending(a => a), d.Pareto.Select(p => p.Amount));
            Assert.True(d.Pareto.Zip(d.Pareto.Skip(1), (a, b) => a.CumulativeShare <= b.CumulativeShare).All(x => x));
            Assert.True(d.Pareto.Last().CumulativeShare <= 1m);

            // Ratio acier = armatures / beton, en quantites nettes retenues.
            decimal concrete = d.KeyQuantities.Single(k => k.Kind == QuantityKinds.ConcreteVolume).Value;
            decimal rebar = d.KeyQuantities.Single(k => k.Kind == QuantityKinds.Reinforcement).Value;
            Assert.Equal(rebar / concrete, d.RebarRatio);

            Assert.Equal(SignalLevel.Critique, d.Signals[0].Level);
            Assert.Contains(d.Signals, s => s.Title.Contains("sans prix"));
            Assert.Contains(d.Signals, s => s.Title.StartsWith("Taux de change du 21/11/2026"));
            Assert.Equal(BoqDashboard.Score(d), d.HealthScore);
            Assert.InRange(d.HealthScore, 0, 100);
        }

        [Fact]
        public void Tableau_de_bord_devis_complet_sans_voyant_critique()
        {
            ExportContext ctx = ExportTests.Context(withMissingPrice: false);
            ctx.Project.ReferenceArea = 400m;
            BoqDashboard d = BoqDashboard.Build(ctx.Boq, ctx.Audit, ctx.Quantities, ctx.Project);
            // Seule la ligne non classee (lot NC, jamais tarifee) reste sans prix.
            Assert.All(d.MissingPrices, m => Assert.StartsWith("NC.", m.Code));
            Assert.Equal(d.LineCount - 1, d.PricedLineCount);
            Assert.Contains(d.Signals, s => s.Title.Contains("non classée"));
        }

        [Fact]
        public void Feuille_tableau_de_bord_totaux_en_formules_egaux_au_moteur()
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Project.ReferenceArea = 250m;
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.Build(wb, ctx);
                IXLWorksheet ws = wb.Worksheet(BoqWorkbookWriter.DashboardSheetName);
                IXLCell ttc = ws.CellsUsed().First(c => c.Address.RowNumber > 1 && c.HasFormula && c.FormulaA1.StartsWith("'BOQ'!") &&
                                                        ws.Cell(c.Address.RowNumber - 1, c.Address.ColumnNumber).GetString() == "TOTAL TTC");
                Assert.Equal(ctx.Boq.TotalIncludingVat, (decimal)ttc.Value.GetNumber());
                IXLCell perM2 = ws.CellsUsed().First(c => c.Address.RowNumber > 1 && ws.Cell(c.Address.RowNumber - 1, c.Address.ColumnNumber).GetString() == "COÛT TTC AU M²");
                Assert.Equal(Math.Round(ctx.Boq.TotalIncludingVat / 250m, 6), Math.Round((decimal)perM2.Value.GetNumber(), 6));
                Assert.Contains(ws.CellsUsed(), c => c.GetString() == "VOYANTS");
                Assert.Contains(ws.CellsUsed(), c => c.GetString().StartsWith("PRIX À SAISIR"));
            }
        }

        [Theory]
        [InlineData(CoverTheme.Futuriste, false)]
        [InlineData(CoverTheme.Classique, false)]
        [InlineData(CoverTheme.Futuriste, true)]
        public void PDF_et_classeur_avec_page_de_garde_et_tableau_de_bord(CoverTheme theme, bool importedPage)
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Project.Purpose = "Construction d'un immeuble R+3 à usage de bureaux";
            ctx.Project.ValidityDays = "90";
            ctx.Cover = new CoverSettings
            {
                Theme = theme,
                Subtitle = "Dossier d'appel d'offres",
                ProjectImage = Png(dir, "rendu.png"),
                ImportedPage = importedPage ? Png(dir, "garde.png") : null
            };
            byte[] pdf = BoqPdfWriter.Generate(ctx);
            Assert.True(pdf.Length > 1000);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));

            string xlsx = Path.Combine(dir, "devis.xlsx");
            BoqWorkbookWriter.Write(ctx, xlsx);
            using (var wb = new XLWorkbook(xlsx))
            {
                Assert.Equal("Page de garde", wb.Worksheet(1).Name);
                Assert.Equal(BoqWorkbookWriter.DashboardSheetName, wb.Worksheet(2).Name);
                Assert.Equal(importedPage ? 2 : 1, wb.Worksheet(1).Pictures.Count);
            }
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class PricedQuantityDecisionTests
    {
        [Fact]
        public void Configuration_livree_valorise_la_quantite_nette()
        {
            LoadResult load = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory());
            Assert.False(load.HasErrors, string.Join(" | ", load.Issues));
            Assert.Equal(PricedQuantity.Nette, load.Configuration.Settings.PricedQuantity);
            Assert.Equal(PricedQuantity.Nette, new EstimateSettings().PricedQuantity);
        }

        [Fact]
        public void En_nette_les_pertes_ne_changent_pas_le_montant()
        {
            BoqConfiguration config = TestData.Config();
            config.Settings.PricedQuantity = PricedQuantity.Nette;
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 200m));
            config.Coefficients.Items.Add(new Coefficient { Code = "PERTE", Designation = "Pertes", Value = 0.05m, AppliesTo = TestData.List("03.01") });
            BoqLine line = new BoqEngine().Build(new[] { TestData.Concrete(1, 10.0) }, config).AllLines.Single();
            Assert.Equal(10m, line.Quantity);
            Assert.Equal(10.5m, line.GrossQuantity);
            Assert.Equal(2000m, line.Amount);
        }
    }
}
