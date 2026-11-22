using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class AmountInWordsTests
    {
        [Theory]
        [InlineData(0, "zéro")]
        [InlineData(1, "un")]
        [InlineData(17, "dix-sept")]
        [InlineData(21, "vingt et un")]
        [InlineData(71, "soixante et onze")]
        [InlineData(72, "soixante-douze")]
        [InlineData(80, "quatre-vingts")]
        [InlineData(81, "quatre-vingt-un")]
        [InlineData(91, "quatre-vingt-onze")]
        [InlineData(99, "quatre-vingt-dix-neuf")]
        [InlineData(100, "cent")]
        [InlineData(200, "deux cents")]
        [InlineData(201, "deux cent un")]
        [InlineData(1000, "mille")]
        [InlineData(2080, "deux mille quatre-vingts")]
        [InlineData(80000, "quatre-vingt mille")]
        [InlineData(200000, "deux cent mille")]
        [InlineData(1000000, "un million")]
        [InlineData(2300450, "deux millions trois cent mille quatre cent cinquante")]
        [InlineData(1000000000, "un milliard")]
        public void Nombres(long n, string expected) => Assert.Equal(expected, AmountInWords.Number(n));

        [Fact]
        public void Montants_avec_devise_et_centimes()
        {
            Assert.Equal("neuf mille sept cent neuf dollars américains et onze cents", AmountInWords.Format(9709.11m, Currency.USD));
            Assert.Equal("un dollar américain et un cent", AmountInWords.Format(1.01m, Currency.USD));
            Assert.Equal("un million de francs congolais", AmountInWords.Format(1000000m, Currency.CDF));
            Assert.Equal("vingt-sept millions six cent huit mille trois cent trente-neuf francs congolais et soixante-quatorze centimes",
                AmountInWords.Format(27608339.74m, Currency.CDF));
            Assert.StartsWith("Arrêté le présent devis à la somme de : Neuf mille", AmountInWords.Sentence(9709.11m, Currency.USD));
        }
    }
}

namespace DanCI.BOQ.Tests
{
    using DanCI.BOQ.Core.Dashboard;
    using DanCI.BOQ.Core.Materials;
    using DanCI.BOQ.Core.Planning;
    using DanCI.BOQ.Core.Revisions;
    using DanCI.BOQ.Export.Excel;
    using DanCI.BOQ.Export.Pdf;

    public class ImprovementsTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-ameliorations-" + Guid.NewGuid().ToString("N"));

