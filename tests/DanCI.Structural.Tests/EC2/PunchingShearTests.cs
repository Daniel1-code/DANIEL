using System;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Poinconnement d'une semelle, EN 1992-1-1 article 6.4.
    /// Cas de reference : semelle 2 400 x 2 400 x 600, poteau 400 x 400, C25/30,
    /// V_Ed = 1 200 kN, contrainte nette 208,3 kPa, d = 544 mm, rho_l = 0,00139.
    /// </summary>
    public class PunchingShearTests
    {
        private const double ColumnLoadN = 1200000.0;
        private const double ColumnSizeMm = 400.0;
        private const double EffectiveDepthMm = 544.0;
        private const double NetPressureMPa = 0.208333;
        private const double ReinforcementRatio = 0.001386;
        private const double OverhangMm = 1000.0;
        private const double GammaC = 1.5;

        private static ConcreteProperties Materials()
        {
            return new ConcreteProperties(new ConcreteMaterial(25.0), new SteelMaterial(500.0),
                                          new RecommendedAnnex());
        }

        [Fact]
        public void Le_Perimetre_Suit_La_Figure_6_13()
        {
            // u = 2 (c1 + c2) + 2 pi a = 1 600 + 2 pi x 1 000 = 7 883 mm
            Assert.InRange(PunchingShear.Perimeter(400.0, 400.0, 1000.0), 7880.0, 7886.0);
            // Au nu du poteau, a = 0 : u = 1 600 mm
            Assert.Equal(1600.0, PunchingShear.Perimeter(400.0, 400.0, 0.0), 6);
        }

        [Fact]
        public void L_Aire_Delimitee_Est_Coherente()
        {
            // A = c1 c2 + 2 a (c1 + c2) + pi a^2
            //   = 160 000 + 1 600 000 + 3 141 593 = 4 901 593 mm2
            Assert.InRange(PunchingShear.EnclosedArea(400.0, 400.0, 1000.0),
                           4899000.0, 4904000.0);
        }

        [Fact]
        public void La_Resistance_De_Base_Retient_v_min_Pour_Une_Semelle_Peu_Armee()
        {
            // k = 1 + sqrt(200/544) = 1,606
            // C_Rd,c k (100 rho fck)^(1/3) = 0,12 x 1,606 x 1,513 = 0,292 MPa
            // v_min = 0,035 x 1,606^1,5 x 5 = 0,356 MPa -> gouverne
            double resistance = PunchingShear.BaseResistance(EffectiveDepthMm, ReinforcementRatio,
                                                             Materials(), GammaC);
            Assert.InRange(resistance, 0.352, 0.361);
        }

        [Fact]
        public void Cas_De_Reference_La_Semelle_Resiste()
        {
            PunchingResult result = PunchingShear.Check(ColumnLoadN, ColumnSizeMm, ColumnSizeMm,
                EffectiveDepthMm, NetPressureMPa, ReinforcementRatio, Materials(), GammaC,
                OverhangMm);

            // Au nu : v_Ed = 1 200 000 / (1 600 x 544) = 1,379 MPa
            //         v_Rd,max = 0,5 x 0,54 x 16,667 = 4,50 MPa
            Assert.InRange(result.ColumnFaceStressMPa, 1.37, 1.39);
            Assert.InRange(result.MaxStressMPa, 4.45, 4.55);
            Assert.True(result.ColumnFacePasses);

            // Le perimetre critique se situe autour de 0,7 a 0,8 d, avec un taux voisin de 0,43.
            Assert.NotNull(result.Critical);
            Assert.InRange(result.Critical.Utilization, 0.38, 0.47);
            Assert.True(result.Passes);
        }

        [Fact]
        public void Le_Perimetre_Critique_N_Est_Pas_Celui_A_2d()
        {
            // C'est tout l'objet de l'article 6.4.4(2) : la contrainte decroit avec la
            // distance, mais la resistance est majoree de 2d/a. Le pire n'est donc jamais
            // au perimetre le plus eloigne.
            PunchingResult result = PunchingShear.Check(ColumnLoadN, ColumnSizeMm, ColumnSizeMm,
                EffectiveDepthMm, NetPressureMPa, ReinforcementRatio, Materials(), GammaC,
                OverhangMm);

            PunchingPerimeterCheck farthest = result.Perimeters[result.Perimeters.Count - 1];
            Assert.True(result.Critical.Utilization > farthest.Utilization,
                "Le perimetre le plus eloigne ne doit pas etre le plus defavorable.");
            Assert.True(result.Critical.DistanceMm < 2.0 * EffectiveDepthMm);
        }

        [Fact]
        public void L_Effort_Est_Reduit_Par_La_Reaction_Du_Sol()
        {
            PunchingResult result = PunchingShear.Check(ColumnLoadN, ColumnSizeMm, ColumnSizeMm,
                EffectiveDepthMm, NetPressureMPa, ReinforcementRatio, Materials(), GammaC,
                OverhangMm);

            foreach (PunchingPerimeterCheck check in result.Perimeters)
            {
                Assert.True(check.NetShearN < ColumnLoadN,
                    "La reaction du sol a l'interieur du perimetre doit reduire l'effort.");
            }
        }

        [Fact]
        public void Une_Charge_Excessive_Ecrase_Le_Beton_Au_Nu_Du_Poteau()
        {
            // v_Rd,max = 4,50 MPa sur u0 d = 1 600 x 544 = 870 400 mm2 -> environ 3 917 kN.
            PunchingResult result = PunchingShear.Check(5000000.0, ColumnSizeMm, ColumnSizeMm,
                EffectiveDepthMm, NetPressureMPa, ReinforcementRatio, Materials(), GammaC,
                OverhangMm);

            Assert.False(result.ColumnFacePasses);
            Assert.False(result.Passes);
            Assert.Contains("DEPASSE", result.Justification);
        }

        [Fact]
        public void Une_Semelle_Trop_Mince_Ne_Passe_Pas_Le_Poinconnement()
        {
            // Meme charge, hauteur utile divisee par trois.
            PunchingResult result = PunchingShear.Check(ColumnLoadN, ColumnSizeMm, ColumnSizeMm,
                180.0, NetPressureMPa, ReinforcementRatio, Materials(), GammaC, OverhangMm);

            Assert.True(result.Critical.Utilization > 1.0,
                "Taux obtenu : " + result.Critical.Utilization);
        }

        [Fact]
        public void Le_Balayage_Est_Borne_Par_Le_Debord_De_La_Semelle()
        {
            // Debord de 300 mm : aucun perimetre au-dela ne doit etre examine.
            PunchingResult result = PunchingShear.Check(ColumnLoadN, ColumnSizeMm, ColumnSizeMm,
                EffectiveDepthMm, NetPressureMPa, ReinforcementRatio, Materials(), GammaC, 300.0);

            foreach (PunchingPerimeterCheck check in result.Perimeters)
            {
                Assert.True(check.DistanceMm <= 300.0 + 1e-6);
            }
        }
    }
}
