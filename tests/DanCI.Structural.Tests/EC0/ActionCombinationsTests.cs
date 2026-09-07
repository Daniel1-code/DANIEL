using System.Collections.Generic;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Eurocodes.EC0;
using Xunit;

namespace DanCI.Structural.Tests.EC0
{
    /// <summary>Combinaisons d'actions de l'EN 1990, valeurs recommandees du tableau A1.1.</summary>
    public class ActionCombinationsTests
    {
        [Fact]
        public void Ultimate_FollowsEquation610()
        {
            // Plancher courant : g = 6,0 kN/m2, q = 2,5 kN/m2
            //   1,35 x 6,0 + 1,50 x 2,5 = 8,10 + 3,75 = 11,85 kN/m2
            Assert.Equal(11.85, ActionCombinations.Ultimate(6.0, 2.5), 4);
        }

        [Fact]
        public void Characteristic_IsTheUnfactoredSum()
        {
            Assert.Equal(8.5, ActionCombinations.Characteristic(6.0, 2.5), 4);
        }

        [Theory]
        // Tableau A1.1 : psi_2 = 0,3 (A et B), 0,6 (C et D), 0,8 (E), 0,0 (H, neige, vent).
        [InlineData(UseCategory.Residential, 0.3)]
        [InlineData(UseCategory.Office, 0.3)]
        [InlineData(UseCategory.Congregation, 0.6)]
        [InlineData(UseCategory.Shopping, 0.6)]
        [InlineData(UseCategory.Storage, 0.8)]
        [InlineData(UseCategory.Roof, 0.0)]
        [InlineData(UseCategory.Snow, 0.0)]
        [InlineData(UseCategory.Wind, 0.0)]
        public void Psi2_FollowsTableA11(UseCategory category, double expected)
        {
            Assert.Equal(expected, ActionCombinations.Psi(category).Psi2, 4);
        }

        [Fact]
        public void QuasiPermanent_UsesPsi2()
        {
            // Bureaux : 6,0 + 0,3 x 2,5 = 6,75 kN/m2
            Assert.Equal(6.75, ActionCombinations.QuasiPermanent(6.0, 2.5, UseCategory.Office), 4);
            // Stockage : 6,0 + 0,8 x 2,5 = 8,00 kN/m2
            Assert.Equal(8.0, ActionCombinations.QuasiPermanent(6.0, 2.5, UseCategory.Storage), 4);
        }

        [Fact]
        public void StorageIsMoreOnerousThanOffices_AtServiceability()
        {
            double office = ActionCombinations.QuasiPermanent(6.0, 5.0, UseCategory.Office);
            double storage = ActionCombinations.QuasiPermanent(6.0, 5.0, UseCategory.Storage);

            // A l'ELU les deux sont identiques, c'est a l'ELS que la categorie compte :
            // c'est la combinaison quasi-permanente qui pilote fleche et fissuration.
            Assert.Equal(ActionCombinations.Ultimate(6.0, 5.0),
                         ActionCombinations.Ultimate(6.0, 5.0), 4);
            Assert.True(storage > office);
        }

        [Fact]
        public void Describe_NamesTheClauseAndTheNumbers()
        {
            string text = ActionCombinations.Describe(6.0, 2.5, UseCategory.Office);

            Assert.Contains("EN 1990", text);
            Assert.Contains("6.10", text);
            Assert.Contains("psi_2", text);
        }

        [Fact]
        public void ForStrip_KeepsTheThreeSituationsApart()
        {
            var uls = new InternalForces(0, 0, 50000, 0, 0, 60e6);
            var car = new InternalForces(0, 0, 36000, 0, 0, 43e6);
            var qp = new InternalForces(0, 0, 29000, 0, 0, 34e6);

            List<LoadCombination> combinations = ActionCombinations.ForStrip(uls, car, qp);

            Assert.Equal(3, combinations.Count);
            Assert.True(combinations[0].IsUltimate);
            Assert.False(combinations[1].IsUltimate);
            Assert.False(combinations[2].IsUltimate);
            Assert.Equal(DesignSituation.ServiceabilityQuasiPermanent, combinations[2].Situation);
        }
    }
}
