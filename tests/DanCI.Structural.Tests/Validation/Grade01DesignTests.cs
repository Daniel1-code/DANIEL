using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Eurocodes.EC8;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// GRADE-01 : longrine 300 x 500, portee 5,00 m entre semelles, en zone sismique.
    /// Fiche de validation : docs/validation/GRADE-01.md
    ///
    /// C25/30, B500, XC2 sur beton de proprete, suspendue entre appuis.
    /// Charge de mur 40 kN/m (ELU), sol de classe C, alpha = 0,15, S = 1,15,
    /// N_Ed moyen des poteaux relies 800 kN, batiment de 3 niveaux.
    ///
    /// Charges  : pp = 0,30 x 0,50 x 25 = 3,75 kN/m
    ///            w = 40 + 1,35 x 3,75 = 45,06 kN/m
    /// Efforts  : M = w l2/8 = 45,06 x 25 / 8 = 140,8 kN.m ; V = 112,7 kN
    /// Enrobage : XC2 -> 35 mm releve a 40 mm (art. 4.4.1.3(4))
    ///            d = 500 - 40 - 8 - 20/2 = 442 mm
    /// Flexion  : mu = 0,1442 -> z = 397,7 mm -> As = 814 mm2
    /// Liaison  : N = 0,4 x 0,15 x 1,15 x 800 = 55,2 kN
    ///            As,N = 55 200 / 434,78 = 127 mm2, soit 63 mm2 par nappe
    /// Minimum  : EN 1998-1 5.8.2(5) : 0,4 % x 150 000 = 600 mm2 par nappe
    ///            (l'EC2 n'en demanderait que 0,26 f_ctm/f_yk b d = 177 mm2)
    /// Requis   : nappe inferieure max(814 + 63 ; 600) = 878 mm2
    ///            nappe superieure max(63 ; 600) = 600 mm2
    /// Tranchant: V_Rd,c = 69,4 kN < 112,7 kN -> cadres necessaires
    ///            A_sw/s = 112 700 / (397,8 x 434,78 x 2,5) = 261 mm2/m
    ///            HA8 a 2 brins -> e = 386 mm, plafonne par 0,75 d = 332 mm
    /// </summary>
    public class Grade01DesignTests
    {
        private static GradeBeamData Beam()
        {
            return new GradeBeamData
            {
                Id = "GRADE-01",
                Name = "LG1",
                Mark = "LG1",
                WidthMm = 300.0,
                HeightMm = 500.0,
                SpanMm = 5000.0,
                SpanKind = GradeBeamSpanKind.SimplySupported
            };
        }

        private static GradeBeamDesignSettings Settings()
        {
            return new GradeBeamDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                Bedding = GradeBeamBedding.Suspended,
                WallLoadKnPerM = 40.0,
                IncludeSelfWeight = true,
                SeismicDesign = true,
                Ground = GroundType.C,
                GroundAccelerationRatio = 0.15,
                SoilFactor = 1.15,
                MeanColumnAxialLoadKn = 800.0,
                StoreyCount = 3,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                CastDirectlyAgainstSoil = false,
                AutoLongitudinalDiameter = true,
                AutoStirrupDiameter = true,
                StirrupLegs = 2
            };
        }

        private static GradeBeamDesignResult Design()
        {
            return new GradeBeamDesignModule().Design(Beam(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            GradeBeamDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
            Assert.Equal("OK", result.Status);
        }

        [Fact]
        public void Load_IncludesFactoredSelfWeight()
        {
            GradeBeamDesignResult result = Design();

            // 40 + 1,35 x 3,75 = 45,0625 kN/m
            Assert.Equal(45.0625, result.DesignLoadKnPerM, 3);
        }

        [Fact]
        public void MomentAndShear_ComeFromStaticsAlone()
        {
            GradeBeamDesignResult result = Design();

            // w l2/8 = 140,82 kN.m ; w l/2 = 112,66 kN
            Assert.Equal(140.82, result.SpanMomentKnm, 1);
            Assert.Equal(112.66, result.ShearKn, 1);
            Assert.Contains(result.Notes, n => n.Contains("statique seule"));
        }

        [Fact]
        public void Cover_UsesTheFoundationFloor()
        {
            // Une longrine est un element de fondation : art. 4.4.1.3(4).
            Assert.Equal(40.0, Design().Reinforcement.CoverMm, 1);
        }

        [Fact]
        public void TieForce_MatchesEurocode8()
        {
            GradeBeamDesignResult result = Design();

            // 0,4 x 0,15 x 1,15 x 800 = 55,2 kN
            Assert.Equal(55.2, result.TieForceKn, 1);
            Assert.NotNull(result.Tie);
            Assert.True(result.Tie.TieRequired);
            Assert.Contains(result.Notes, n => n.Contains("alterne"));
        }

        [Fact]
        public void SeismicMinimum_GovernsTheTopFace()
        {
            GradeBeamDesignResult result = Design();

            // 0,4 % de 150 000 = 600 mm2, contre 63 mm2 dus a la traction seule.
            Assert.Equal(600.0, result.TopSteelRequiredMm2, 1);
            Assert.Contains(result.Notes, n => n.Contains("EN HAUT ET EN BAS"));
        }

        [Fact]
        public void BottomSteel_AddsBendingAndTension()
        {
            GradeBeamDesignResult result = Design();

            // 814 (flexion) + 63 (traction) = 878 mm2
            Assert.InRange(result.BottomSteelRequiredMm2, 855.0, 900.0);
            Assert.True(result.Reinforcement.BottomBars.AreaMm2
                        >= result.BottomSteelRequiredMm2);
        }

        [Fact]
        public void BothFaces_RunContinuous()
        {
            GradeBeamDesignResult result = Design();

            // Les deux nappes existent et filent : ce n'est pas un montage constructif.
            Assert.True(result.Reinforcement.TopBars.Count >= 2);
            Assert.True(result.Reinforcement.BottomBars.Count >= 2);
            Assert.True(result.Reinforcement.TopBars.AreaMm2 >= 600.0);
        }

        [Fact]
        public void Shear_NeedsStirrups()
        {
            CheckResult shear = Find(Design(), "Effort tranchant");

            // A_sw/s = 0,2605 mm2/mm, soit 261 mm2/m
            Assert.Equal("mm2/m", shear.Demand.Unit);
            Assert.InRange(shear.Demand.Value, 235.0, 290.0);
            Assert.Equal(CheckStatus.Pass, shear.Status);
        }

        [Fact]
        public void StirrupSpacing_IsCappedByThreeQuartersD()
        {
            GradeBeamDesignResult result = Design();
            CheckResult spacing = Find(result, "Espacement des cadres");

            // 0,75 x 442 = 332 mm
            Assert.InRange(spacing.Resistance.Value, 325.0, 340.0);
            Assert.True(result.Reinforcement.StirrupSpacingMm <= spacing.Resistance.Value);
            Assert.Contains(result.Notes, n => n.Contains("espacement constant"));
        }

        [Fact]
        public void CompressionCheck_IsProducedBecauseTheTieAlternates()
        {
            CheckResult compression = Find(Design(), "Effort de liaison en compression");

            // 55,2 kN contre A_c f_cd + A_s f_yd, tres largement suffisant.
            Assert.InRange(compression.Demand.Value, 54.0, 57.0);
            Assert.True(compression.Utilization < 0.10);
            Assert.Contains("flambement ne la concerne pas", compression.Comment);
        }

        [Fact]
        public void Plan_HoldsBothFacesAndTheStirrups()
        {
            GradeBeamDesignResult result = Design();

            // nappe inferieure + nappe superieure + cadres
            Assert.Equal(3, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
            Assert.Contains(result.Plan.Groups, g => g.Kind == RebarKind.Stirrup);
        }

        [Fact]
        public void EveryCheck_CarriesItsClause()
        {
            GradeBeamDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
            });
        }

        private static CheckResult Find(GradeBeamDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
