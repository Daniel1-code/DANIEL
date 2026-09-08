using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STRIP-01 : semelle filante 900 x 400 sous voile de 200 mm, longueur 10,00 m.
    /// Fiche de validation : docs/validation/STRIP-01.md
    ///
    /// C25/30, B500, XC2 sur beton de proprete, sol a 200 kPa, N = 150 kN/m centre.
    ///
    /// Poids propre : 0,90 x 0,40 x 25 = 9,0 kN/m -> N total = 159,0 kN/m
    /// Contraintes  : sigma_geo = 159,0 / 0,90 = 176,7 kPa <= 200 kPa (taux 0,88)
    ///                sigma_net = 150,0 / 0,90 = 166,7 kPa
    /// Enrobage     : XC2 -> c_nom = 35 mm, releve a 40 mm par l'art. 4.4.1.3(4)
    ///                d = 400 - 40 - 7 = 353 mm (HA14)
    /// Flexion      : debord a = (900 - 200)/2 = 350 mm
    ///                M = 1 000 x 350^2 x 0,16667 / 2 = 10,21 kN.m/m
    ///                As de flexion = 67 mm2/m
    ///                As,min = 0,26 x 2,565/500 x 1 000 x 353 = 471 mm2/m
    ///                -> le minimum gouverne d'un facteur SEPT
    /// Nappes       : HA14 e = 300 (513 mm2/m) ; repartition HA8 e = 300 (168 mm2/m)
    /// Tranchant    : a - d = 350 - 353 = -3 mm -> la section tombe hors de la semelle
    /// Poinconnement: sans objet, la charge du voile arrive repartie
    /// Ancrage      : l_bd(HA14) = 565 mm contre 350 - 40 = 310 mm disponibles
    ///                -> CROCHET D'EXTREMITE indispensable
    /// </summary>
    public class Strip01DesignTests
    {
        private static StripFootingData Footing()
        {
            return new StripFootingData
            {
                Id = "STRIP-01",
                Name = "SF1",
                Mark = "SF1",
                WidthMm = 900.0,
                ThicknessMm = 400.0,
                LengthMm = 10000.0,
                WallThicknessMm = 200.0
            };
        }

        private static StripFootingDesignSettings Settings()
        {
            return new StripFootingDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                AllowableBearingPressureKpa = 200.0,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                CastDirectlyAgainstSoil = false,
                AxialLoadKnPerM = 150.0,
                IncludeSelfWeight = true,
                AutoMeshDiameter = true,
                Starters = true,
                StarterSpacingMm = 250.0,
                StarterDiameterMm = 10.0
            };
        }

        private static StripFootingDesignResult Design()
        {
            return new StripFootingDesignModule().Design(Footing(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            StripFootingDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
        }

        [Fact]
        public void SoilPressure_MatchesHandCalculation()
        {
            StripFootingDesignResult result = Design();

            // 159,0 / 0,90 = 176,7 kPa ; 150,0 / 0,90 = 166,7 kPa
            Assert.True(result.Pressure.WithinCore);
            Assert.InRange(result.Pressure.EffectivePressureKpa, 174.0, 180.0);
            Assert.InRange(result.Pressure.NetPressureKpa, 164.0, 170.0);
        }

        [Fact]
        public void Bearing_UtilizationMatchesHandCalculation()
        {
            CheckResult bearing = Find(Design(), "Capacite portante");

            // 176,7 / 200 = 0,883
            Assert.InRange(bearing.Utilization, 0.86, 0.91);
            Assert.Equal(CheckStatus.Pass, bearing.Status);
        }

        [Fact]
        public void Cover_IsRaisedToFortyMillimetres()
        {
            StripFootingDesignResult result = Design();
            Assert.Equal(40.0, result.Reinforcement.CoverMm, 1);
        }

        [Fact]
        public void MinimumSteel_GovernsBySevenFold()
        {
            StripFootingDesignResult result = Design();

            // M = 10,21 kN.m/m -> As de flexion 67 mm2/m, As,min 471 mm2/m.
            Assert.InRange(result.CantileverMomentKnmPerM, 9.8, 10.7);
            Assert.InRange(result.TransverseSteelRequiredMm2PerM, 460.0, 485.0);
            Assert.Contains(result.Notes, n => n.Contains("As,min gouverne"));
        }

        [Fact]
        public void TransverseMesh_CoversTheRequirement()
        {
            StripFootingDesignResult result = Design();

            Assert.True(result.Reinforcement.Transverse.AreaPerMetreMm2
                        >= result.TransverseSteelRequiredMm2PerM);
            Assert.InRange(result.Reinforcement.Transverse.AreaPerMetreMm2
                           / result.TransverseSteelRequiredMm2PerM, 1.0, 1.15);
        }

        [Fact]
        public void LongitudinalDistribution_IsAtLeastTwentyPercent()
        {
            StripFootingDesignResult result = Design();

            Assert.True(result.Reinforcement.Longitudinal.AreaPerMetreMm2
                        >= 0.2 * result.Reinforcement.Transverse.AreaPerMetreMm2);
            Assert.Equal(CheckStatus.Pass, Find(result, "Repartition longitudinale").Status);
        }

        [Fact]
        public void ShearSection_FallsOutsideTheFooting()
        {
            StripFootingDesignResult result = Design();

            // d = 353 mm depasse le debord de 350 mm : aucune section a verifier.
            Assert.DoesNotContain(result.Checks,
                c => c.Description.Contains("Effort tranchant"));
            Assert.Contains(result.Notes, n => n.Contains("Semelle compacte"));
        }

        [Fact]
        public void Punching_IsDeclaredNotApplicableRatherThanOmitted()
        {
            CheckResult punching = Find(Design(), "Poinconnement");

            // Une semelle filante sous voile ne poinconne pas, et le dire vaut mieux
            // que de laisser croire a un oubli.
            Assert.Equal(CheckStatus.NotApplicable, punching.Status);
            Assert.Contains("repartie", punching.Comment);
        }

        [Fact]
        public void TransverseBars_NeedAnEndHook()
        {
            StripFootingDesignResult result = Design();
            CheckResult anchorage = Find(result, "Ancrage des armatures transversales");

            // l_bd = 565 mm contre 310 mm disponibles au-dela du nu.
            Assert.Equal(CheckStatus.Warning, anchorage.Status);
            Assert.True(result.Reinforcement.TransverseNeedsHook);
            Assert.Contains("CROCHET", anchorage.Comment);

            // Et le plan de ferraillage le porte reellement.
            Assert.Contains(result.Plan.Groups,
                g => g.Kind == RebarKind.Longitudinal && g.WithHooks);
        }

        [Fact]
        public void FootingIsRigid()
        {
            // a/h = 350/400 = 0,88 <= 2
            Assert.True(Footing().IsRigid);
            Assert.DoesNotContain(Design().Warnings, w => w.Contains("rigide"));
        }

        [Fact]
        public void Plan_HoldsTheTwoMeshesAndTwoStarterRows()
        {
            StripFootingDesignResult result = Design();

            // transversales + longitudinales + deux files d'attentes
            Assert.Equal(4, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
        }

        [Fact]
        public void EveryCheck_CarriesItsClause()
        {
            StripFootingDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
            });
        }

        private static CheckResult Find(StripFootingDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
