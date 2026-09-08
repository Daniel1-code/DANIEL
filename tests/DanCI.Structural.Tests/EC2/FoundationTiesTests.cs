using DanCI.Structural.Eurocodes.EC8;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Longrines de liaison, EN 1998-5 5.4.1.2 et EN 1998-1 5.8. Les valeurs de reference
    /// sont lues directement dans les articles.
    /// </summary>
    public class FoundationTiesTests
    {
        [Theory]
        [InlineData(GroundType.A, 0.0)]
        [InlineData(GroundType.B, 0.3)]
        [InlineData(GroundType.C, 0.4)]
        [InlineData(GroundType.D, 0.6)]
        public void Epsilon_FollowsClause54127(GroundType ground, double expected)
        {
            Assert.Equal(expected, FoundationTies.Epsilon(ground), 3);
        }

        [Fact]
        public void GroundTypeE_IsTreatedAsTheMostOnerousTabulatedClass()
        {
            // Sol stratifie : l'article ne le tabule pas, le moteur retient la valeur la
            // plus defavorable plutot que de ne rien exiger.
            Assert.Equal(FoundationTies.Epsilon(GroundType.D),
                         FoundationTies.Epsilon(GroundType.E), 3);
        }

        [Fact]
        public void RockNeedsNoTie()
        {
            TieForceResult result = FoundationTies.Compute(1_000_000, 0.20, 1.20, GroundType.A);

            Assert.False(result.TieRequired);
            Assert.Equal(0.0, result.AxialForceN, 3);
            Assert.Contains("aucune longrine de liaison", result.Justification);
        }

        [Fact]
        public void TieForce_MatchesHandCalculation()
        {
            // N_Ed = 1 200 kN, alpha = 0,20, S = 1,15, sol C -> epsilon = 0,40
            //   N = 0,40 x 0,20 x 1,15 x 1 200 = 110,4 kN
            TieForceResult result = FoundationTies.Compute(1_200_000, 0.20, 1.15, GroundType.C);

            Assert.True(result.TieRequired);
            Assert.Equal(110_400.0, result.AxialForceN, 0);
            Assert.Contains("5.4.1.2(7)", result.Justification);
            Assert.Contains("alterne", result.Justification);
        }

        [Fact]
        public void SofterGroundDemandsMoreTie()
        {
            double b = FoundationTies.Compute(1_000_000, 0.2, 1.2, GroundType.B).AxialForceN;
            double c = FoundationTies.Compute(1_000_000, 0.2, 1.2, GroundType.C).AxialForceN;
            double d = FoundationTies.Compute(1_000_000, 0.2, 1.2, GroundType.D).AxialForceN;

            Assert.True(b < c);
            Assert.True(c < d);
        }

        [Theory]
        // EN 1998-1 5.8.1(4) : 0,40 m jusqu'a trois niveaux, 0,50 m a partir de quatre.
        [InlineData(1, 400.0)]
        [InlineData(3, 400.0)]
        [InlineData(4, 500.0)]
        [InlineData(10, 500.0)]
        public void MinimumHeight_FollowsClause5814(int storeys, double expected)
        {
            Assert.Equal(expected, FoundationTies.MinimumHeightMm(storeys), 1);
        }

        [Fact]
        public void MinimumWidth_IsTwoHundredFifty()
        {
            Assert.Equal(250.0, FoundationTies.MinimumWidthMm, 1);
        }

        [Fact]
        public void MinimumSteel_IsFourPerThousandOnEachFace()
        {
            // Longrine 300 x 500 : A_c = 150 000 mm2 -> 0,004 x 150 000 = 600 mm2 par nappe
            string justification;
            double area = FoundationTies.MinimumSteelPerFace(150000.0, out justification);

            Assert.Equal(600.0, area, 1);
            Assert.Contains("EN HAUT ET EN BAS", justification);
        }

        [Fact]
        public void TensionSteel_IsTheForceOverFyd()
        {
            // 110,4 kN / 434,78 = 254 mm2
            Assert.Equal(254.0, FoundationTies.TensionSteel(110_400.0, 434.78), 0);
            // Une compression ne demande aucun acier de traction.
            Assert.Equal(0.0, FoundationTies.TensionSteel(-50_000.0, 434.78), 3);
        }

        [Fact]
        public void CompressionResistance_AddsConcreteAndSteel()
        {
            // 150 000 x 16,667 + 1 200 x 434,78 = 2 500 050 + 521 736 = 3 021 786 N
            double resistance = FoundationTies.CompressionResistance(150000.0, 1200.0,
                                                                     16.667, 434.78);
            Assert.InRange(resistance, 3_000_000.0, 3_040_000.0);
        }
    }
}
