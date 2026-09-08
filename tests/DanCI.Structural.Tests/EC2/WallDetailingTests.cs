using DanCI.Structural.Eurocodes.Detailing;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>Dispositions constructives des voiles, article 9.6, valeurs recommandees.</summary>
    public class WallDetailingTests
    {
        private static readonly IWallDetailingCode Code = new Ec2WallDetailing();

        /// <summary>Voile de 200 mm sur une bande de 1 m : A_c = 200 000 mm2.</summary>
        private const double GrossArea = 200000.0;

        [Fact]
        public void MinimumVerticalSteel_IsTwoPerThousand()
        {
            string justification;
            double area = Code.MinVerticalSteel(GrossArea, out justification);

            // 0,002 x 200 000 = 400 mm2/m
            Assert.Equal(400.0, area, 1);
            Assert.Contains("9.6.2", justification);
        }

        [Fact]
        public void MaximumVerticalSteel_DoublesAtLaps()
        {
            Assert.Equal(8000.0, Code.MaxVerticalSteel(GrossArea, false), 1);
            Assert.Equal(16000.0, Code.MaxVerticalSteel(GrossArea, true), 1);
        }

        [Theory]
        // s <= min(3t ; 400)
        [InlineData(100.0, 300.0)]
        [InlineData(133.0, 399.0)]
        [InlineData(200.0, 400.0)]
        [InlineData(400.0, 400.0)]
        public void VerticalSpacing_FollowsClause9623(double thickness, double expected)
        {
            string justification;
            Assert.Equal(expected, Code.MaxVerticalSpacingMm(thickness, out justification), 1);
        }

        [Fact]
        public void HorizontalSteel_IsProportionalToWhatIsActuallyPlaced()
        {
            string justification;

            // Voile faiblement arme : 0,001 Ac = 200 mm2 gouverne devant 0,25 x 400 = 100.
            double light = Code.MinHorizontalSteel(GrossArea, 400.0, out justification);
            Assert.Equal(200.0, light, 1);

            // Voile fortement arme : 0,25 x 2 000 = 500 mm2 gouverne devant 200.
            double heavy = Code.MinHorizontalSteel(GrossArea, 2000.0, out justification);
            Assert.Equal(500.0, heavy, 1);
            Assert.Contains("proportionnel", justification);
        }

        [Fact]
        public void HorizontalSpacing_IsFourHundred()
        {
            Assert.Equal(400.0, Code.MaxHorizontalSpacingMm, 1);
        }

        [Fact]
        public void TransverseLinks_AppearOnlyAboveTwoPercent()
        {
            // 0,02 Ac = 4 000 mm2
            Assert.False(Code.RequiresTransverseLinks(GrossArea, 3900.0));
            Assert.True(Code.RequiresTransverseLinks(GrossArea, 4100.0));
        }
    }
}