        public ImprovementsTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        // ---------------------------------------------------------------- 7. QR
        [Fact]
        public void QR_porte_l_empreinte_complete_et_le_montant()
        {
            ExportContext ctx = ExportTests.Context();
            string payload = VerificationCode.Payload(ctx);
            Assert.Contains("Empreinte: " + ctx.Fingerprint, payload);
            Assert.Contains("TTC: " + ctx.Boq.TotalIncludingVat.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture), payload);
            byte[] png = VerificationCode.Png(payload);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, VerificationCode.Png(payload, 4, dark: true).Take(4).ToArray());
        }

        // ---------------------------------------------------------------- 8. Modeles
        [Fact]
        public void Modeles_aller_retour_liste_et_suppression()
        {
            var t = new QuoteTemplate
            {
                Name = "Client ABC / bureaux",
                Estimate = new ProjectEstimate { VatPercent = 16m, Markups = { new Markup { Code = "FG", Designation = "Frais généraux", Percent = 10m } } },
                Conditions = new QuoteConditions { ValidityDays = "60" },
                Prices = new List<PriceEntry> { TestData.Price("03.01", BoqUnit.CubicMeter, 250m) }
            };
            string path = QuoteTemplateStore.Save(dir, t);
            Assert.EndsWith("Client ABC _ bureaux.json", path);
            var issues = new List<BoqIssue>();
            List<QuoteTemplate> list = QuoteTemplateStore.List(dir, issues);
            Assert.Empty(issues);
            QuoteTemplate back = Assert.Single(list);
            Assert.Equal(10m, back.Estimate.Markups[0].Percent);
            Assert.Equal(250m, back.Prices[0].UnitPrice);
            Assert.Equal("TVA et majorations, 1 prix, conditions", back.Contents);
            QuoteTemplateStore.Delete(dir, t.Name);
            Assert.Empty(QuoteTemplateStore.List(dir, issues));
        }

        // ---------------------------------------------------------------- 2. Revisions
        [Fact]
        public void Revisions_instantane_et_comparaison()
        {
            ExportContext ctx = ExportTests.Context();
            BoqSnapshot a = BoqSnapshot.From(ctx.Boq, new ProjectInfo { Name = "P", Revision = "A" }, ctx.Fingerprint, new DateTime(2026, 11, 1));
            string folder = RevisionStore.FolderFor(Path.Combine(dir, "Immeuble.danciboq.json"));
            Assert.EndsWith("Immeuble_DanCI_revisions", folder);
            RevisionStore.Save(folder, a);

            // Revision B : un prix change, une ligne ajoutee, une supprimee.
            BoqSnapshot b = BoqSnapshot.From(ctx.Boq, new ProjectInfo { Name = "P", Revision = "B" }, ctx.Fingerprint, new DateTime(2026, 11, 20));
            SnapshotLine changed = b.Lines.First(l => l.Amount.HasValue);
            changed.UnitPrice += 10m;
            changed.Amount += 10m * changed.Quantity;
            b.Lines.Remove(b.Lines.Last());
            b.Lines.Add(new SnapshotLine { Code = "99.99", Lot = "99", Designation = "Nouveau", Unit = "U", Quantity = 1m, UnitPrice = 5m, Amount = 5m });
            b.TotalIncludingVat = a.TotalIncludingVat + 100m;
            RevisionStore.Save(folder, b);

            var issues = new List<BoqIssue>();
            List<BoqSnapshot> saved = RevisionStore.List(folder, issues);
            Assert.Equal(new[] { "B", "A" }, saved.Select(s => s.Revision));

            RevisionComparison cmp = RevisionComparer.Compare(saved[1], saved[0]);
            Assert.Equal(1, cmp.Lines.Count(l => l.Kind == ChangeKind.Added));
            Assert.Equal(1, cmp.Lines.Count(l => l.Kind == ChangeKind.Removed));
            LineChange price = cmp.Lines.Single(l => l.Code == changed.Code);
            Assert.Equal(ChangeKind.Modified, price.Kind);
            Assert.Equal("Prix", price.Cause);
            Assert.Equal(100m, cmp.DeltaTtc);
            Assert.Contains("métré IDENTIQUE", cmp.Summary());

            string xlsx = Path.Combine(dir, "cmp.xlsx");
            RevisionWorkbook.Write(xlsx, cmp);
            using (var doc = DocumentFormat.OpenXml.Packaging.SpreadsheetDocument.Open(xlsx, false))
            {
                Assert.Empty(new DocumentFormat.OpenXml.Validation.OpenXmlValidator().Validate(doc));
            }
            using (var wb = new XLWorkbook(xlsx))
            {
                Assert.Equal(new[] { "Synthèse", "Par lot", "Par article" }, wb.Worksheets.Select(w => w.Name).ToArray());
            }
        }

        // ---------------------------------------------------------------- 4. Materiaux
        [Fact]
        public void Materiaux_depuis_les_quantites_brutes_avec_le_calcul_en_clair()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "PERTE", Designation = "Pertes", Value = 0.05m, AppliesTo = TestData.List("03.01") });
            var beton = TestData.Concrete(1, 10.0);
            beton.Parameters["ClasseBeton"] = "C25/30";
            var acier = TestData.Rebar(2, 888.0, Reliability.Measured);
            acier.Parameters["DiametreMm"] = "12";
            var mur = new ElementQuantity { ElementId = 3, Kind = "Maconnerie", Category = "OST_Walls", Value = 100, Unit = BoqUnit.SquareMeter };
            BoqResult boq = new DanCI.BOQ.Core.Engine.BoqEngine().Build(new[] { beton, acier, mur }, config);

            var settings = new MaterialSettings
            {
                Concretes = { new ConcreteMix { Class = TestData.List("C25/30"), Designation = "C25/30", CementKg = 400m, SandM3 = 0.4m, GravelM3 = 0.8m, WaterL = 180m } }
            };
            MaterialList m = MaterialsCalculator.Build(boq, settings);
            // 10 m3 + 5 % de pertes = 10,5 m3 -> 4 200 kg de ciment.
            Assert.Equal(10.5m, m.Lines.Single(l => l.Material == "Béton C25/30").Quantity);
            Assert.Equal(4200m, m.Lines.Single(l => l.Material == "Ciment — Béton C25/30").Quantity);
            // 888 kg d'HA12 (0,888 kg/m) = 1 000 m = 84 barres de 12 m.
            Assert.Equal(84m, m.Lines.Single(l => l.Material.StartsWith("Acier HA Ø12 — barres")).Quantity);
            Assert.Equal(1250m, m.Lines.Single(l => l.Material == "Blocs de ciment").Quantity);
            MaterialLine bags = m.Totals.Single(t => t.Unit == "sacs");
            Assert.Equal(Math.Ceiling(m.Totals.Single(t => t.Material == "Ciment" && t.Unit == "kg").Quantity / 50m), bags.Quantity);
            Assert.All(m.Lines, l => Assert.False(string.IsNullOrWhiteSpace(l.Basis)));
            Assert.Contains(m.Notes, n => n.Contains("à valider"));
        }

        [Fact]
        public void Materiaux_configuration_livree_lisible()
        {
            var load = DanCI.BOQ.Core.Configuration.ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory());
            Assert.False(load.HasErrors, string.Join(" | ", load.Issues));
            Assert.True(load.Configuration.Materials.Concretes.Count >= 4);
            Assert.Equal(50m, load.Configuration.Materials.CementBagKg);
        }

        // ---------------------------------------------------------------- 6. Echeancier
        [Fact]
        public void Echeancier_somme_des_mois_egale_le_TTC_au_centime()
        {
            ExportContext ctx = ExportTests.Context(withMissingPrice: false);
            var schedule = new ProjectSchedule
            {
                TotalMonths = 7,
                StartDate = new DateTime(2027, 1, 1),
                Lots = { new LotPeriod { Lot = "03", StartMonth = 1, DurationMonths = 4 }, new LotPeriod { Lot = "05", StartMonth = 3, DurationMonths = 9 } }
            };
            ScheduleResult s = ScheduleCalculator.Build(ctx.Boq, schedule);
            Assert.Equal(7, s.Months.Count);
            Assert.Equal(ctx.Boq.TotalIncludingVat, s.Months.Sum(m => m.Ttc));
            Assert.Equal(ctx.Boq.TotalIncludingVat, s.Months.Last().CumulativeTtc);
            Assert.All(s.Months, m => Assert.True(m.Ttc >= 0m, m.Label + " : " + m.Ttc));
            Assert.All(s.Lots, l => Assert.Equal(l.Total, l.Months.Sum()));
            Assert.Contains(s.Notes, n => n.Contains("Lot 05") && n.Contains("durée réduite"));
            Assert.Contains(s.Notes, n => n.Contains("NC"));   // lot sans periode : toute la duree
            Assert.Equal(0m, s.Lots.Single(l => l.Lot == "03").Months[5]);
            Assert.StartsWith("janv.", s.Months[0].Label);

            ctx.Schedule = schedule;
            string xlsx = Path.Combine(dir, "e.xlsx");
            BoqWorkbookWriter.Write(ctx, xlsx);
            using (var wb = new XLWorkbook(xlsx))
            {
                IXLWorksheet ws = wb.Worksheet(BoqWorkbookWriter.ScheduleSheetName);
                IXLRow ttcRow = ws.RowsUsed().First(r => r.Cell(2).GetString() == "TTC du mois");
                decimal excel = Enumerable.Range(6, 7).Sum(c => (decimal)ttcRow.Cell(c).Value.GetNumber());
                Assert.Equal(ctx.Boq.TotalIncludingVat, Math.Round(excel, 2));
            }
            Assert.True(BoqPdfWriter.Generate(ctx).Length > 1000);
        }

        // ---------------------------------------------------------------- 5. Couleurs Revit
        [Fact]
        public void Coloration_par_lot_statut_prix()
        {
            ExportContext ctx = ExportTests.Context();
            ColoringResult lots = ModelColoring.Build(ctx.Boq, ColoringMode.Lot);
            Assert.Equal(Palette.ForLot("03"), lots.Colors[1]);
            ColoringResult prices = ModelColoring.Build(ctx.Boq, ColoringMode.Price);
            long rebar = ctx.Boq.AllLines.First(l => l.Code == "03.08").Contributions.First().Element.ElementId;
            Assert.Equal(Palette.Red, prices.Colors[rebar]);           // armatures sans prix
            Assert.Equal(Palette.Green, prices.Colors[1]);
            ColoringResult status = ModelColoring.Build(ctx.Boq, ColoringMode.Reliability);
            Assert.Equal(prices.Colors.Count, status.Legend.Sum(l => l.Count));
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class DashboardBoardTests
    {
        [Fact]
        public void Planche_SVG_complete_et_image_rendue()
        {
            ExportContext ctx = ExportTests.Context();
            var history = new[] { DanCI.BOQ.Core.Revisions.BoqSnapshot.From(ctx.Boq, new ProjectInfo { Revision = "A" }, "autre", new DateTime(2026, 10, 1)) };
            string svg = DanCI.BOQ.Export.Pdf.DashboardBoard.Svg(ctx, history);
            foreach (string title in new[] { "DYNAMIQUE DU DEVIS", "VOYANTS", "RÉPARTITION PAR LOT", "STATUT DU MONTANT", "SANTÉ DU DEVIS", "QUANTITÉS CLÉS", "PARETO", "PROFIL D&apos;AUDIT" })
            {
                Assert.Contains(title, svg);
            }
            System.Xml.Linq.XDocument.Parse(svg);   // SVG bien forme
            byte[] png = DanCI.BOQ.Export.Pdf.DashboardBoard.Png(ctx, history, 0.5);
            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47 }, png.Take(4).ToArray());
        }

        [Fact]
        public void Planche_sans_prix_ni_historique()
        {
            ExportContext ctx = ExportTests.Context();
            foreach (var l in ctx.Boq.AllLines) { l.Amount = null; l.UnitPrice = null; }
            ctx.Boq.WorksTotal = 0m;
            string svg = DanCI.BOQ.Export.Pdf.DashboardBoard.Svg(ctx, null);
            Assert.Contains("aucun montant", svg);
            System.Xml.Linq.XDocument.Parse(svg);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    using DanCI.BOQ.Core.Extraction;

    public class SubstructureTests
    {
        [Fact]
        public void Configuration_livree_valide_avec_les_nouveaux_articles()
        {
            var load = DanCI.BOQ.Core.Configuration.ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory());
            Assert.False(load.HasErrors, string.Join(" | ", load.Issues.Where(i => i.Severity == IssueSeverity.Error)));
            Assert.DoesNotContain(load.Issues, i => i.Code.Contains("NATURE"));
            foreach (string code in new[] { "02.06", "02.07", "02.08", "02.09", "02.10", "03.09" })
            {
                Assert.NotNull(load.Configuration.Mapping.FindArticle(code));
            }
            Assert.Equal(0.20, load.Configuration.Extraction.SlabsOnGrade.HardcoreThicknessM);
        }

        [Fact]
        public void Pieux_marques_coffrage_retire_et_ventiles_sur_leurs_articles()
        {
            var config = DanCI.BOQ.Core.Configuration.ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            ElementQuantity Q(string kind, BoqUnit unit, double v) => new ElementQuantity
            {
                ElementId = 7, Category = "OST_StructuralFoundation", Family = "Pieu foré béton", TypeName = "Ø600", Kind = kind, Unit = unit, Value = v,
                Material = "Béton C25/30"
            };
            var list = new List<ElementQuantity>
            {
                Q(QuantityKinds.ConcreteVolume, BoqUnit.CubicMeter, 2.83), Q(QuantityKinds.Formwork, BoqUnit.SquareMeter, 18.8),
                Q(QuantityKinds.Length, BoqUnit.LinearMeter, 10), Q(QuantityKinds.Count, BoqUnit.Each, 1),
                new ElementQuantity { ElementId = 8, Category = "OST_StructuralFoundation", Family = "Semelle isolée", Kind = QuantityKinds.ConcreteVolume, Unit = BoqUnit.CubicMeter, Value = 1, Material = "Béton C25/30" }
            };
            var notes = new List<BoqIssue>();
            Assert.Equal(1, PileRules.Apply(list, config.Extraction.Piles, notes));
            Assert.DoesNotContain(list, q => q.Kind == QuantityKinds.Formwork);
            Assert.Contains(notes, n => n.Code == "PIEUX_SANS_COFFRAGE");
            Assert.False(list.Last().Parameters.ContainsKey(PileRules.Parameter));   // la semelle n'est pas un pieu

            BoqResult boq = new DanCI.BOQ.Core.Engine.BoqEngine().Build(list, config);
            Assert.Equal(2.83m, boq.AllLines.Single(l => l.ArticleCode == "02.06").NetQuantity);
            Assert.Equal(10m, boq.AllLines.Single(l => l.ArticleCode == "02.07").NetQuantity);
            Assert.Equal(1m, boq.AllLines.Single(l => l.ArticleCode == "02.08").NetQuantity);
            Assert.Equal(1m, boq.AllLines.Single(l => l.ArticleCode == "02.03").NetQuantity);   // semelle : article des fondations
        }

        [Fact]
        public void Dallage_polyane_mesure_herisson_estime()
        {
            var items = SlabOnGradeRule.Quantities(120.0, byLowestLevel: false, new SlabOnGradeSettings()).ToList();
            var polyane = items.Single(i => i.kind == QuantityKinds.Polythene);
            Assert.Equal(120.0, polyane.value);
            Assert.Equal(Reliability.Measured, polyane.reliability);
            var herisson = items.Single(i => i.kind == QuantityKinds.Hardcore);
            Assert.Equal(24.0, herisson.value, 6);
            Assert.Equal(Reliability.Estimated, herisson.reliability);
            Assert.Contains("20 cm", herisson.assumption);

            var byLevel = SlabOnGradeRule.Quantities(50.0, byLowestLevel: true, new SlabOnGradeSettings()).ToList();
            Assert.All(byLevel, i => Assert.Equal(Reliability.Estimated, i.reliability));
            Assert.Empty(SlabOnGradeRule.Quantities(50.0, false, new SlabOnGradeSettings { Polythene = false, Hardcore = false }));
        }

        [Fact]
        public void Pieu_reconnu_par_famille_ou_type_uniquement_en_fondation()
        {
            var s = new PileSettings();
            Assert.True(PileRules.IsPile("OST_StructuralFoundation", "M_Pieu", null, s));
            Assert.True(PileRules.IsPile("OST_StructuralFoundation", "Fondation", "Pile 600", s));
            Assert.False(PileRules.IsPile("OST_StructuralFoundation", "Semelle filante", "S80", s));
            Assert.False(PileRules.IsPile("OST_StructuralColumns", "Pieu", null, s));
            Assert.False(PileRules.IsPile("OST_StructuralFoundation", "Pile Cap-3 Pile", "1800 x 1800", s));
            Assert.False(PileRules.IsPile("OST_StructuralFoundation", "Semelle sur pieux", "SP2", s));
        }
    }
}
