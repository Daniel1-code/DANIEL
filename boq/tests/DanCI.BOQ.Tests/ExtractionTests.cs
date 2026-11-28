using System.Collections.Generic;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Export.Csv;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class OpeningDeductionTests
    {
        private static ExtractionSettings Rule(OpeningDeductionMode mode, decimal threshold = 0.5m) =>
            new ExtractionSettings { OpeningDeduction = mode, OpeningDeductionThresholdM2 = threshold };

        // Mur de 10 m² de face, perce par Revit d'une fenetre de 0,36 m² et d'une porte de 1,89 m².
        private const double Pierced = 10.0 - 0.36 - 1.89;

        private static List<WallOpening> Openings() => new List<WallOpening>
        {
            new WallOpening { ElementId = 1, AreaM2 = 0.36 },  // 0,60 x 0,60
            new WallOpening { ElementId = 2, AreaM2 = 1.89 }   // 0,90 x 2,10
        };

        [Fact]
        public void Toutes_les_ouvertures_deduites_la_face_percee_est_le_resultat()
        {
            DeductionResult r = OpeningDeductionRule.Apply(Pierced, Openings(), Rule(OpeningDeductionMode.Toutes));
            Assert.Equal(Pierced, r.AreaM2, 9);
            Assert.Equal(2, r.DeductedCount);
            Assert.Equal(Reliability.Measured, r.Reliability);
        }

        [Fact]
        public void Au_dela_du_seuil_seules_les_grandes_ouvertures_sont_deduites()
        {
            DeductionResult r = OpeningDeductionRule.Apply(Pierced, Openings(), Rule(OpeningDeductionMode.AuDelaDuSeuil, 0.5m));
            Assert.Equal(10.0 - 1.89, r.AreaM2, 9); // la fenetre de 0,36 m² est rajoutee
            Assert.Equal(1, r.KeptCount);
            Assert.Equal(1, r.DeductedCount);
            Assert.Contains("> 0,5 m²", r.Description);
        }

        [Fact]
        public void Une_ouverture_egale_au_seuil_n_est_pas_deduite()
        {
            var openings = new List<WallOpening> { new WallOpening { ElementId = 1, AreaM2 = 0.5 } };
            DeductionResult r = OpeningDeductionRule.Apply(9.5, openings, Rule(OpeningDeductionMode.AuDelaDuSeuil, 0.5m));
            Assert.Equal(10.0, r.AreaM2, 9);
        }

        [Fact]
        public void Aucune_deduction_donne_la_surface_brute()
        {
            DeductionResult r = OpeningDeductionRule.Apply(Pierced, Openings(), Rule(OpeningDeductionMode.Aucune));
            Assert.Equal(10.0, r.AreaM2, 9);
        }

        [Fact]
        public void Une_ouverture_illisible_reste_deduite_et_la_quantite_devient_estimee()
        {
            var openings = Openings();
            openings.Add(new WallOpening { ElementId = 77, AreaM2 = null });
            DeductionResult r = OpeningDeductionRule.Apply(Pierced - 0.3, openings, Rule(OpeningDeductionMode.AuDelaDuSeuil));
            Assert.Equal(Reliability.Estimated, r.Reliability);
            Assert.Contains("77", r.Assumption);
        }

        [Fact]
        public void En_mode_toutes_une_ouverture_illisible_ne_change_rien()
        {
            var openings = new List<WallOpening> { new WallOpening { ElementId = 77, AreaM2 = null } };
            DeductionResult r = OpeningDeductionRule.Apply(8.0, openings, Rule(OpeningDeductionMode.Toutes));
            Assert.Equal(Reliability.Measured, r.Reliability);
            Assert.Equal(8.0, r.AreaM2, 9);
        }
    }

    public class ConcreteClassTests
    {
        [Theory]
        [InlineData("Béton C25/30", "C25/30")]
        [InlineData("BETON_C30/37_XC2", "C30/37")]
        [InlineData("Béton - c20/25 - coulé en place", "C20/25")]
        [InlineData("Poteau C 25 / 30", "C25/30")]
        [InlineData("Béton C100/115", "C100/115")]
        [InlineData("Béton armé", null)]
        [InlineData("ABC25/30", null)]
        [InlineData("", null)]
        [InlineData(null, null)]
        public void Classe_reconnue_seulement_si_explicite(string text, string expected)
        {
            Assert.Equal(expected, ConcreteClass.Parse(text));
        }
    }

    public class SplitByTypeTests
    {
        private static BoqConfiguration Config()
        {
            var config = TestData.Config();
            config.Mapping.Lots.Add(new Lot { Code = "07", Designation = "Menuiseries" });
            config.Mapping.Articles.Add(new Article { Code = "07.01", Lot = "07", Unit = BoqUnit.Each, Designation = "Porte", SplitBy = ArticleSplit.Type });
            config.Mapping.Rules.Add(TestData.Rule("PORTES", 70, "07.01", kind: "Comptage", category: "OST_Doors"));
            return config;
        }

        private static ElementQuantity Door(long id, string type, string w, string h) => new ElementQuantity
        {
            ElementId = id,
            Category = "OST_Doors",
            Family = "Porte simple",
            TypeName = type,
            Kind = QuantityKinds.Count,
            Value = 1,
            Unit = BoqUnit.Each,
            Parameters = new Dictionary<string, string> { { "LargeurMm", w }, { "HauteurMm", h } }
        };

        [Fact]
        public void Une_sous_ligne_par_type_avec_dimensions_dans_la_designation()
        {
            BoqResult r = new BoqEngine().Build(new[]
            {
                Door(1, "P90", "900", "2100"), Door(2, "P80", "800", "2100"), Door(3, "P90", "900", "2100")
            }, Config());

            List<BoqLine> lines = r.Lots.Single().Lines;
            Assert.Equal(new[] { "07.01.01", "07.01.02" }, lines.Select(l => l.Code).ToArray());
            Assert.All(lines, l => Assert.Equal("07.01", l.ArticleCode));
            Assert.Equal("Porte — Porte simple : P80 — 800 × 2100 mm", lines[0].Designation);
            Assert.Equal(2m, lines[1].Quantity);
            Assert.Equal(new long[] { 1, 3 }, lines[1].ElementIds.ToArray());
        }

        [Fact]
        public void Dimensions_variables_dans_un_meme_type_sont_signalees_dans_la_designation()
        {
            BoqResult r = new BoqEngine().Build(new[] { Door(1, "P", "900", "2100"), Door(2, "P", "800", "2100") }, Config());
            Assert.EndsWith("dimensions variables", r.AllLines.Single().Designation);
        }

        [Fact]
        public void Le_prix_d_un_type_se_rattache_par_nom_de_type_avec_repli_sur_le_prix_general()
        {
            var config = Config();
            PriceEntry p90 = TestData.Price("07.01", BoqUnit.Each, 250m);
            p90.TypeName = TestData.List("P90*");
            config.Prices.Prices.Add(p90);
            config.Prices.Prices.Add(TestData.Price("07.01", BoqUnit.Each, 180m));

            BoqResult r = new BoqEngine().Build(new[] { Door(1, "P90 isoplane", "900", "2100"), Door(2, "P80", "800", "2100") }, config);

            Assert.Equal(180m, r.AllLines.Single(l => l.Designation.Contains("P80")).UnitPrice);
            Assert.Equal(250m, r.AllLines.Single(l => l.Designation.Contains("P90")).UnitPrice);
            Assert.DoesNotContain(ConfigurationValidator.Validate(config), i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Prix_par_type_sur_un_article_non_ventile_signale()
        {
            var config = TestData.Config();
            PriceEntry p = TestData.Price("03.01", BoqUnit.CubicMeter, 100m);
            p.TypeName = TestData.List("*");
            config.Prices.Prices.Add(p);
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "PRIX_TYPE_INUTILE");
        }
    }

    public class ExtractionSettingsTests
    {
        [Fact]
        public void Les_conventions_livrees()
        {
            ExtractionSettings s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction;
            Assert.Equal(OpeningDeductionMode.AuDelaDuSeuil, s.OpeningDeduction);
            Assert.Equal(0.5m, s.OpeningDeductionThresholdM2);
            Assert.Equal(MasonryMeasureFace.Moyenne, s.MasonryFace);
            Assert.True(s.ConcreteMaterialNames.IsMatch("Béton armé coulé sur place"));
            Assert.True(s.ConcreteMaterialNames.IsMatch("Mat_C25/30"));
            Assert.False(s.ConcreteMaterialNames.IsMatch("Bloc de ciment creux"));
        }

        [Fact]
        public void Seuil_negatif_refuse()
        {
            var config = TestData.Config();
            config.Extraction.OpeningDeductionThresholdM2 = -1m;
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "SEUIL_NEGATIF");
        }
    }

    public class CsvTests
    {
        [Fact]
        public void Format_Excel_francais_point_virgule_et_virgule_decimale()
        {
            var w = new StringWriter();
            QuantityCsvWriter.Write(w, new[] { TestData.Concrete(42, 0.27) }, CsvFormat.ExcelFrancais);
            string[] lines = w.ToString().Split("\r\n");
            Assert.StartsWith("Document;ElementId;UniqueId;Categorie", lines[0]);
            Assert.Contains(";42;", lines[1]);
            Assert.Contains(";0,27;m³;", lines[1]);
        }

        [Fact]
        public void Les_champs_sont_echappes_et_les_formules_neutralisees()
        {
            CsvFormat f = CsvFormat.ExcelFrancais;
            Assert.Equal("\"a;b\"", f.Field("a;b"));
            Assert.Equal("\"dit \"\"x\"\"\"", f.Field("dit \"x\""));
            Assert.Equal("'=SOMME(A1)", f.Field("=SOMME(A1)"));
            Assert.Equal("-12", f.Field("-12"));
            Assert.Equal(string.Empty, f.Field(null));
        }

        [Fact]
        public void Pas_d_arrondi_visible_dans_le_metre_brut()
        {
            Assert.Equal("0,123456789", CsvFormat.ExcelFrancais.Number(0.123456789));
            Assert.Equal("1234567,5", CsvFormat.ExcelFrancais.Number(1234567.5));
        }

        [Fact]
        public void Les_lignes_du_BOQ_et_les_anomalies()
        {
            BoqResult r = new BoqEngine().Build(new[] { TestData.Concrete(1, 1.0), TestData.Concrete(2, -1.0) }, TestData.Config());
            var lines = new StringWriter();
            BoqLinesCsvWriter.Write(lines, r, CsvFormat.ExcelFrancais);
            Assert.Contains("03;03.01;03.01;", lines.ToString());

            var issues = new StringWriter();
            IssuesCsvWriter.Write(issues, r.Issues, CsvFormat.ExcelFrancais);
            string text = issues.ToString();
            Assert.Contains("Erreur;QUANTITE_NEGATIVE;", text);
            Assert.True(text.IndexOf("Erreur", System.StringComparison.Ordinal) < text.IndexOf("Avertissement", System.StringComparison.Ordinal) ||
                        !text.Contains("Avertissement"));
        }
    }
}
