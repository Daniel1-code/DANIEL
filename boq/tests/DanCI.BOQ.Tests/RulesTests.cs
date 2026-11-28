using System.Collections.Generic;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class UnitTests
    {
        [Theory]
        [InlineData("m3", BoqUnit.CubicMeter)]
        [InlineData("m³", BoqUnit.CubicMeter)]
        [InlineData("M2", BoqUnit.SquareMeter)]
        [InlineData("ml", BoqUnit.LinearMeter)]
        [InlineData("kg", BoqUnit.Kilogram)]
        [InlineData("U", BoqUnit.Each)]
        [InlineData("Ff", BoqUnit.LumpSum)]
        public void Les_unites_s_ecrivent_comme_dans_un_devis(string text, BoqUnit expected)
        {
            Assert.Equal(expected, BoqUnits.Parse(text));
        }

        [Fact]
        public void Une_unite_inconnue_est_refusee()
        {
            Assert.False(BoqUnits.TryParse("m4", out _));
            Assert.Throws<System.FormatException>(() => BoqUnits.Parse("pieds"));
        }

        [Fact]
        public void Symboles_affiches()
        {
            Assert.Equal("m³", BoqUnit.CubicMeter.Symbol());
            Assert.Equal("m²", BoqUnit.SquareMeter.Symbol());
            Assert.Equal("Ff", BoqUnit.LumpSum.Symbol());
        }
    }

    public class PatternTests
    {
        [Theory]
        [InlineData("*C25/30*", "Béton C25/30 - poteaux", true)]
        [InlineData("*c25/30*", "BÉTON C25/30", true)]
        [InlineData("*C25/30*", "Béton C30/37", false)]
        [InlineData("OST_Walls", "OST_Walls", true)]
        [InlineData("OST_Walls", "OST_WallsX", false)]
        [InlineData("Bloc ?0", "Bloc 20", true)]
        [InlineData("Bloc ?0", "Bloc 200", false)]
        [InlineData("a.b", "aXb", false)]
        public void Jokers_et_casse(string pattern, string value, bool expected)
        {
            Assert.Equal(expected, new TextPattern(pattern).IsMatch(value));
        }

        [Fact]
        public void Une_valeur_absente_ne_correspond_qu_a_l_etoile()
        {
            Assert.True(new TextPattern("*").IsMatch(null));
            Assert.False(new TextPattern("*C25*").IsMatch(null));
        }

        [Fact]
        public void Liste_de_motifs_alternatifs()
        {
            var list = new PatternList(new[] { "OST_Walls", "OST_Floors" });
            Assert.True(list.IsMatch("OST_Floors"));
            Assert.False(list.IsMatch("OST_Roofs"));
            Assert.True(new PatternList(new string[0]).IsMatch("n'importe quoi"));
        }
    }

    public class MappingTests
    {
        private static ElementQuantity Column() => TestData.Concrete(1, 1.0);

        [Fact]
        public void La_premiere_regle_par_ordre_croissant_gagne_quel_que_soit_l_ordre_du_fichier()
        {
            var catalog = TestData.Config().Mapping;
            catalog.Rules.Clear();
            catalog.Rules.Add(TestData.Rule("GENERIQUE", 50, "03.02", kind: "VolumeBeton"));
            catalog.Rules.Add(TestData.Rule("POTEAUX", 10, "03.01", kind: "VolumeBeton", category: "OST_StructuralColumns"));

            Assert.Equal("POTEAUX", catalog.Match(Column()).Id);
        }

        [Fact]
        public void Tous_les_criteres_doivent_correspondre()
        {
            var catalog = TestData.Config().Mapping;
            catalog.Rules.Clear();
            catalog.Rules.Add(TestData.Rule("C30", 10, "03.01", kind: "VolumeBeton", material: "*C30/37*"));
            Assert.Null(catalog.Match(Column()));
        }

        [Fact]
        public void Critere_sur_parametre()
        {
            var rule = new MappingRule
            {
                Id = "P",
                Order = 1,
                Article = "03.08",
                When = new RuleCriteria
                {
                    Parameters = new Dictionary<string, PatternList> { { "HoteCategorie", TestData.List("OST_StructuralFoundation") } }
                }
            };
            var rebar = TestData.Rebar(5, 10, Reliability.Measured);
            Assert.False(rule.When.IsMatch(rebar));
            rebar.Parameters["HoteCategorie"] = "OST_StructuralFoundation";
            Assert.True(rule.When.IsMatch(rebar));
        }

        [Fact]
        public void Deux_regles_de_meme_ordre_sont_une_erreur()
        {
            var config = TestData.Config();
            config.Mapping.Rules.Add(TestData.Rule("BIS", 10, "03.01", kind: "VolumeBeton"));
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "ORDRE_AMBIGU");
        }

        [Fact]
        public void Une_regle_sans_critere_est_une_erreur()
        {
            var config = TestData.Config();
            config.Mapping.Rules.Add(new MappingRule { Id = "TOUT", Order = 99, Article = "03.01", When = new RuleCriteria() });
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "REGLE_SANS_CRITERE");
        }

        [Fact]
        public void Une_exclusion_doit_donner_son_motif()
        {
            var config = TestData.Config();
            var rule = TestData.Rule("EXCL", 5, null, kind: "VolumeBeton");
            rule.Exclude = true;
            config.Mapping.Rules.Add(rule);
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "EXCLUSION_SANS_MOTIF");
            rule.Reason = "Éléments provisoires";
            Assert.DoesNotContain(ConfigurationValidator.Validate(config), i => i.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Regle_vers_un_article_inconnu()
        {
            var config = TestData.Config();
            config.Mapping.Rules.Add(TestData.Rule("X", 99, "99.99", kind: "VolumeBeton"));
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "REGLE_ARTICLE_INCONNU");
        }

        [Fact]
        public void Nature_inconnue_signalee()
        {
            var config = TestData.Config();
            config.Mapping.Rules.Add(TestData.Rule("X", 99, "03.01", kind: "VolumeBetonn"));
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "NATURE_INCONNUE" && i.Severity == IssueSeverity.Warning);
        }

        [Fact]
        public void Le_lot_NC_est_reserve()
        {
            var config = TestData.Config();
            config.Mapping.Lots.Add(new Lot { Code = "NC", Designation = "x" });
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "LOT_RESERVE");
        }

        [Fact]
        public void La_configuration_de_test_est_valide()
        {
            Assert.DoesNotContain(ConfigurationValidator.Validate(TestData.Config()), i => i.Severity == IssueSeverity.Error);
        }
    }
}
