using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// COLUMN-03 : elancement et second ordre d'un poteau 300 x 300, hauteur 6,00 m.
    /// Fiche de validation : docs/validation/COLUMN-03.md
    ///
    /// Donnees : C25/30, B500, 8 HA16, NEd = 900 kN, l0 = 1,0 x 6 000 mm, phi_ef = 2,0.
    ///   f_cd = 16,667 MPa   f_yd = 434,78 MPa   eps_yd = 0,0021739
    ///   A_c = 90 000 mm2    A_s = 1 608,5 mm2
    ///   n = 900 000 / (90 000 x 16,667) = 0,600
    ///   omega = 1 608,5 x 434,78 / (90 000 x 16,667) = 0,4662
    /// </summary>
    public class Column03SecondOrderTests
    {
        private const double AxialForceN = 900000.0;
        private const double BucklingLengthMm = 6000.0;
        private const double SectionHeightMm = 300.0;
        private const double EffectiveDepthMm = 254.0;    // 300 - 30 - 8 - 8
        private const double RelativeAxial = 0.6;
        private const double MechanicalRatio = 0.466231;
        private const double SteelYieldStrain = 434.7826 / 200000.0;
        private const double CreepCoefficient = 2.0;

        [Fact]
        public void L_Elancement_Vaut_69_3()
        {
            // i = h / sqrt(12) = 300 / 3,4641 = 86,60 mm ; lambda = 6 000 / 86,60 = 69,28
            double slenderness = SecondOrder.Slenderness(BucklingLengthMm, SectionHeightMm);
            Assert.InRange(slenderness, 69.2, 69.4);
        }

        [Fact]
        public void L_Elancement_Limite_Vaut_17_9()
        {
            // A = 1 / (1 + 0,2 x 2,0)      = 0,7143
            // B = sqrt(1 + 2 x 0,4662)     = 1,3901
            // C = 0,7  (rapport des moments d'extremite inconnu)
            // lambda_lim = 20 x 0,7143 x 1,3901 x 0,7 / sqrt(0,600) = 17,95
            double limit = SecondOrder.SlendernessLimit(RelativeAxial, MechanicalRatio,
                                                        CreepCoefficient);
            Assert.InRange(limit, 17.8, 18.1);
        }

        [Fact]
        public void Le_Second_Ordre_Est_Requis()
        {
            double slenderness = SecondOrder.Slenderness(BucklingLengthMm, SectionHeightMm);
            double limit = SecondOrder.SlendernessLimit(RelativeAxial, MechanicalRatio,
                                                        CreepCoefficient);
            Assert.True(slenderness > limit,
                "lambda = " + slenderness + " doit depasser lambda_lim = " + limit);
        }

        [Fact]
        public void L_Excentricite_Du_Second_Ordre_Vaut_57_mm()
        {
            // 1/r0  = eps_yd / (0,45 d) = 0,0021739 / (0,45 x 254) = 1,9019e-5 /mm
            // Kr    = (1,4662 - 0,600) / (1,4662 - 0,400) = 0,8124
            // beta  = 0,35 + 25/200 - 69,28/150 = 0,01313
            // Kphi  = 1 + 0,01313 x 2,0 = 1,0263
            // 1/r   = 0,8124 x 1,0263 x 1,9019e-5 = 1,5857e-5 /mm
            // e2    = (1/r) l0^2 / 10 = 1,5857e-5 x 3,6e6 = 57,08 mm
            double slenderness = SecondOrder.Slenderness(BucklingLengthMm, SectionHeightMm);
            SecondOrderResult result = SecondOrder.NominalCurvature(
                AxialForceN, BucklingLengthMm, EffectiveDepthMm, slenderness,
                RelativeAxial, MechanicalRatio, SteelYieldStrain, 25.0, CreepCoefficient);

            Assert.InRange(result.SecondOrderEccentricityMm, 56.5, 57.7);
        }

        [Fact]
        public void Le_Moment_Du_Second_Ordre_Vaut_51_4_kNm()
        {
            // M2 = NEd x e2 = 900 kN x 0,05708 m = 51,4 kN.m
            double slenderness = SecondOrder.Slenderness(BucklingLengthMm, SectionHeightMm);
            SecondOrderResult result = SecondOrder.NominalCurvature(
                AxialForceN, BucklingLengthMm, EffectiveDepthMm, slenderness,
                RelativeAxial, MechanicalRatio, SteelYieldStrain, 25.0, CreepCoefficient);

            Assert.InRange(UnitConverter.NmmToKnm(result.SecondOrderMomentNmm), 50.8, 52.0);
        }

        [Fact]
        public void L_Excentricite_Minimale_Suit_L_Article_6_1()
        {
            // e0 = max(h/30 ; 20 mm)
            Assert.Equal(20.0, SecondOrder.MinimumEccentricityMm(300.0), 6);   // 300/30 = 10 < 20
            Assert.Equal(30.0, SecondOrder.MinimumEccentricityMm(900.0), 6);   // 900/30 = 30 > 20
        }

        [Fact]
        public void Le_Fluage_Aggrave_Le_Second_Ordre()
        {
            double slenderness = SecondOrder.Slenderness(BucklingLengthMm, SectionHeightMm);
            double without = SecondOrder.NominalCurvature(AxialForceN, BucklingLengthMm,
                EffectiveDepthMm, slenderness, RelativeAxial, MechanicalRatio, SteelYieldStrain,
                25.0, 0.0).SecondOrderEccentricityMm;
            double with = SecondOrder.NominalCurvature(AxialForceN, BucklingLengthMm,
                EffectiveDepthMm, slenderness, RelativeAxial, MechanicalRatio, SteelYieldStrain,
                25.0, 2.0).SecondOrderEccentricityMm;

            Assert.True(with > without, "phi_ef = 2 doit accroitre e2 par rapport a phi_ef = 0.");
        }
    }
}
