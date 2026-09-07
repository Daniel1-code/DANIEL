using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Eurocodes.Detailing;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>Dispositions constructives des poteaux, EN 1992-1-1 article 9.5.</summary>
    public class ColumnDetailingTests
    {
        private static Ec2ColumnDetailing Code()
        {
            return new Ec2ColumnDetailing(new RecommendedAnnex());
        }

        [Fact]
        public void As_Min_Retient_Le_Terme_Le_Plus_Defavorable()
        {
            // Poteau 300 x 300 (Ac = 90 000 mm2), NEd = 800 kN, B500 (fyd = 434,8 MPa).
            //   0,10 NEd / fyd = 0,10 x 800 000 / 434,8 = 184 mm2
            //   0,002 Ac       = 180 mm2
            string justification;
            double asMin = Code().MinSteelArea(90000.0, 800000.0, 500.0, out justification);

            Assert.InRange(asMin, 183.0, 185.0);
            Assert.Contains("9.5.2", justification);
        }

        [Fact]
        public void As_Min_Se_Reduit_Au_Terme_Geometrique_Sans_Effort_Normal()
        {
            string justification;
            double asMin = Code().MinSteelArea(90000.0, 0.0, 500.0, out justification);
            Assert.Equal(180.0, asMin, 6);
        }

        [Fact]
        public void As_Max_Vaut_Quatre_Pourcent_De_La_Section()
        {
            Assert.Equal(3600.0, Code().MaxSteelArea(90000.0), 6);
        }

        [Fact]
        public void Le_Nombre_Minimal_De_Barres_Depend_De_La_Forme()
        {
            Assert.Equal(4, Code().MinBarCount(SectionShape.Rectangular));
            Assert.Equal(6, Code().MinBarCount(SectionShape.Circular));
        }

        [Theory]
        [InlineData(12, 6.0)]    // max(6 ; 12/4 = 3)  = 6
        [InlineData(20, 6.0)]    // max(6 ; 20/4 = 5)  = 6
        [InlineData(32, 8.0)]    // max(6 ; 32/4 = 8)  = 8
        [InlineData(40, 10.0)]   // max(6 ; 40/4 = 10) = 10
        public void Diametre_Minimal_Des_Cadres(double longitudinal, double expected)
        {
            Assert.Equal(expected, Code().MinTransverseDiameterMm(longitudinal), 6);
        }

        [Theory]
        [InlineData(16, 300, 300)]   // min(20x16 = 320 ; 300 ; 400) = 300
        [InlineData(12, 500, 240)]   // min(20x12 = 240 ; 500 ; 400) = 240
        [InlineData(25, 600, 400)]   // min(20x25 = 500 ; 600 ; 400) = 400
        public void Espacement_Maximal_Des_Cadres(double longitudinal, double minDimension,
                                                  double expected)
        {
            string justification;
            double spacing = Code().MaxStirrupSpacingMm(longitudinal, 8.0, minDimension,
                                                        out justification);
            Assert.Equal(expected, spacing, 6);
            Assert.Contains("9.5.3", justification);
        }

        [Theory]
        [InlineData(12, 20, 25.0)]   // max(12 ; 20 + 5 = 25 ; 20) = 25
        [InlineData(32, 20, 32.0)]   // max(32 ; 25 ; 20)          = 32
        [InlineData(8, 10, 20.0)]    // max(8 ; 15 ; 20)           = 20
        public void Espacement_Libre_Minimal_Entre_Barres(double diameter, double aggregate,
                                                          double expected)
        {
            Assert.Equal(expected, Code().MinClearBarSpacingMm(diameter, aggregate), 6);
        }

        [Fact]
        public void Les_Zones_Critiques_S_Allongent_En_Sismique()
        {
            // Hors sismique : la plus grande dimension. En sismique : max(hc ; lcl/6 ; 450).
            Assert.Equal(400.0, Code().CriticalZoneLengthMm(400.0, 3000.0, false), 6);
            Assert.Equal(500.0, Code().CriticalZoneLengthMm(400.0, 3000.0, true), 6);
        }

        [Fact]
        public void L_Annexe_Nationale_Pilote_Les_Coefficients()
        {
            // Aucune valeur n'est codee en dur dans la regle : changer l'annexe change le resultat.
            var annex = new TighterAnnex();
            var code = new Ec2ColumnDetailing(annex);

            string justification;
            Assert.Equal(0.003 * 90000.0,
                         code.MinSteelArea(90000.0, 0.0, 500.0, out justification), 6);
            Assert.Equal(0.03 * 90000.0, code.MaxSteelArea(90000.0), 6);
        }

        /// <summary>Annexe fictive, uniquement destinee a prouver que les NDP sont bien injectes.</summary>
        private sealed class TighterAnnex : RecommendedAnnex
        {
            public override string Name { get { return "Annexe de test"; } }
            public override double ColumnMinSteelAreaRatio { get { return 0.003; } }
            public override double ColumnMaxSteelAreaRatio { get { return 0.03; } }
        }
    }
}
