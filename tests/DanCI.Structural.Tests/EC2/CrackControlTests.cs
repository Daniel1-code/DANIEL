using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Maitrise de la fissuration sans calcul direct, article 7.3. Les valeurs de reference
    /// sont lues directement dans les tableaux 7.2N et 7.3N.
    /// </summary>
    public class CrackControlTests
    {
        [Theory]
        [InlineData(ExposureClass.X0, 0.4)]
        [InlineData(ExposureClass.XC1, 0.4)]
        [InlineData(ExposureClass.XC2, 0.3)]
        [InlineData(ExposureClass.XC4, 0.3)]
        [InlineData(ExposureClass.XS3, 0.3)]
        public void RecommendedCrackWidth_FollowsTable71N(ExposureClass exposure, double expected)
        {
            Assert.Equal(expected, CrackControl.RecommendedCrackWidthMm(exposure), 3);
        }

        [Theory]
        // Tableau 7.2N, lignes exactes pour w_k = 0,3 mm.
        [InlineData(160.0, 32.0)]
        [InlineData(200.0, 25.0)]
        [InlineData(240.0, 16.0)]
        [InlineData(280.0, 12.0)]
        [InlineData(320.0, 10.0)]
        [InlineData(360.0, 8.0)]
        public void MaxBarDiameter_MatchesTable72N(double stress, double expected)
        {
            Assert.Equal(expected, CrackControl.MaxBarDiameter(stress, 0.3), 3);
        }

        [Theory]
        // Tableau 7.3N, lignes exactes pour w_k = 0,3 mm.
        [InlineData(160.0, 300.0)]
        [InlineData(200.0, 250.0)]
        [InlineData(240.0, 200.0)]
        [InlineData(280.0, 150.0)]
        [InlineData(320.0, 100.0)]
        public void MaxSpacing_MatchesTable73N(double stress, double expected)
        {
            Assert.Equal(expected, CrackControl.MaxSpacing(stress, 0.3), 3);
        }

        [Fact]
        public void IntermediateStress_IsInterpolated()
        {
            // A mi-chemin entre 200 MPa (25 mm) et 240 MPa (16 mm) : 20,5 mm.
            Assert.Equal(20.5, CrackControl.MaxBarDiameter(220.0, 0.3), 2);
            // Entre 200 (250 mm) et 240 (200 mm) : 225 mm.
            Assert.Equal(225.0, CrackControl.MaxSpacing(220.0, 0.3), 2);
        }

        [Fact]
        public void WiderCrackAllowance_IsLessDemanding()
        {
            // A contrainte egale, tolerer 0,4 mm de fissure permet des barres plus grosses
            // et plus espacees que 0,2 mm.
            Assert.True(CrackControl.MaxBarDiameter(240.0, 0.4)
                        > CrackControl.MaxBarDiameter(240.0, 0.2));
            Assert.True(CrackControl.MaxSpacing(240.0, 0.4)
                        > CrackControl.MaxSpacing(240.0, 0.2));
        }

        [Fact]
        public void OutsideTheTable_TheCriterionIsNotSatisfiedSilently()
        {
            // 0,2 mm a 400 MPa : le tableau 7.3N ne donne aucune valeur. Le moteur rend 0,
            // ce qui fait echouer le critere plutot que de laisser passer.
            Assert.Equal(0.0, CrackControl.MaxSpacing(400.0, 0.2), 3);

            CrackControlResult result = CrackControl.Check(400.0, 0.2, 8.0, 100.0);
            Assert.False(result.SpacingSatisfied);
        }

        [Fact]
        public void EitherCriterionIsEnough()
        {
            // sigma_s = 240 MPa, w = 0,3 : diametre max 16 mm, espacement max 200 mm.
            // HA20 e = 150 : le diametre depasse, l'espacement passe -> 7.3.3(2) satisfait.
            CrackControlResult result = CrackControl.Check(240.0, 0.3, 20.0, 150.0);

            Assert.False(result.DiameterSatisfied);
            Assert.True(result.SpacingSatisfied);
            Assert.True(result.Passes);
        }

        [Fact]
        public void NeitherCriterion_Fails()
        {
            // HA25 e = 300 a 320 MPa : 10 mm et 100 mm admissibles, les deux sont depasses.
            CrackControlResult result = CrackControl.Check(320.0, 0.3, 25.0, 300.0);

            Assert.False(result.DiameterSatisfied);
            Assert.False(result.SpacingSatisfied);
            Assert.False(result.Passes);
        }

        [Fact]
        public void NonUniformStressFactor_FollowsClause7322()
        {
            Assert.Equal(1.00, CrackControl.NonUniformStressFactor(200.0), 3);
            Assert.Equal(1.00, CrackControl.NonUniformStressFactor(300.0), 3);
            // Interpolation lineaire : a 550 mm, 1,0 - 0,35 x 250/500 = 0,825.
            Assert.Equal(0.825, CrackControl.NonUniformStressFactor(550.0), 3);
            Assert.Equal(0.65, CrackControl.NonUniformStressFactor(800.0), 3);
            Assert.Equal(0.65, CrackControl.NonUniformStressFactor(1200.0), 3);
        }

        [Fact]
        public void MinimumSteel_FollowsEquation71()
        {
            // Dalle de 200 mm, bande de 1 m : A_ct = 1000 x 100 = 100 000 mm2.
            // k_c = 0,4 (flexion), k = 1,0 (h <= 300), f_ct,eff = 2,6 MPa, sigma_s = 500 MPa.
            //   A_s,min = 0,4 x 1,0 x 2,6 x 100 000 / 500 = 208 mm2
            double asMin = CrackControl.MinimumSteel(100000.0, 2.6, 500.0);
            Assert.Equal(208.0, asMin, 1);
        }

        [Fact]
        public void SteelStress_FallsWithTheQuasiPermanentMoment()
        {
            // f_yd = 435 MPa, M_qp/M_Ed = 0,65, A_s,req/A_s,prov = 0,90
            //   sigma_s = 435 x 0,65 x 0,90 = 254,5 MPa
            double stress = CrackControl.SteelStress(435.0, 65.0, 100.0, 900.0, 1000.0);
            Assert.InRange(stress, 253.0, 256.0);
        }
    }
}
