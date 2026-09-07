using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Verifie les proprietes de calcul contre le tableau 3.1 de l'EN 1992-1-1.
    /// Valeurs de reference : C25/30 et C30/37, acier B500 (gamma_c = 1,5 ; gamma_s = 1,15).
    /// </summary>
    public class ConcretePropertiesTests
    {
        private static ConcreteProperties For(double fck)
        {
            return new ConcreteProperties(new ConcreteMaterial(fck), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        [Theory]
        [InlineData(20, 28)]
        [InlineData(25, 33)]
        [InlineData(30, 38)]
        [InlineData(50, 58)]
        public void Fcm_Vaut_Fck_Plus_Huit(double fck, double expected)
        {
            Assert.Equal(expected, For(fck).Fcm, 6);
        }

        [Theory]
        [InlineData(20, 2.21)]
        [InlineData(25, 2.56)]
        [InlineData(30, 2.90)]
        [InlineData(40, 3.51)]
        [InlineData(50, 4.07)]
        public void Fctm_Conforme_Au_Tableau_3_1(double fck, double expected)
        {
            Assert.Equal(expected, For(fck).Fctm, 2);
        }

        [Fact]
        public void Fcd_Applique_Alpha_Cc_Et_Gamma_C()
        {
            // f_cd = 1,0 x 25 / 1,5 = 16,667 MPa
            Assert.Equal(16.667, For(25).Fcd, 3);
        }

        [Fact]
        public void Fctd_Utilise_Le_Fractile_Cinq_Pourcent()
        {
            // f_ctk,0,05 = 0,7 x 2,565 = 1,796 ; f_ctd = 1,796 / 1,5 = 1,197 MPa
            ConcreteProperties properties = For(25);
            Assert.Equal(0.7 * properties.Fctm, properties.Fctk005, 6);
            Assert.Equal(1.197, properties.Fctd, 3);
        }

        [Fact]
        public void Fyd_Vaut_Fyk_Divise_Par_Gamma_S()
        {
            Assert.Equal(500.0 / 1.15, For(25).Fyd, 6);
        }

        [Theory]
        [InlineData(25, 31476)]
        [InlineData(30, 32837)]
        public void Ecm_Conforme_Au_Tableau_3_1(double fck, double expectedMPa)
        {
            // Le tableau 3.1 arrondit au GPa : on tolere 50 MPa d'ecart.
            Assert.InRange(For(fck).Ecm, expectedMPa - 50.0, expectedMPa + 50.0);
        }

        [Fact]
        public void Les_Deformations_Suivent_Le_Tableau_3_1_Pour_Beton_Courant()
        {
            ConcreteProperties properties = For(25);
            Assert.Equal(0.0020, properties.StrainC2, 6);
            Assert.Equal(0.0035, properties.StrainCu2, 6);
            Assert.Equal(2.0, properties.ParabolaExponent, 6);
        }

        [Fact]
        public void La_Loi_Parabole_Rectangle_Est_Continue_Et_Bornee()
        {
            ConcreteProperties properties = For(25);
            Assert.Equal(0.0, properties.ConcreteStress(-0.001), 9);
            Assert.Equal(0.0, properties.ConcreteStress(0.0), 9);
            Assert.Equal(properties.Fcd, properties.ConcreteStress(properties.StrainC2), 6);
            Assert.Equal(properties.Fcd, properties.ConcreteStress(properties.StrainCu2), 6);
            // A mi-parcours, la parabole donne 0,75 f_cd pour n = 2.
            Assert.Equal(0.75 * properties.Fcd, properties.ConcreteStress(0.001), 6);
        }

        [Fact]
        public void L_Acier_Est_Elastoplastique_Parfait()
        {
            ConcreteProperties properties = For(25);
            Assert.Equal(200.0, properties.SteelStress(0.001), 6);         // domaine elastique
            Assert.Equal(properties.Fyd, properties.SteelStress(0.01), 6);  // palier plastique
            Assert.Equal(-properties.Fyd, properties.SteelStress(-0.01), 6);
        }
    }
}
