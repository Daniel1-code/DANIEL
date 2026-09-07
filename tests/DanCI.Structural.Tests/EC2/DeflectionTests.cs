using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Elancement limite de l'article 7.4.2. Les valeurs de reference sont calculees a la
    /// main a partir des equations 7.16a et 7.16b et du tableau 7.4N.
    /// </summary>
    public class DeflectionTests
    {
        [Theory]
        [InlineData(StructuralSystem.SimplySupported, 1.0)]
        [InlineData(StructuralSystem.EndSpan, 1.3)]
        [InlineData(StructuralSystem.InteriorSpan, 1.5)]
        [InlineData(StructuralSystem.FlatSlab, 1.2)]
        [InlineData(StructuralSystem.Cantilever, 0.4)]
        public void SystemFactor_FollowsTable74N(StructuralSystem system, double expected)
        {
            Assert.Equal(expected, Deflection.SystemFactor(system), 3);
        }

        [Fact]
        public void BasicRatio_UsesEquation716a_WhenLightlyReinforced()
        {
            // C25/30 : rho_0 = 10^-3 sqrt(25) = 0,005. Avec rho = 0,004 < rho_0 :
            //   rho_0/rho = 1,25
            //   l/d = 11 + 1,5 x 5 x 1,25 + 3,2 x 5 x (0,25)^1,5
            //       = 11 + 9,375 + 16 x 0,125 = 22,375
            double ratio = Deflection.BasicRatio(0.004, 0.0, 25.0);
            Assert.InRange(ratio, 22.2, 22.6);
        }

        [Fact]
        public void BasicRatio_UsesEquation716b_WhenHeavilyReinforced()
        {
            // C25/30, rho = 0,010 > rho_0 = 0,005, rho' = 0 :
            //   l/d = 11 + 1,5 x 5 x 0,005/0,010 + 0 = 11 + 3,75 = 14,75
            double ratio = Deflection.BasicRatio(0.010, 0.0, 25.0);
            Assert.InRange(ratio, 14.6, 14.9);
        }

        [Fact]
        public void BasicRatio_IncreasesWhenReinforcementDecreases()
        {
            // Moins d'acier tendu = section moins sollicitee = elancement admissible plus grand.
            double light = Deflection.BasicRatio(0.003, 0.0, 30.0);
            double heavy = Deflection.BasicRatio(0.012, 0.0, 30.0);
            Assert.True(light > heavy);
        }

        [Fact]
        public void Check_ProvidingMoreSteelRelaxesTheLimit_ButNotBeyondOnePointFive()
        {
            DeflectionResult modest = Deflection.Check(6000, 200, 1000, 800, 900, 0,
                StructuralSystem.SimplySupported, 25.0);
            DeflectionResult generous = Deflection.Check(6000, 200, 1000, 800, 4000, 0,
                StructuralSystem.SimplySupported, 25.0);

            Assert.InRange(modest.SteelProvisionFactor, 1.12, 1.13);
            // 4000/800 = 5,0, mais l'article plafonne la correction a 1,5.
            Assert.Equal(1.5, generous.SteelProvisionFactor, 3);
        }

        [Fact]
        public void Check_LongSpanWithPartitions_IsPenalised()
        {
            DeflectionResult without = Deflection.Check(9000, 300, 1000, 1000, 1000, 0,
                StructuralSystem.EndSpan, 25.0, 1.0, false);
            DeflectionResult with = Deflection.Check(9000, 300, 1000, 1000, 1000, 0,
                StructuralSystem.EndSpan, 25.0, 1.0, true);

            Assert.Equal(1.0, without.LongSpanFactor, 3);
            // 7 / 9 = 0,778
            Assert.InRange(with.LongSpanFactor, 0.77, 0.79);
            Assert.True(with.AllowableRatio < without.AllowableRatio);
        }

        [Fact]
        public void Check_WideFlangeIsPenalised()
        {
            DeflectionResult rectangular = Deflection.Check(6000, 500, 300, 1000, 1000, 0,
                StructuralSystem.SimplySupported, 25.0, 1.0);
            DeflectionResult flanged = Deflection.Check(6000, 500, 300, 1000, 1000, 0,
                StructuralSystem.SimplySupported, 25.0, 5.0);

            Assert.Equal(1.0, rectangular.FlangeFactor, 3);
            Assert.Equal(0.8, flanged.FlangeFactor, 3);
        }

        [Fact]
        public void Check_ThinSlabOnALongSpanFails()
        {
            // Dalle de 140 mm sur 6,00 m : l/d = 6000/115 = 52, largement au-dela.
            DeflectionResult result = Deflection.Check(6000, 115, 1000, 900, 950, 0,
                StructuralSystem.SimplySupported, 25.0);

            Assert.False(result.Passes);
            Assert.True(result.ActualRatio > result.AllowableRatio);
        }

        [Fact]
        public void Check_ReportsItsEquationAndFactors()
        {
            DeflectionResult result = Deflection.Check(5000, 200, 1000, 700, 800, 0,
                StructuralSystem.EndSpan, 25.0);

            Assert.Contains("7.4.2", result.Justification);
            Assert.Contains("rho_0", result.Justification);
            Assert.Contains("l/d", result.Justification);
            Assert.Equal(1.3, result.SystemFactor, 3);
        }
    }
}
