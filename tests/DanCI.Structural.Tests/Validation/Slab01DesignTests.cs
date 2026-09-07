using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Eurocodes.EC0;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// SLAB-01 : dalle pleine isostatique de 220 mm, portee 5,00 m, bande de 1 m.
    /// Fiche de validation : docs/validation/SLAB-01.md
    ///
    /// C25/30, B500, XC1, habitation. g additionnel 2,0 kN/m2, q 2,5 kN/m2,
    /// poids propre inclus.
    ///
    /// Charges  : pp = 0,22 x 25 = 5,50 -> g = 7,50 kN/m2 ; q = 2,50 kN/m2
    ///            ELU  = 1,35 x 7,50 + 1,50 x 2,50 = 13,875 kN/m2
    ///            ELS quasi-permanente = 7,50 + 0,3 x 2,50 = 8,25 kN/m2
    /// Efforts  : M = w l2/8 = 13,875 x 25 / 8 = 43,36 kN.m/m ; V = 34,69 kN/m
    /// Enrobage : XC1, geometrie de dalle -> classe S3, c_min,dur = 10 mm,
    ///            c_min,b = 12 mm -> c_nom = 22 mm ; d = 220 - 22 - 6 = 192 mm
    /// Flexion  : mu = 0,0706 -> z = 183 mm -> As = 545 mm2/m
    ///            (As,min = 256 mm2/m ne gouverne pas)
    ///            HA12 e = 200 -> 565 mm2/m
    /// Tranchant: V_Rd,c = v_min b d = 0,495 x 1 000 x 192 = 95,0 kN/m  -> taux 0,37
    /// Fleche   : rho = 0,00284 < rho_0 = 0,005 -> eq. 7.16a
    ///            l/d admissible = 34,9 x 1,04 = 36,2 contre l/d = 26,0  -> taux 0,72
    /// Fissures : sigma_s = 435 x 0,595 x 0,963 = 249 MPa, w = 0,4 mm
    ///            -> phi_max 19 mm et s_max 239 mm : HA12 e = 200 satisfait les deux
    /// </summary>
    public class Slab01DesignTests
    {
        private static SlabData Slab()
        {
            return new SlabData
            {
                Id = "SLAB-01",
                Name = "D1",
                Mark = "D1",
                ThicknessMm = 220.0,
                SpanMm = 5000.0,
                WidthMm = 12000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };
        }

        private static SlabDesignSettings Settings()
        {
            return new SlabDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                MomentSource = SlabMomentSource.FromLoads,
                PermanentLoadKnM2 = 2.0,
                VariableLoadKnM2 = 2.5,
                IncludeSelfWeight = true,
                ConcreteUnitWeightKnM3 = 25.0,
                Category = UseCategory.Residential,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true
            };
        }

        private static SlabDesignResult Design()
        {
            return new SlabDesignModule().Design(Slab(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            SlabDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
            Assert.Equal("OK", result.Status);
        }

        [Fact]
        public void Loads_FollowEn1990()
        {
            SlabDesignResult result = Design();

            // 1,35 x 7,50 + 1,50 x 2,50 = 13,875 kN/m2
            Assert.Equal(13.875, result.UltimateLoadKnM2, 3);
            // 7,50 + 0,3 x 2,50 = 8,25 kN/m2
            Assert.Equal(8.25, result.QuasiPermanentLoadKnM2, 3);
        }

        [Fact]
        public void SimplySupportedMoment_ComesFromStaticsAlone()
        {
            SlabDesignResult result = Design();

            // w l2 / 8 = 13,875 x 25 / 8 = 43,359 kN.m/m
            Assert.Equal(43.359, result.SpanMomentKnmPerM, 2);
            // w l / 2 = 34,6875 kN/m
            Assert.Equal(34.688, result.ShearKnPerM, 2);
            Assert.Equal(0.0, result.SupportMomentKnmPerM, 6);

            Assert.Contains(result.Notes, n => n.Contains("statique seule"));
        }

        [Fact]
        public void Cover_UsesTheSlabReduction()
        {
            SlabDesignResult result = Design();

            // XC1 + geometrie de dalle -> S3 -> c_min,dur = 10 mm ; c_min,b = phi = 12 mm
            //   c_nom = 12 + 10 = 22 mm
            Assert.Equal(22.0, result.Reinforcement.CoverMm, 1);
            Assert.Equal(192.0, result.Reinforcement.EffectiveDepthMm, 1);
        }

        [Fact]
        public void SpanSteel_MatchesHandCalculation()
        {
            SlabDesignResult result = Design();

            // As = 545 mm2/m, As,min = 256 mm2/m ne gouverne pas.
            Assert.InRange(result.SpanSteelRequiredMm2PerM, 535.0, 555.0);
            Assert.True(result.Reinforcement.BottomMain.AreaPerMetreMm2
                        >= result.SpanSteelRequiredMm2PerM);
            Assert.InRange(result.Reinforcement.BottomMain.AreaPerMetreMm2
                           / result.SpanSteelRequiredMm2PerM, 1.0, 1.10);
        }

        [Fact]
        public void TransverseSteel_IsAtLeastTwentyPercent()
        {
            SlabDesignResult result = Design();

            Assert.True(result.Reinforcement.BottomTransverse.AreaPerMetreMm2
                        >= 0.2 * result.Reinforcement.BottomMain.AreaPerMetreMm2);
            Assert.Equal(CheckStatus.Pass, Find(result, "Armature de repartition").Status);
        }

        [Fact]
        public void Spacings_FollowClause9311()
        {
            SlabDesignResult result = Design();

            // s_max principal = min(3 x 220 ; 400) = 400 mm
            CheckResult main = Find(result, "Espacement des armatures principales");
            Assert.Equal(400.0, main.Resistance.Value, 1);
            Assert.Equal(CheckStatus.Pass, main.Status);

            // s_max repartition = min(3,5 x 220 ; 450) = 450 mm
            CheckResult transverse = Find(result, "Espacement des armatures de repartition");
            Assert.Equal(450.0, transverse.Resistance.Value, 1);
            Assert.Equal(CheckStatus.Pass, transverse.Status);
        }

        [Fact]
        public void Shear_MatchesHandCalculation()
        {
            CheckResult shear = Find(Design(), "Effort tranchant");

            // V_Ed = 34,7 kN/m contre V_Rd,c = 95,0 kN/m (v_min gouverne)
            Assert.InRange(shear.Demand.Value, 33.5, 36.0);
            Assert.InRange(shear.Resistance.Value, 91.0, 99.0);
            Assert.Equal(CheckStatus.Pass, shear.Status);
        }

        [Fact]
        public void Deflection_UsesEquation716a_AndPasses()
        {
            SlabDesignResult result = Design();

            Assert.NotNull(result.Deflection);
            Assert.Equal(1.0, result.Deflection.SystemFactor, 3);
            // l/d de base 34,9 ; l/d reel 5 000 / 192 = 26,0
            Assert.InRange(result.Deflection.BasicRatio, 33.5, 36.5);
            Assert.InRange(result.Deflection.ActualRatio, 25.5, 26.5);
            Assert.True(result.Deflection.Passes);
        }

        [Fact]
        public void Cracking_SatisfiesBothCriteria()
        {
            SlabDesignResult result = Design();

            Assert.NotNull(result.Cracking);
            // XC1 -> w_max = 0,4 mm
            Assert.Equal(0.4, result.Cracking.CrackWidthLimitMm, 3);
            // sigma_s estimee vers 249 MPa
            Assert.InRange(result.Cracking.SteelStressMPa, 235.0, 265.0);
            Assert.True(result.Cracking.DiameterSatisfied);
            Assert.True(result.Cracking.SpacingSatisfied);
        }

        [Fact]
        public void Plan_HoldsTheTwoBottomLayersOnly()
        {
            SlabDesignResult result = Design();

            // Dalle isostatique sans chapeaux demandes : nappe porteuse + repartition.
            Assert.Equal(2, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
        }

        [Fact]
        public void EveryCheck_CarriesItsClause()
        {
            SlabDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
                Assert.False(string.IsNullOrWhiteSpace(c.GoverningCombination));
            });
        }

        private static CheckResult Find(SlabDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
