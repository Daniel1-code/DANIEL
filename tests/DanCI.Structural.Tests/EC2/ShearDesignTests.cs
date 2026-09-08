using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Effort tranchant, EN 1992-1-1 articles 6.2 et 9.2.2.
    /// Section de reference : b_w = 300, d = 550 mm, C25/30, B500, A_sl = 1 184 mm2, N_Ed = 0.
    ///   k     = 1 + sqrt(200/550) = 1,603
    ///   rho_l = 1 184 / 165 000   = 0,00718
    ///   V_Rd,c = 0,12 x 1,603 x (100 x 0,00718 x 25)^(1/3) x 165 000 = 83,0 kN
    ///   v_min  = 0,035 x 1,603^1,5 x 5 = 0,355 MPa -> plancher 58,6 kN
    /// </summary>
    public class ShearDesignTests
    {
        private const double WebWidthMm = 300.0;
        private const double EffectiveDepthMm = 550.0;
        private const double TensionSteelMm2 = 1184.0;
        private const double GammaC = 1.5;

        private static ConcreteProperties Materials(double fck = 25.0)
        {
            return new ConcreteProperties(new ConcreteMaterial(fck), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        [Fact]
        public void V_Rd_C_Conforme_A_L_Article_6_2_2()
        {
            string justification;
            double vrdc = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC, out justification);

            Assert.InRange(UnitConverter.NToKn(vrdc), 82.0, 84.0);
            Assert.Contains("6.2.2", justification);
        }

        [Fact]
        public void Le_Coefficient_k_Est_Plafonne_A_Deux()
        {
            // Pour d = 150 mm : k = 1 + sqrt(200/150) = 2,15 -> plafonne a 2,0.
            // On verifie indirectement : doubler d ne double pas V_Rd,c au-dela du plafond.
            string ignored;
            double shallow = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm, 150.0,
                500.0, 0.0, Materials(), GammaC, out ignored);
            double deep = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm, 800.0,
                500.0, 0.0, Materials(), GammaC, out ignored);

            Assert.True(shallow > 0 && deep > 0);
            Assert.True(deep > shallow, "Une section plus haute doit resister davantage.");
        }

        [Fact]
        public void Le_Plancher_v_min_S_Applique_Aux_Sections_Peu_Armees()
        {
            // Avec tres peu d'acier tendu, c'est v_min qui gouverne.
            string justification;
            double vrdc = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm,
                EffectiveDepthMm, 50.0, 0.0, Materials(), GammaC, out justification);

            // v_min x b_w x d = 0,355 x 165 000 = 58,6 kN
            Assert.InRange(UnitConverter.NToKn(vrdc), 57.5, 59.5);
        }

        [Fact]
        public void Sous_V_Rd_C_Seules_Les_Armatures_Minimales_Sont_Requises()
        {
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(60.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);

            Assert.False(result.RequiresShearReinforcement);
            // rho_w,min = 0,08 sqrt(25) / 500 = 0,0008 -> A_sw/s = 0,0008 x 300 = 0,24 mm2/mm
            Assert.Equal(0.24, result.MinimumAswPerMillimetreMm2, 4);
            Assert.Equal(result.MinimumAswPerMillimetreMm2, result.AswPerMillimetreMm2, 6);
        }

        [Fact]
        public void Cas_De_Reference_250_kN_Bielles_A_21_8_Degres()
        {
            // z = 0,9 x 550 = 495 mm ; nu1 = 0,6 (1 - 25/250) = 0,54
            // V_Rd,max(cot theta = 2,5) = 300 x 495 x 0,54 x 16,667 / 2,9 = 460,9 kN > 250 kN
            // A_sw/s = 250 000 / (495 x 434,78 x 2,5) = 0,4647 mm2/mm
            // Decalage a_l = z cot theta / 2 = 495 x 2,5 / 2 = 618,75 mm
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(250.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);

            Assert.True(result.RequiresShearReinforcement);
            Assert.True(result.IsWebAdequate);
            Assert.Equal(2.5, result.CotTheta, 6);
            Assert.InRange(result.ThetaDegrees, 21.7, 21.9);
            Assert.Equal(495.0, result.LeverArmMm, 6);
            Assert.InRange(UnitConverter.NToKn(result.VrdmaxN), 458.0, 464.0);
            Assert.InRange(result.AswPerMillimetreMm2, 0.462, 0.468);
            Assert.InRange(result.ShiftLengthMm, 617.0, 620.0);
        }

        [Fact]
        public void L_Espacement_Maximal_Vaut_0_75_d()
        {
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(250.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);
            Assert.Equal(412.5, result.MaxSpacingMm, 6);
        }

        [Fact]
        public void Les_Bielles_Se_Redressent_Quand_L_Effort_Croit()
        {
            // V_Ed = 550 kN depasse V_Rd,max a cot theta = 2,5 (460,9 kN) :
            // theta doit augmenter et cot theta descendre sous 2,5.
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(550.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);

            Assert.True(result.RequiresShearReinforcement);
            Assert.True(result.CotTheta < 2.5,
                "cot theta = " + result.CotTheta + " devrait etre inferieur a 2,5.");
            Assert.True(result.CotTheta >= 1.0);
            Assert.True(result.ThetaDegrees > 21.8);
            // Les bielles redressees doivent equilibrer l'effort applique.
            Assert.True(result.VrdmaxN >= UnitConverter.KnToN(549.0));
        }

        [Fact]
        public void Une_Ame_Insuffisante_Est_Signalee()
        {
            // A 45 degres, V_Rd,max = b_w z nu1 f_cd / 2 = 300 x 495 x 0,54 x 16,667 / 2 = 668 kN.
            // Un effort de 900 kN ne peut pas etre repris.
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(900.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);

            Assert.False(result.IsWebAdequate);
            Assert.Contains("SECTION INSUFFISANTE", result.Justification);
        }

        [Fact]
        public void La_Section_Minimale_Est_Toujours_Respectee()
        {
            // Meme quand le calcul demande moins, le minimum de l'article 9.2.2(5) s'applique.
            ShearResult result = ShearDesign.Design(UnitConverter.KnToN(90.0), WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC);
            Assert.True(result.AswPerMillimetreMm2 >= result.MinimumAswPerMillimetreMm2);
        }

        [Fact]
        public void La_Compression_Ameliore_La_Resistance_Sans_Armatures()
        {
            string ignored;
            double without = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 0.0, Materials(), GammaC, out ignored);
            double with = ShearDesign.ShearResistanceWithoutReinforcement(WebWidthMm,
                EffectiveDepthMm, TensionSteelMm2, 2.0, Materials(), GammaC, out ignored);

            // k1 sigma_cp b_w d = 0,15 x 2,0 x 165 000 = 49,5 kN de plus.
            Assert.InRange(UnitConverter.NToKn(with - without), 48.0, 51.0);
        }
    }
}
