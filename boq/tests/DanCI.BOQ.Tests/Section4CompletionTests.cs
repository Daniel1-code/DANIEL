using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class EarthworkRuleTests
    {
        [Fact]
        public void Fouille_a_parois_verticales()
        {
            // Semelle 1 x 1 m, surlargeur 0,30 m, 1 m de profondeur : 1,6 x 1,6 x 1 = 2,56 m3.
            Assert.Equal(2.56, ExcavationRule.PitVolume(1.0, 1.0, 1.0, 0.30, 0), 9);
        }

        [Fact]
        public void Fouille_talutee_tronc_de_pyramide()
        {
            // Fond 1,6 x 1,6 ; talus 1/1 sur 1 m : tete 3,6 x 3,6. V = 1/3 (2,56 + 12,96 + 5,76) = 7,0933.
            Assert.Equal((2.56 + 12.96 + 5.76) / 3.0, ExcavationRule.PitVolume(1.0, 1.0, 1.0, 0.30, 1.0), 9);
        }

        [Fact]
        public void Pas_de_fouille_au_dessus_du_terrain()
        {
            Assert.Equal(0, ExcavationRule.PitVolume(1.0, 1.0, -0.2, 0.30, 0));
        }

        [Fact]
        public void Beton_de_proprete_face_elargie_du_debord()
        {
            // Semelle 1 x 1 : (1 + 4 x 0,05 + 4 x 0,05²) x 0,05 = 1,1 x 1,1 x 0,05 = 0,0605 m3.
            Assert.Equal(1.1 * 1.1 * 0.05, BlindingRule.Volume(1.0, 4.0, new BlindingSettings { ThicknessM = 0.05, OverhangM = 0.05 }), 12);
            Assert.Equal(0, BlindingRule.Volume(1.0, 4.0, new BlindingSettings { ThicknessM = 0 }));
        }
    }

    public class FinishClassifierTests
    {
        private static readonly FinishSettings S = new FinishSettings();

        [Theory]
        [InlineData("Enduit ciment intérieur", QuantityKinds.Plaster)]
        [InlineData("Peinture acrylique blanche", QuantityKinds.Paint)]
        [InlineData("Carrelage grès cérame 40x40", QuantityKinds.FloorFinish)]
        [InlineData("Membrane bitumineuse SBS", QuantityKinds.Waterproofing)]
        [InlineData("Tôle bac acier 0,5 mm", QuantityKinds.Roofing)]
        [InlineData("Béton C25/30", null)]
        [InlineData("Bloc de ciment creux", null)]
        public void Finition_reconnue_par_le_nom_du_materiau(string material, string expected)
        {
            Assert.Equal(expected, FinishClassifier.KindOf(material, S));
        }
    }

    public class SplitByParameterTests
    {
        private static ElementQuantity Pipe(long id, double m, string section) => new ElementQuantity
        {
            ElementId = id, Category = "OST_PipeCurves", TypeName = "PVC", Kind = QuantityKinds.Length,
            Value = m, Unit = BoqUnit.LinearMeter, Parameters = new Dictionary<string, string> { { "Section", section } }
        };

        [Fact]
        public void Une_ligne_par_diametre_dans_l_ordre_numerique_et_prix_par_valeur()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            config.Prices.Prices.Add(new PriceEntry { Code = "09.01", Unit = BoqUnit.LinearMeter, UnitPrice = 12m, Currency = Currency.USD, SplitValue = TestData.List("Ø 110") });
            config.Prices.Prices.Add(new PriceEntry { Code = "09.01", Unit = BoqUnit.LinearMeter, UnitPrice = 4m, Currency = Currency.USD });

            BoqResult r = new BoqEngine().Build(new[] { Pipe(1, 12.5, "Ø 110"), Pipe(2, 30, "Ø 20"), Pipe(3, 7.5, "Ø 110") }, config);
            List<BoqLine> lines = r.AllLines.Where(l => l.ArticleCode == "09.01").ToList();

            Assert.Equal(new[] { "09.01.01", "09.01.02" }, lines.Select(l => l.Code).ToArray());
            Assert.EndsWith("— Ø 20 mm", lines[0].Designation);
            Assert.EndsWith("— Ø 110 mm", lines[1].Designation);
            Assert.Equal(20m, lines[1].NetQuantity);
            Assert.Equal(12m, lines[1].UnitPrice); // prix propre au Ø 110
            Assert.Equal(4m, lines[0].UnitPrice);  // repli sur le prix general
        }

        [Fact]
        public void Ventilation_par_parametre_sans_parametre_refusee()
        {
            var config = TestData.Config();
            config.Mapping.Articles[0].SplitBy = ArticleSplit.Parametre;
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "VENTILATION_SANS_PARAMETRE");
        }
    }

    public class ShippedSection4ConfigurationTests
    {
        [Fact]
        public void Fouilles_au_volume_en_place_foisonnement_en_information()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            Assert.Empty(config.Coefficients.For("01.01")); // jamais de foisonnement dans le prix des fouilles
            var pit = new ElementQuantity
            {
                ElementId = 5, Category = "OST_StructuralFoundation", Kind = QuantityKinds.Excavation, Value = 2.56,
                Unit = BoqUnit.CubicMeter, Reliability = Reliability.Estimated, Assumption = "fouille estimée"
            };
            BoqLine line = new BoqEngine().Build(new[] { pit }, config).AllLines.Single();
            Assert.Equal("01.01", line.Code);
            Assert.Equal(2.56m, line.Quantity);
            Assert.Equal(3.2m, line.Derived.Single().Value); // 2,56 x 1,25 = 3,20 m3 a evacuer
        }

        [Fact]
        public void Les_nouvelles_natures_ont_toutes_un_article()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var q = new List<ElementQuantity>
            {
                new ElementQuantity { ElementId = 1, Kind = QuantityKinds.BlindingConcrete, Category = "OST_StructuralFoundation", Value = 0.06, Unit = BoqUnit.CubicMeter, Reliability = Reliability.Estimated, Assumption = "5 cm" },
                new ElementQuantity { ElementId = 2, Kind = QuantityKinds.Plaster, Category = "OST_Walls", Value = 12, Unit = BoqUnit.SquareMeter },
                new ElementQuantity { ElementId = 2, Kind = QuantityKinds.Paint, Category = "OST_Walls", Material = "Peinture", Value = 12, Unit = BoqUnit.SquareMeter },
                new ElementQuantity { ElementId = 3, Kind = QuantityKinds.FloorFinish, Category = "OST_Floors", Value = 30, Unit = BoqUnit.SquareMeter },
                new ElementQuantity { ElementId = 4, Kind = QuantityKinds.Roofing, Category = "OST_Roofs", Value = 80, Unit = BoqUnit.SquareMeter },
                new ElementQuantity { ElementId = 5, Kind = QuantityKinds.Waterproofing, Category = "OST_Roofs", Material = "Membrane", Value = 40, Unit = BoqUnit.SquareMeter },
                new ElementQuantity { ElementId = 6, Kind = QuantityKinds.Count, Category = "OST_PlumbingFixtures", TypeName = "WC", Value = 1, Unit = BoqUnit.Each },
                new ElementQuantity { ElementId = 7, Kind = QuantityKinds.Length, Category = "OST_Conduit", Value = 15, Unit = BoqUnit.LinearMeter,
                                      Parameters = new Dictionary<string, string> { { "Section", "Ø 20" } } }
            };
            BoqResult r = new BoqEngine().Build(q, config);
            Assert.DoesNotContain(r.AllLines, l => l.IsUnclassified);
            Assert.Equal(new[] { "02.01", "06.01", "06.02", "08.01", "08.02", "08.03", "09.02.01", "10.01.01" },
                r.AllLines.Select(l => l.Code).OrderBy(c => c).ToArray());
        }

        [Fact]
        public void Les_familles_generiques_sans_regle_remontent_en_non_classes()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var generic = new ElementQuantity { ElementId = 9, Kind = QuantityKinds.Count, Category = "OST_GenericModel", TypeName = "Regard 60x60", Value = 1, Unit = BoqUnit.Each };
            Assert.True(new BoqEngine().Build(new[] { generic }, config).AllLines.Single().IsUnclassified);
        }
    }
}
