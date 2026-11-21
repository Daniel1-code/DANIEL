using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Reference;
using DanCI.BOQ.Core.Rules;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ShippedConfigurationTests
    {
        [Fact]
        public void Les_fichiers_livres_se_chargent_sans_erreur()
        {
            LoadResult load = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory());
            Assert.False(load.HasErrors, string.Join(Environment.NewLine, load.Issues));
            Assert.DoesNotContain(load.Issues, i => i.Code == "FICHIER_ABSENT");
        }

        [Fact]
        public void La_structure_par_defaut_comporte_les_lots_00_a_11()
        {
            MappingCatalog m = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Mapping;
            Assert.Equal(Enumerable.Range(0, 12).Select(i => i.ToString("D2")).ToArray(), m.Lots.Select(l => l.Code).ToArray());
        }

        [Fact]
        public void Aucun_prix_n_est_livre_avec_l_extension()
        {
            PriceLibrary prices = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Prices;
            Assert.NotNull(prices);
            Assert.Empty(prices.Prices);
        }

        [Fact]
        public void Les_coefficients_livres_sont_tous_a_valider()
        {
            CoefficientSet set = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Coefficients;
            Assert.NotEmpty(set.Items);
            Assert.All(set.Items, c => Assert.True(c.NeedsValidation, c.Code));
        }

        [Fact]
        public void Les_parametres_par_defaut_TVA_16_USD()
        {
            EstimateSettings s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Settings;
            Assert.Equal(16m, s.VatPercent);
            Assert.Equal(Currency.USD, s.Currency);
            Assert.Equal(0, s.DecimalsFor(BoqUnit.Kilogram));
            Assert.Equal(2, s.DecimalsFor(BoqUnit.CubicMeter));
        }

        [Fact]
        public void Bout_en_bout_avec_la_configuration_livree()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var column = TestData.Concrete(1001, 0.9);
            var c2025 = TestData.Concrete(1002, 0.5, "OST_StructuralFraming");
            c2025.Material = "Béton C20/25";
            var foundationRebar = TestData.Rebar(1003, 120, Reliability.Measured);
            foundationRebar.Parameters["HoteCategorie"] = "OST_StructuralFoundation";
            var wall = new ElementQuantity
            {
                ElementId = 1004, Kind = QuantityKinds.Masonry, Category = "OST_Walls", TypeName = "Bloc creux 20 cm",
                Value = 25.0, Unit = BoqUnit.SquareMeter
            };
            var door = new ElementQuantity { ElementId = 1005, Kind = QuantityKinds.Count, Category = "OST_Doors", Value = 1, Unit = BoqUnit.Each };

            BoqResult r = new BoqEngine().Build(new[] { column, c2025, foundationRebar, wall, door }, config);

            Assert.Equal("03.01", Line(r, 1001).Code);
            Assert.Equal("03.06", Line(r, 1002).Code);
            Assert.Equal("02.05", Line(r, 1003).Code);
            Assert.Equal("05.01", Line(r, 1004).Code);
            Assert.Equal("07.01", Line(r, 1005).ArticleCode);
            Assert.Equal("07.01.01", Line(r, 1005).Code); // menuiseries ventilees par type
            Assert.Equal(0.927m, Line(r, 1001).GrossQuantity); // pertes 3 % a valider
            Assert.Equal(26.25m, Line(r, 1004).GrossQuantity); // casse 5 %
            Assert.Contains(r.Issues, i => i.Code == "COEF_A_VALIDER");
            Assert.Contains(r.Issues, i => i.Code == "PRIX_ABSENT");
            Assert.All(r.AllLines, l => Assert.Null(l.Amount));
        }

        private static BoqLine Line(BoqResult r, long id) => r.AllLines.Single(l => l.ElementIds.Contains(id));
    }

    public class JsonReadingTests
    {
        private static T Read<T>(string json, List<BoqIssue> issues) where T : class =>
            ConfigurationLoader.ReadText<T>(json, "test.json", issues);

        [Fact]
        public void Un_critere_s_ecrit_en_texte_ou_en_tableau_et_les_commentaires_sont_admis()
        {
            var issues = new List<BoqIssue>();
            var m = Read<MappingCatalog>(@"{
                // commentaire
                ""schemaVersion"": 1,
                ""lots"": [ { ""code"": ""03"", ""designation"": ""GO"" } ],
                ""articles"": [ { ""code"": ""03.01"", ""lot"": ""03"", ""unite"": ""m³"", ""designation"": ""Béton"" } ],
                ""regles"": [
                  { ""id"": ""A"", ""ordre"": 1, ""si"": { ""nature"": ""VolumeBeton"", ""categorie"": [""OST_Walls"", ""OST_Floors""] }, ""article"": ""03.01"" },
                ]
            }", issues);

            Assert.Empty(issues);
            Assert.Equal(BoqUnit.CubicMeter, m.Articles[0].Unit);
            Assert.Equal(2, m.Rules[0].When.Category.Items.Count);
            Assert.Single(m.Rules[0].When.Kind.Items);
        }

        [Fact]
        public void Une_propriete_inconnue_est_une_erreur()
        {
            // Une faute de frappe dans un critere ne doit pas produire une regle qui correspond a tout.
            var issues = new List<BoqIssue>();
            Read<MappingCatalog>(@"{ ""schemaVersion"": 1, ""regles"": [ { ""id"": ""A"", ""ordre"": 1, ""si"": { ""categorei"": ""OST_Walls"" }, ""article"": ""x"" } ] }", issues);
            BoqIssue issue = Assert.Single(issues);
            Assert.Equal("JSON_INVALIDE", issue.Code);
            Assert.Contains("categorei", issue.Message);
        }

        [Fact]
        public void Une_erreur_de_syntaxe_indique_la_ligne()
        {
            var issues = new List<BoqIssue>();
            Read<MappingCatalog>("{\n  \"schemaVersion\": 1,\n  \"lots\": [ { \"code\": \"03\" \"designation\": \"x\" } ]\n}", issues);
            BoqIssue issue = Assert.Single(issues);
            Assert.Contains("ligne 3", issue.Message);
            Assert.Equal("test.json", issue.File);
        }

        [Fact]
        public void Une_unite_inconnue_est_une_erreur_de_lecture()
        {
            var issues = new List<BoqIssue>();
            Read<MappingCatalog>(@"{ ""schemaVersion"": 1, ""articles"": [ { ""code"": ""1"", ""lot"": ""0"", ""unite"": ""pied3"", ""designation"": ""x"" } ] }", issues);
            Assert.Contains("pied3", Assert.Single(issues).Message);
        }

        [Fact]
        public void Les_enumerations_ne_s_ecrivent_pas_en_nombre()
        {
            var issues = new List<BoqIssue>();
            Read<EstimateSettings>(@"{ ""schemaVersion"": 1, ""devise"": 1 }", issues);
            Assert.Single(issues);
        }

        [Fact]
        public void Dossier_sans_fichier_de_regles()
        {
            string dir = Path.Combine(Path.GetTempPath(), "danci-boq-vide-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                LoadResult load = ConfigurationLoader.LoadDirectory(dir);
                Assert.True(load.HasErrors);
                Assert.Contains(load.Issues, i => i.Code == "FICHIER_ABSENT" && i.Severity == IssueSeverity.Error && i.File == BoqConfiguration.MappingFileName);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }

    public class ValidationTests
    {
        [Fact]
        public void Un_taux_de_change_doit_etre_date()
        {
            var config = TestData.Config();
            config.Settings.ExchangeRate = new ExchangeRate { CdfPerUsd = 2850m };
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "TAUX_NON_DATE" && i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Coefficient_saisi_en_pourcentage_entier_signale()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "P", Value = 5m, AppliesTo = TestData.List("03.01") });
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "COEF_ELEVE" && i.Severity == IssueSeverity.Warning);
        }

        [Fact]
        public void Coefficient_negatif_refuse()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new Coefficient { Code = "P", Value = -0.1m, AppliesTo = TestData.List("03.01") });
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "COEF_NEGATIF");
        }

        [Fact]
        public void Prix_dans_une_autre_unite_que_l_article()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.SquareMeter, 10m));
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "PRIX_UNITE" && i.ArticleCode == "03.01" && i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Decomposition_du_prix_incoherente()
        {
            var config = TestData.Config();
            PriceEntry p = TestData.Price("03.01", BoqUnit.CubicMeter, 100m);
            p.Breakdown = new PriceBreakdown { Materials = 60m, Labour = 30m, Equipment = 5m };
            config.Prices.Prices.Add(p);
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "PRIX_DECOMPOSITION");
        }

        [Fact]
        public void Prix_non_date_ou_sans_source_signale()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(new PriceEntry { Code = "03.01", Unit = BoqUnit.CubicMeter, UnitPrice = 10m });
            var issues = ConfigurationValidator.Validate(config);
            Assert.Contains(issues, i => i.Code == "PRIX_NON_DATE");
            Assert.Contains(issues, i => i.Code == "PRIX_SANS_SOURCE");
        }

        [Fact]
        public void Version_de_schema_non_prise_en_charge()
        {
            var config = TestData.Config();
            config.Mapping.SchemaVersion = 2;
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "SCHEMA_VERSION");
        }
    }

    public class RebarMassTests
    {
        [Theory]
        [InlineData(6)] [InlineData(8)] [InlineData(10)] [InlineData(12)] [InlineData(14)]
        [InlineData(16)] [InlineData(20)] [InlineData(25)] [InlineData(32)] [InlineData(40)]
        public void La_table_correspond_a_pi_d2_sur_4_fois_7850(int d)
        {
            double theoretical = Math.PI * Math.Pow(d / 1000.0, 2) / 4.0 * 7850.0;
            Assert.InRange((double)RebarMass.Get(d), theoretical - 0.001, theoretical + 0.001);
        }

        [Fact]
        public void Un_diametre_hors_table_n_est_pas_interpole()
        {
            Assert.False(RebarMass.TryGet(18, out _));
            Assert.Throws<ArgumentOutOfRangeException>(() => RebarMass.Get(18));
        }
    }
}
