using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Longueur de flambement d'un voile, article 12.6.5.1. Les valeurs de reference sont
    /// calculees a la main a partir des expressions de l'article.
    /// </summary>
    public class WallEffectiveLengthTests
    {
        [Fact]
        public void TopAndBottomOnly_GivesBetaOne()
        {
            WallBucklingResult result = WallEffectiveLength.Compute(3000, 0,
                WallRestraint.TopAndBottom);

            Assert.Equal(1.0, result.Beta, 3);
            Assert.Equal(3000.0, result.BucklingLengthMm, 1);
        }

        [Fact]
        public void Cantilever_GivesBetaTwo()
        {
            WallBucklingResult result = WallEffectiveLength.Compute(3000, 0,
                WallRestraint.Cantilever);

            Assert.Equal(2.0, result.Beta, 3);
            Assert.Equal(6000.0, result.BucklingLengthMm, 1);
        }

        [Fact]
        public void ThreeEdges_FollowsTheFormula()
        {
            // l_w = 3 000, b = 4 000 >= l_w :
            //   beta = 1 / (1 + (3 000 / (3 x 4 000))^2) = 1 / (1 + 0,0625) = 0,9412
            WallBucklingResult result = WallEffectiveLength.Compute(3000, 4000,
                WallRestraint.ThreeEdges);

            Assert.Equal(0.9412, result.Beta, 3);
            Assert.Equal(2823.5, result.BucklingLengthMm, 0);
        }

        [Fact]
        public void FourEdges_FollowsTheFormula()
        {
            // l_w = 3 000, b = 4 000 >= l_w :
            //   beta = 1 / (1 + (3 000 / 4 000)^2) = 1 / 1,5625 = 0,64
            WallBucklingResult result = WallEffectiveLength.Compute(3000, 4000,
                WallRestraint.FourEdges);

            Assert.Equal(0.64, result.Beta, 3);
            Assert.Equal(1920.0, result.BucklingLengthMm, 0);
        }

        [Fact]
        public void FourEdgesIsStifferThanThreeEdges()
        {
            WallBucklingResult three = WallEffectiveLength.Compute(3000, 4000,
                WallRestraint.ThreeEdges);
            WallBucklingResult four = WallEffectiveLength.Compute(3000, 4000,
                WallRestraint.FourEdges);

            Assert.True(four.Beta < three.Beta);
        }

        [Fact]
        public void NarrowPanel_FallsBackToTheReducedForm()
        {
            // b = 2 000 < l_w = 3 000 : beta = b / l_w = 0,667 pour trois rives.
            WallBucklingResult result = WallEffectiveLength.Compute(3000, 2000,
                WallRestraint.ThreeEdges);

            Assert.Equal(0.6667, result.Beta, 3);
        }

        [Fact]
        public void DistantLateralRestraint_IsFlaggedAsIneffective()
        {
            // Un voile de 3 m de haut tenu sur des rives distantes de 12 m : au milieu,
            // le voile ne sait pas que ses rives existent.
            WallBucklingResult far = WallEffectiveLength.Compute(3000, 12000,
                WallRestraint.ThreeEdges);
            WallBucklingResult near = WallEffectiveLength.Compute(3000, 4000,
                WallRestraint.ThreeEdges);

            Assert.False(far.LateralRestraintEffective);
            Assert.True(near.LateralRestraintEffective);
            Assert.Contains("partie courante", far.Justification);
        }

        [Fact]
        public void EveryCase_NamesItsClause()
        {
            foreach (WallRestraint restraint in new[]
            {
                WallRestraint.TopAndBottom, WallRestraint.ThreeEdges,
                WallRestraint.FourEdges, WallRestraint.Cantilever
            })
            {
                WallBucklingResult result = WallEffectiveLength.Compute(3000, 4000, restraint);
                Assert.Contains("12.6.5.1", result.Justification);
                Assert.True(result.Beta > 0);
            }
        }
    }
}
