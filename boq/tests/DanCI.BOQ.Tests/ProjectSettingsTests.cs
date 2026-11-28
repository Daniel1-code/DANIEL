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
        public void Voyants_du_tableau_de_bord_ont_une_destination_cliquable()
        {
            ExportContext ctx = ExportTests.Context();
            BoqDashboard d = BoqDashboard.Build(ctx.Boq, ctx.Audit, ctx.Quantities, ctx.Project);
            var known = new[] { "prix", "audit", "nc", "devis", "identite", "boq" };
            Assert.NotEmpty(d.Signals);
            Assert.All(d.Signals, s => Assert.Contains(s.Target ?? "boq", known));
            Assert.Contains(d.Signals, s => s.Target == "prix");
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
                Assert.Equal(BoqWorkbookWriter.VisualSheetName, wb.Worksheet(2).Name);
                Assert.Equal(1, wb.Worksheet(2).Pictures.Count);
                Assert.Equal(BoqWorkbookWriter.DashboardSheetName, wb.Worksheet(3).Name);
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

namespace DanCI.BOQ.Tests
{
    public class PreviewTests
    {
        [Fact]
        public void Apercu_PDF_une_image_par_page_du_document_exporte()
        {
            ExportContext ctx = ExportTests.Context();
            IReadOnlyList<byte[]> pages = BoqPdfWriter.GeneratePageImages(ctx, 60);
            Assert.True(pages.Count >= 3, "page de garde + tableau de bord + BOQ");
            Assert.All(pages, p => Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, p.Take(4).ToArray()));
        }

        [Fact]
        public void Apercu_Excel_memes_feuilles_et_valeurs_calculees()
        {
            ExportContext ctx = ExportTests.Context();
            List<DanCI.BOQ.Core.Preview.SheetPreview> sheets = DanCI.BOQ.Export.Preview.WorkbookPreview.Build(ctx);
            Assert.Equal(new[] { "Page de garde", "Synthèse visuelle", "Tableau de bord", "BOQ", "Récapitulatif", "Bordereau des prix (BPU)", "BOQ descriptif", "Temps unitaires", "Matériaux", "BOQ par niveau", "Planning", "Détail du métré", "Audit", "Hypothèses", "Exclus" },
                sheets.Select(s => s.Name).ToArray());
            // Le TTC calcule par la formule Excel apparait dans l'apercu de la feuille BOQ.
            string ttc = ctx.Boq.TotalIncludingVat.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);
            var boqTexts = sheets.Single(s => s.Name == "BOQ").Rows.SelectMany(r => r.Cells).Select(c => c.Text.Replace(" ", ",").Replace(" ", ",")).ToList();
            Assert.Contains(boqTexts, t => t.Replace(",", string.Empty).Replace(".", string.Empty) == ttc.Replace(",", string.Empty).Replace(".", string.Empty));
            Assert.Contains(sheets.Single(s => s.Name == "Tableau de bord").Rows.SelectMany(r => r.Cells), c => c.Text == "VOYANTS" && c.ColumnSpan > 1);
            Assert.Equal(4, sheets.Single(s => s.Name == "Tableau de bord").ChartCount);
            Assert.DoesNotContain(sheets.SelectMany(s => s.Rows).SelectMany(r => r.Cells), c => (c.Text ?? string.Empty).StartsWith("(formule"));
        }

        [Fact]
        public void Apercu_Excel_limite_les_longues_feuilles()
        {
            ExportContext ctx = ExportTests.Context();
            var sheets = DanCI.BOQ.Export.Preview.WorkbookPreview.Build(ctx, maxRowsPerSheet: 10);
            var detail = sheets.Single(s => s.Name == "Détail du métré");
            Assert.Equal(10, detail.Rows.Count);
            Assert.True(detail.HiddenRows > 0);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class ExcelChartsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-charts-" + Guid.NewGuid().ToString("N"));

        public ExcelChartsTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        [Theory]
        [InlineData(CoverTheme.Futuriste)]
        [InlineData(CoverTheme.Classique)]
        public void Graphiques_natifs_valides_et_tableaux_filtrables(CoverTheme theme)
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Cover = new CoverSettings { Theme = theme };
            string path = Path.Combine(dir, "devis.xlsx");
            BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Write(ctx, path);
            Assert.Equal(4, map.Charts.Count);

            using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(path, false))
            {
                var errors = new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc).Take(10)
                    .Select(e => e.Part?.Uri + " : " + e.Path?.XPath + " : " + e.Description).ToList();
                Assert.True(errors.Count == 0, string.Join(Environment.NewLine, errors));
                var sheet = doc.WorkbookPart.Workbook.Sheets.Elements<DocumentFormat.OpenXml.Spreadsheet.Sheet>()
                    .Single(s => s.Name == BoqWorkbookWriter.DashboardSheetName);
                var part = (DocumentFormat.OpenXml.Packaging.WorksheetPart)doc.WorkbookPart.GetPartById(sheet.Id);
                Assert.Equal(4, part.DrawingsPart.ChartParts.Count());
            }

            // Le classeur se relit ; tableaux Excel (filtres) presents ; les graphiques pointent sur leurs plages.
            using (var wb = new XLWorkbook(path))
            {
                IXLWorksheet ws = wb.Worksheet(BoqWorkbookWriter.DashboardSheetName);
                Assert.Equal(new[] { "T_Articles", "T_Lots", "T_Statuts" }, ws.Tables.Select(tb => tb.Name).OrderBy(n => n).ToArray());
                Assert.Equal(ctx.Boq.AllLines.Count(), ws.Table("T_Articles").DataRange.RowCount());
                Assert.True(ws.Table("T_Articles").ShowAutoFilter);
                // Statuts : la somme des montants par statut = total des travaux du moteur.
                decimal sum = ws.Table("T_Statuts").DataRange.Rows().Sum(r => (decimal)r.Cell(2).Value.GetNumber());
                Assert.Equal(ctx.Boq.AllLines.Sum(l => l.Amount ?? 0m), sum);
            }
            ChartSpec lots = map.Charts.First();
            Assert.Equal(ctx.Boq.Lots.Select(l => DanCI.BOQ.Core.Dashboard.Palette.ForLot(l.Code)), lots.PointColors);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class PartiesTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-parties-" + Guid.NewGuid().ToString("N"));

        public PartiesTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private static PartyInfo Company(string logo = null) => new PartyInfo
        {
            ShortName = "DanCI", Name = "Dan Consult Intelligente", Activity = "Bureau d'études", Address = "Av. de la Justice 12", City = "Kinshasa",
            Country = "RDC", Phone = "+243 000 000 000", Email = "contact@exemple.cd", Rccm = "CD/KIN/RCCM/00-B-0000", IdNat = "01-000-N00000X",
            Nif = "A0000000X", Bank = "Banque exemple — compte 0000", Contact = "Ingénieur responsable", ContactRole = "Ingénieur civil", Logo = logo
        };

        [Fact]
        public void Entreprise_absente_valeurs_DanCI_et_aller_retour()
        {
            var issues = new List<BoqIssue>();
            string path = Path.Combine(dir, "entreprise.json");
            Assert.Equal("DanCI", CompanyProfileStore.Load(path, issues).DisplayShort);
            PartyInfo c = Company(logo: "logo.png");
            CompanyProfileStore.Save(path, c);
            PartyInfo back = CompanyProfileStore.Load(path, issues);
            Assert.Empty(issues);
            Assert.Equal(c.Rccm, back.Rccm);
            Assert.Equal(Path.Combine(dir, "logo.png"), back.Logo);
            Assert.Equal("RCCM CD/KIN/RCCM/00-B-0000  ·  Id. Nat. 01-000-N00000X  ·  NIF A0000000X", back.LegalLine);
        }

        [Fact]
        public void Identite_fiche_client_prime_et_entreprise_par_defaut()
        {
            var revit = new ProjectInfo { Name = "Projet", Client = "Client Revit" };
            ProjectInfo p = ProjectIdentity.Build(revit, new ProjectDetails { Client = "Client saisi" },
                new PartyInfo { Name = "Société Cliente SARL", City = "Lubumbashi" }, Company());
            Assert.Equal("Société Cliente SARL", p.Client);
            Assert.Equal("Lubumbashi", p.ClientParty.City);
            Assert.Equal("DanCI — Dan Consult Intelligente", p.Engineer);
            Assert.Equal("Ingénieur responsable, Ingénieur civil", p.Author);

            ProjectInfo q = ProjectIdentity.Build(revit, new ProjectDetails { Client = "Client saisi", Author = "Moi" }, new PartyInfo(), null);
            Assert.Equal("Client saisi", q.Client);
            Assert.Equal("Moi", q.Author);
            Assert.Equal("DanCI", q.Company.DisplayShort);
        }

        [Fact]
        public void Fiche_client_dans_le_fichier_projet()
        {
            string path = Path.Combine(dir, "p.danciboq.json");
            ProjectFileStore.Save(path, new ProjectFile { ClientParty = new PartyInfo { Name = "Client", Logo = "client.png" } });
            var issues = new List<BoqIssue>();
            ProjectFile back = ProjectFileStore.Load(path, issues);
            Assert.Empty(issues);
            Assert.Equal(Path.Combine(dir, "client.png"), back.ClientParty.Resolve(path).Logo);
        }

        [Fact]
        public void PDF_et_classeur_avec_entreprise_client_et_logos()
        {
            ExportContext ctx = ExportTests.Context();
            string companyLogo = ProjectSettingsTests.Png(dir, "societe.png");
            string clientLogo = ProjectSettingsTests.Png(dir, "client.png");
            ctx.Project = ProjectIdentity.Build(ctx.Project, null,
                new PartyInfo { Name = "Société Cliente SARL", Address = "Bd du 30 Juin", City = "Kinshasa", Contact = "M. Client", Phone = "+243 1", Logo = clientLogo },
                Company(companyLogo));
            Assert.Equal(companyLogo, ctx.EffectiveLogo);
            Assert.Equal(clientLogo, ctx.ClientLogo);
            byte[] pdf = BoqPdfWriter.Generate(ctx);
            Assert.True(pdf.Length > 1000);

            string xlsx = Path.Combine(dir, "d.xlsx");
            BoqWorkbookWriter.Write(ctx, xlsx);
            using (var wb = new XLWorkbook(xlsx))
            {
                IXLWorksheet cover = wb.Worksheet("Page de garde");
                Assert.Equal(2, cover.Pictures.Count);
                Assert.Contains(cover.CellsUsed(), c => c.GetString() == "DanCI");
                Assert.Contains(cover.CellsUsed(), c => c.GetString() == "Société Cliente SARL");
                Assert.Contains(cover.CellsUsed(), c => c.GetString().Contains("RCCM CD/KIN/RCCM/00-B-0000"));
            }
        }
    }
}
