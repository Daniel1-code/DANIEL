using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Ancrages et recouvrements, EN 1992-1-1 articles 8.4 et 8.7.
    ///
    /// Cas de reference calcule a la main : C25/30, B500, barre HA16 droite, bonnes
    /// conditions d'adherence, valeurs recommandees.
    ///   f_ctm      = 0,30 x 25^(2/3)            = 2,565 MPa
    ///   f_ctk,0,05 = 0,7 x 2,565                = 1,795 MPa
    ///   f_ctd      = 1,795 / 1,5                = 1,197 MPa
    ///   f_bd       = 2,25 x 1 x 1 x 1,197       = 2,693 MPa
    ///   sigma_sd   = 500 / 1,15                 = 434,8 MPa
    ///   l_b,rqd    = (16/4) x (434,8 / 2,693)   = 646 mm
    ///   l_0        = 1,5 x 646                  = 969 mm, soit 60,6 phi
    /// </summary>
    public class AnchorageTests
    {
        private static ConcreteProperties Materials(double fck = 25.0, double fyk = 500.0)
        {
            return new ConcreteProperties(new ConcreteMaterial(fck), new SteelMaterial(fyk),
                                          new RecommendedAnnex());
        }

        [Fact]
        public void Cas_De_Reference_C25_B500_HA16()
        {
            AnchorageResult result = Anchorage.Compute(16.0, Materials(), new RecommendedAnnex());

            Assert.InRange(result.BondStressMPa, 2.68, 2.70);
            Assert.InRange(result.RequiredAnchorageMm, 644.0, 648.0);
            Assert.InRange(result.LapLengthMm, 966.0, 972.0);
        }

        [Fact]
        public void Le_Recouvrement_Croit_Avec_Le_Diametre()
        {
            ConcreteProperties materials = Materials();
            var annex = new RecommendedAnnex();
            double lap12 = Anchorage.Compute(12.0, materials, annex).LapLengthMm;
            double lap20 = Anchorage.Compute(20.0, materials, annex).LapLengthMm;
            double lap25 = Anchorage.Compute(25.0, materials, annex).LapLengthMm;

            Assert.True(lap12 < lap20);
            Assert.True(lap20 < lap25);
        }

        [Fact]
        public void Un_Beton_Plus_Resistant_Reduit_Le_Recouvrement()
        {
            var annex = new RecommendedAnnex();
            double lapC25 = Anchorage.Compute(16.0, Materials(25.0), annex).LapLengthMm;
            double lapC35 = Anchorage.Compute(16.0, Materials(35.0), annex).LapLengthMm;

            Assert.True(lapC35 < lapC25);
        }

        [Fact]
        public void Les_Mauvaises_Conditions_D_Adherence_Allongent_L_Ancrage()
        {
            ConcreteProperties materials = Materials();
            var annex = new RecommendedAnnex();
            double good = Anchorage.Compute(16.0, materials, annex, true).RequiredAnchorageMm;
            double poor = Anchorage.Compute(16.0, materials, annex, false).RequiredAnchorageMm;

            // eta1 = 0,7 en mauvaises conditions : l_b,rqd est divise par 0,7.
            Assert.InRange(poor / good, 1.42, 1.44);
        }

        [Fact]
        public void Le_Recouvrement_Respecte_Le_Minimum_Absolu()
        {
            // Beton tres resistant et petite barre : le minimum 15 phi / 200 mm doit s'appliquer.
            AnchorageResult result = Anchorage.Compute(8.0, Materials(90.0), new RecommendedAnnex());
            Assert.True(result.LapLengthMm >= 200.0);
            Assert.True(result.LapLengthMm >= 15.0 * 8.0);
        }
    }
}
