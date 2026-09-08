using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// WALL-01 : voile porteur 200 mm, longueur 4,00 m, hauteur libre 3,00 m.
    /// Fiche de validation : docs/validation/WALL-01.md
    ///
    /// C25/30, B500, XC1, maintenu en tete et en pied, N_Ed = 400 kN/m centre,
    /// phi_ef = 2,0.
    ///
    /// Enrobage : XC1, geometrie de dalle -> S3, c_min,dur = 10 mm, c_min,b = 10 mm
    ///            c_nom = 20 mm ; d = 200 - 20 - 5 = 175 mm
    /// Flambement : beta = 1,00 -> l_0 = 3 000 mm ; i = 200/sqrt(12) = 57,7 mm
    ///            lambda = 3 000 / 57,7 = 52,0
    /// Limite   : n = 400 000 / (200 000 x 16,667) = 0,120 ; omega = 0,052
    ///            lambda_lim = 20 x 0,714 x 1,051 x 0,7 / sqrt(0,120) = 30,3
    ///            52,0 > 30,3 -> le second ordre est obligatoire
    /// 2e ordre : Kr = 1,00 (plafonne), Kphi = 1 + (0,35 + 0,125 - 0,347) x 2,0 = 1,257
    ///            1/r = 1,257 x 0,0021739 / (0,45 x 175) = 3,47e-5 1/mm
    ///            e_2 = 3,47e-5 x 3 000^2 / 10 = 31,2 mm -> M_2 = 12,5 kN.m/m
    /// 1er ordre: e_0 = max(t/30 ; 20) = 20 mm -> M_1 = 8,0 kN.m/m
    ///            M_Ed = 8,0 + 12,5 = 20,5 kN.m/m
    /// Section  : la section minimale de l'art. 9.6.2 (0,002 Ac = 400 mm2/m) suffit
    ///            largement : M_Rd d'une bande 1 000 x 200 armee de 2 x 200 mm2 vaut
    ///            environ 48 kN.m/m sous 400 kN.
    /// Nappes   : HA8 e = 250 par nappe (201 mm2/m), soit 402 mm2/m au total
    ///            Horizontal : max(0,25 x 402 ; 0,001 x 200 000) = 200 mm2/m
    ///                         -> HA8 e = 300 par nappe (168 mm2/m), 335 mm2/m au total
    /// Epingles : As,v = 402 mm2/m est tres loin de 0,02 Ac = 4 000 mm2/m : aucune.
    /// </summary>
    public class Wall01DesignTests
    {
        private static WallData Wall()
        {
            return new WallData
            {
                Id = "WALL-01",
                Name = "V1",
                Mark = "V1",
                ThicknessMm = 200.0,
                LengthMm = 4000.0,
                ClearHeightMm = 3000.0
            };
        }

        private static WallDesignSettings Settings()
        {
            return new WallDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                Restraint = WallRestraint.TopAndBottom,
                CreepCoefficient = 2.0,
                AxialLoadKnPerM = 400.0,
                OutOfPlaneMomentKnmPerM = 0.0,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoVerticalDiameter = true,
                AutoHorizontalDiameter = true
            };
        }

        private static WallDesignResult Design()
        {
            return new WallDesignModule().Design(Wall(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            WallDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
            Assert.Equal("OK", result.Status);
        }

        [Fact]
        public void ItIsAWallByCode()
        {
            // 4 000 / 200 = 20 >= 4 : c'est bien un voile au sens de l'article 9.6.1.
            Assert.True(Wall().IsWallByCode);
            Assert.Empty(Design().Warnings);
        }

        [Fact]
        public void Cover_UsesTheSlabReduction()
        {
            WallDesignResult result = Design();

            // XC1 + geometrie en nappe -> S3 -> c_min,dur = 10 mm ; c_nom = 20 mm
            Assert.Equal(20.0, result.Reinforcement.CoverMm, 1);
        }

        [Fact]
        public void BucklingLength_IsTheClearHeight()
        {
            WallDesignResult result = Design();

            Assert.Equal(1.0, result.Buckling.Beta, 3);
            Assert.Equal(3000.0, result.Buckling.BucklingLengthMm, 1);
            // lambda = 3 000 / (200/sqrt(12)) = 51,96
            Assert.InRange(result.SlendernessRatio, 51.0, 53.0);
        }

        [Fact]
        public void SecondOrder_IsRequiredAndMatchesHandCalculation()
        {
            WallDesignResult result = Design();

            Assert.NotNull(result.SecondOrder);
            Assert.True(result.SecondOrder.Required);
            // lambda_lim = 30,3
            Assert.InRange(result.SecondOrder.SlendernessLimit, 29.0, 32.0);
            // e_2 = 31,2 mm
            Assert.InRange(result.SecondOrder.SecondOrderEccentricityMm, 28.0, 34.0);
        }

        [Fact]
        public void MinimumEccentricity_DrivesTheFirstOrderMoment()
        {
            WallDesignResult result = Design();

            // Aucun moment declare, mais e_0 = 20 mm impose M_1 = 8,0 kN.m/m.
            // Avec M_2 = 12,5, le moment de calcul vaut environ 20,5 kN.m/m.
            Assert.InRange(result.DesignOutOfPlaneMomentKnmPerM, 18.0, 23.0);
            Assert.Contains(result.Notes, n => n.Contains("6.1(4)"));
        }

        [Fact]
        public void MinimumSteel_Governs()
        {
            WallDesignResult result = Design();

            // 0,002 x 200 000 = 400 mm2/m ; la resistance n'exige pas davantage.
            Assert.Equal(400.0, result.VerticalSteelRequiredMm2PerM, 1);
            Assert.True(result.Reinforcement.VerticalTotalMm2PerM >= 400.0);
        }

        [Fact]
        public void OutOfPlaneCapacity_HasComfortableMargin()
        {
            CheckResult capacity = Find(Design(), "Flexion composee hors plan");

            // M_Ed = 20,5 kN.m/m contre M_Rd de l'ordre de 48 kN.m/m.
            Assert.InRange(capacity.Utilization, 0.25, 0.60);
            Assert.Equal(CheckStatus.Pass, capacity.Status);
        }

        [Fact]
        public void HorizontalSteel_FollowsWhatIsPlacedVertically()
        {
            WallDesignResult result = Design();
            CheckResult horizontal = Find(result, "Section horizontale minimale");

            // max(0,25 x 402 ; 0,001 x 200 000) = 200 mm2/m -> c'est 0,001 Ac qui gouverne
            Assert.InRange(horizontal.Demand.Value, 195.0, 210.0);
            Assert.True(result.Reinforcement.HorizontalTotalMm2PerM >= horizontal.Demand.Value);
        }

        [Fact]
        public void NoTransverseLinks_OnALightlyReinforcedWall()
        {
            WallDesignResult result = Design();

            // As,v = 402 mm2/m, tres loin de 0,02 Ac = 4 000 mm2/m.
            Assert.False(result.Reinforcement.HasLinks);
        }

        [Fact]
        public void Plan_HoldsFourMeshesAndNothingElse()
        {
            WallDesignResult result = Design();

            // Deux nappes verticales + deux nappes horizontales, sans epingle ni rive.
            Assert.Equal(4, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
        }

        [Fact]
        public void EveryCheck_CarriesItsClause()
        {
            WallDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
            });
        }

        private static CheckResult Find(WallDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
