using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Flexion simple, EN 1992-1-1 article 6.1 (diagramme rectangulaire) et article 9.2.1.
    /// Section de reference : 300 x 600, d = 550 mm, C25/30, B500.
    ///   f_cd = 16,667 MPa   f_yd = 434,78 MPa   b d^2 f_cd = 1 512,6 x 10^6 N.mm
    /// </summary>
    public class BendingDesignTests
    {
        private const double WidthMm = 300.0;
        private const double EffectiveDepthMm = 550.0;
        private const double CompressionSteelDepthMm = 50.0;

        private static ConcreteProperties Materials(double fck = 25.0)
        {
            return new ConcreteProperties(new ConcreteMaterial(fck), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        [Fact]
        public void La_Limite_D_Axe_Neutre_Vaut_0_448_Sans_Redistribution()
        {
            // EC2 5.5(4) : x/d <= (delta - k1)/k2 = (1 - 0,44)/1,25 = 0,448
            Assert.Equal(0.448, BendingDesign.NeutralAxisLimit(1.0), 3);
            // mu_lim = 0,8 x 0,448 x (1 - 0,4 x 0,448) = 0,2942
            Assert.Equal(0.2942, BendingDesign.ReducedMomentLimit(0.448), 4);
        }

        [Fact]
        public void Cas_De_Reference_250_kNm_Section_Simplement_Armee()
        {
            // mu = 250e6 / 1 512,6e6 = 0,1653
            // x/d = 1,25 (1 - sqrt(1 - 2,5 x 0,1653)) = 0,2925
            // z = 550 (1 - 0,4 x 0,2925) = 485,6 mm
            // As = 250e6 / (485,6 x 434,78) = 1 184 mm2
            BendingResult result = BendingDesign.Rectangular(250e6, WidthMm, EffectiveDepthMm,
                CompressionSteelDepthMm, Materials());

            Assert.InRange(result.Mu, 0.1650, 0.1656);
            Assert.InRange(result.NeutralAxisRatio, 0.291, 0.294);
            Assert.InRange(result.LeverArmMm, 484.0, 487.0);
            Assert.InRange(result.TensionSteelMm2, 1180.0, 1188.0);
            Assert.False(result.NeedsCompressionSteel);
        }

        [Fact]
        public void Cas_De_Reference_500_kNm_Section_Doublement_Armee()
        {
            // mu = 0,3306 > mu_lim = 0,2942 : des aciers comprimes sont necessaires.
            // M_lim = 0,2942 x 1 512,6e6 = 445,0 kN.m ; excedent = 55,0 kN.m
            // z_lim = 550 x 0,8208 = 451,4 mm
            // eps_sc = 0,0035 (246,4 - 50) / 246,4 = 0,00279 -> sigma_sc = f_yd = 434,78 MPa
            // A's = 55,0e6 / (500 x 434,78) = 253 mm2
            // As  = 445,0e6 / (451,4 x 434,78) + 253 = 2 267 + 253 = 2 520 mm2
            BendingResult result = BendingDesign.Rectangular(500e6, WidthMm, EffectiveDepthMm,
                CompressionSteelDepthMm, Materials());

            Assert.True(result.NeedsCompressionSteel);
            Assert.InRange(result.NeutralAxisRatio, 0.447, 0.449);
            Assert.InRange(result.CompressionSteelMm2, 248.0, 258.0);
            Assert.InRange(result.TensionSteelMm2, 2505.0, 2535.0);
        }

        [Fact]
        public void Un_Moment_Nul_Ne_Demande_Aucune_Armature()
        {
            BendingResult result = BendingDesign.Rectangular(0.0, WidthMm, EffectiveDepthMm,
                CompressionSteelDepthMm, Materials());
            Assert.Equal(0.0, result.TensionSteelMm2, 6);
        }

        [Fact]
        public void La_Section_En_T_Se_Ramene_A_Un_Rectangle_Quand_L_Axe_Neutre_Reste_Dans_La_Table()
        {
            // b_eff = 1 000, h_f = 120, M_Ed = 400 kN.m
            // mu = 400e6 / (1 000 x 550^2 x 16,667) = 0,0793 -> x/d = 0,1308
            // 0,8 x = 57,6 mm <= 120 mm : la table suffit.
            // z = 521,2 mm ; As = 400e6 / (521,2 x 434,78) = 1 765 mm2
            BendingResult result = BendingDesign.TSection(400e6, 1000.0, WidthMm, 120.0,
                EffectiveDepthMm, CompressionSteelDepthMm, Materials());

            Assert.True(result.NeutralAxisInFlange);
            Assert.InRange(result.TensionSteelMm2, 1755.0, 1775.0);
        }

        [Fact]
        public void La_Section_En_T_Se_Decompose_Quand_L_Axe_Neutre_Descend_Dans_L_Ame()
        {
            // b_eff = 1 000, h_f = 100, M_Ed = 900 kN.m
            // 0,8 x calcule sur la table = 140,7 mm > 100 mm -> decomposition.
            // Debords : F = 700 x 100 x 16,667 = 1 166 690 N, M = 583,3 kN.m, As = 2 683 mm2
            // Ame    : M = 316,7 kN.m -> z = 464,9 mm, As = 1 567 mm2
            // Total  : 4 250 mm2
            BendingResult result = BendingDesign.TSection(900e6, 1000.0, WidthMm, 100.0,
                EffectiveDepthMm, CompressionSteelDepthMm, Materials());

            Assert.False(result.NeutralAxisInFlange);
            Assert.InRange(result.TensionSteelMm2, 4210.0, 4290.0);
        }

        [Fact]
        public void As_Min_Conforme_A_L_Article_9_2_1_1()
        {
            // 0,26 x 2,565 / 500 x 300 x 550 = 220,1 mm2 ; 0,0013 x 165 000 = 214,5 mm2
            string justification;
            double asMin = BendingDesign.MinimumTensionSteel(WidthMm, EffectiveDepthMm,
                Materials(), out justification);

            Assert.InRange(asMin, 219.0, 221.5);
            Assert.Contains("9.2.1.1", justification);
        }

        [Fact]
        public void As_Min_Bascule_Sur_Le_Terme_Geometrique_Pour_Les_Betons_Faibles()
        {
            // C16/20 : f_ctm = 1,90 MPa -> 0,26 x 1,90 / 500 = 0,000988 < 0,0013
            string justification;
            double asMin = BendingDesign.MinimumTensionSteel(WidthMm, EffectiveDepthMm,
                Materials(16.0), out justification);

            Assert.Equal(0.0013 * WidthMm * EffectiveDepthMm, asMin, 3);
        }

        [Fact]
        public void As_Max_Vaut_Quatre_Pourcent_De_La_Section()
        {
            Assert.Equal(0.04 * 180000.0, BendingDesign.MaximumSteel(180000.0), 6);
        }

        [Theory]
        [InlineData(BeamSpanKind.SimplySupported, 8000.0, 8000.0)]
        [InlineData(BeamSpanKind.EndSpan, 8000.0, 6800.0)]
        [InlineData(BeamSpanKind.InteriorSpan, 8000.0, 5600.0)]
        [InlineData(BeamSpanKind.Cantilever, 2000.0, 3000.0)]
        public void La_Longueur_De_Moment_Nul_Suit_La_Figure_5_2(BeamSpanKind kind, double span,
                                                                 double expected)
        {
            Assert.Equal(expected, EffectiveFlangeWidth.ZeroMomentLengthMm(kind, span), 6);
        }

        [Fact]
        public void La_Largeur_Participante_Suit_L_Article_5_3_2_1()
        {
            // b = 2 000, b_w = 300, l0 = 3 400 mm -> debord = 850 mm
            // b_eff,i = min(0,2 x 850 + 0,1 x 3 400 ; 0,2 x 3 400 ; 850) = min(510 ; 680 ; 850) = 510
            // b_eff = 2 x 510 + 300 = 1 320 mm
            Assert.Equal(1320.0, EffectiveFlangeWidth.Compute(300.0, 2000.0, 3400.0), 6);
        }

        [Fact]
        public void La_Largeur_Participante_Ne_Depasse_Jamais_La_Largeur_Reelle()
        {
            // Grande portee : la formule donnerait plus que la table disponible.
            Assert.Equal(2000.0, EffectiveFlangeWidth.Compute(300.0, 2000.0, 20000.0), 6);
        }

        [Fact]
        public void Une_Section_Rectangulaire_Reste_Rectangulaire()
        {
            Assert.Equal(300.0, EffectiveFlangeWidth.Compute(300.0, 300.0, 5000.0), 6);
        }
    }
}
