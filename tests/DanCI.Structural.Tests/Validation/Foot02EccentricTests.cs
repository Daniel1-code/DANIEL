using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.IsolatedFooting;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// FOOT-02 : semelle rectangulaire 2400 x 3000 x 700 sous poteau 400 x 600,
    /// chargee de facon excentree.
    /// Fiche de validation : docs/validation/FOOT-02.md
    ///
    /// C30/37, B500, XC2, coulee directement contre le sol, sol a 300 kPa,
    /// N_Ed = 1500 kN, M autour de Y = 300 kN.m, V suivant X = 90 kN.
    ///
    /// Poids propre : 2,4 x 3,0 x 0,7 x 25 = 126 kN -> N total = 1 626 kN.
    /// Moment a la base : 300 + 90 x 0,7 = 363 kN.m -> e_x = 223 mm &lt; B/6 = 400 mm.
    /// Aire effective : B' = 2 400 - 2 x 223 = 1 954 mm, L' = 3 000 mm
    ///                  -> sigma' = 1 626 / (1,954 x 3,0) = 277 kPa &lt;= 300 kPa.
    /// Enrobage : releve a 75 mm (coulee contre le sol) ; d_x = 617, d_y = 601 mm.
    /// Flexion : contrainte de calcul majoree 324,5 kPa ;
    ///           console X (debord 1 000) M = 487 kN.m -> As,min = 2 797 mm2 gouverne ;
    ///           console Y (debord 1 200) M = 561 kN.m -> As,min = 2 194 mm2 gouverne
    ///           de justesse (As de flexion 2 168 mm2).
    /// Tranchant : c'est le grand debord qui gouverne, et il est suivant Y.
    ///           V_Ed,Y = 0,3245 x 2 400 x (1 200 - 601) = 462 kN
    ///           V_Rd,c = 548 kN  ->  taux 0,84, contre 0,53 suivant X.
    /// </summary>
    public class Foot02EccentricTests
    {
        private static FootingData Footing()
        {
            return new FootingData
            {
                Id = "FOOT-02",
                Name = "S2",
                Mark = "S2",
                WidthXMm = 2400.0,
                WidthYMm = 3000.0,
                ThicknessMm = 700.0,
                ColumnWidthXMm = 400.0,
                ColumnWidthYMm = 600.0
            };
        }

        private static FootingDesignSettings Settings()
        {
            return new FootingDesignSettings
            {
                ConcreteStrengthMPa = 30.0,
                SteelStrengthMPa = 500.0,
                AllowableBearingPressureKpa = 300.0,
                InterfaceFrictionAngleDeg = 30.0,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                CastDirectlyAgainstSoil = true,
                AxialLoadKn = 1500.0,
                MomentAboutYKnm = 300.0,
                ShearXKn = 90.0,
                IncludeSelfWeight = true,
                AutoMeshDiameter = true,
                StarterBarCount = 6,
                StarterBarDiameterMm = 20.0
            };
        }

        private static FootingDesignResult Design()
        {
            return new FootingDesignModule().Design(Footing(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            FootingDesignResult result = Design();
            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
        }

        [Fact]
        public void Eccentricity_IncludesTheMomentFromTheHorizontalForce()
        {
            FootingDesignResult result = Design();

            // 300 + 90 x 0,7 = 363 kN.m sur 1 626 kN -> 223 mm.
            Assert.InRange(result.Pressure.EccentricityXMm, 218.0, 228.0);
            Assert.Equal(0.0, result.Pressure.EccentricityYMm, 3);
            Assert.True(result.Pressure.WithinCore);
        }

        [Fact]
        public void EffectiveArea_FollowsMeyerhof()
        {
            FootingDesignResult result = Design();

            // B' = 2 400 - 2 x 223 = 1 954 mm, L' inchangee.
            Assert.InRange(result.Pressure.EffectiveWidthMm, 1944.0, 1964.0);
            Assert.Equal(3000.0, result.Pressure.EffectiveLengthMm, 1);
            Assert.InRange(result.Pressure.EffectivePressureKpa, 272.0, 283.0);
        }

        [Fact]
        public void LinearDistribution_StaysInCompression()
        {
            FootingDesignResult result = Design();

            // sigma = 226 x (1 +- 0,558) = 352 / 100 kPa, aucune traction.
            Assert.InRange(result.Pressure.MaxPressureKpa, 344.0, 360.0);
            Assert.InRange(result.Pressure.MinPressureKpa, 94.0, 106.0);
            Assert.True(result.Pressure.MinPressureKpa > 0);
        }

        [Fact]
        public void Cover_IsRaisedToSeventyFiveMillimetres()
        {
            // EC2 4.4.1.3(4) : beton coule directement contre le sol.
            FootingDesignResult result = Design();
            Assert.Equal(75.0, result.Reinforcement.CoverMm, 1);
        }

        [Fact]
        public void Bearing_UtilizationMatchesHandCalculation()
        {
            CheckResult bearing = Find(Design(), "Capacite portante");

            // 277 / 300 = 0,925
            Assert.InRange(bearing.Utilization, 0.90, 0.96);
            Assert.Equal(CheckStatus.Pass, bearing.Status);
        }

        [Fact]
        public void SlidingAndOverturning_ArePresentAndPass()
        {
            FootingDesignResult result = Design();

            CheckResult sliding = Find(result, "Glissement");
            // R_d = 1 626 x tan(30)/1,25 = 751 kN contre 90 kN.
            Assert.InRange(sliding.Resistance.Value, 720.0, 780.0);
            Assert.Equal(CheckStatus.Pass, sliding.Status);

            CheckResult overturning = Find(result, "Renversement");
            // M_stb = 0,9 x 1 626 x 1,2 = 1 756 kN.m contre 363 kN.m.
            Assert.InRange(overturning.Resistance.Value, 1700.0, 1810.0);
            Assert.Equal(CheckStatus.Pass, overturning.Status);
        }

        [Fact]
        public void LongOverhangGovernsShear()
        {
            FootingDesignResult result = Design();

            CheckResult shearX = Find(result, "Effort tranchant unidirectionnel suivant X");
            CheckResult shearY = Find(result, "Effort tranchant unidirectionnel suivant Y");

            // Le debord suivant Y vaut 1 200 mm contre 1 000 mm suivant X : c'est lui
            // qui gouverne, et ne verifier que X passerait a cote.
            Assert.True(shearY.Utilization > shearX.Utilization);
            Assert.InRange(shearY.Demand.Value, 445.0, 480.0);
            Assert.InRange(shearY.Resistance.Value, 520.0, 575.0);
            Assert.Equal(CheckStatus.Pass, shearY.Status);
        }

        [Fact]
        public void Punching_UsesTheSweptPerimeter()
        {
            FootingDesignResult result = Design();
            double d = result.Reinforcement.MeanEffectiveDepthMm;

            Assert.NotNull(result.Punching.Critical);
            Assert.InRange(result.Punching.Critical.DistanceMm / d, 0.60, 0.95);
            Assert.InRange(result.Punching.Critical.Utilization, 0.32, 0.42);

            // Au nu d'un poteau 400 x 600 : 1 500 000 / (2 000 x 609) = 1,23 MPa.
            Assert.InRange(result.Punching.ColumnFaceStressMPa, 1.18, 1.29);
        }

        [Fact]
        public void MinimumSteel_GovernsInBothDirections()
        {
            FootingDesignResult result = Design();

            // 2 797 / 3,0 = 932 mm2/m suivant X ; 2 194 / 2,4 = 914 mm2/m suivant Y.
            Assert.InRange(result.RequiredSteelXMm2PerM, 915.0, 950.0);
            Assert.InRange(result.RequiredSteelYMm2PerM, 895.0, 935.0);

            Assert.True(result.Reinforcement.BottomX.AreaPerMetreMm2
                        >= result.RequiredSteelXMm2PerM);
            Assert.True(result.Reinforcement.BottomY.AreaPerMetreMm2
                        >= result.RequiredSteelYMm2PerM);
        }

        [Fact]
        public void Plan_PlacesSixStartersAroundTheColumn()
        {
            FootingDesignResult result = Design();

            // 2 nappes + 6 attentes.
            Assert.Equal(8, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
        }

        [Fact]
        public void OverturningFails_WhenTheMomentIsExcessive()
        {
            FootingDesignSettings settings = Settings();
            settings.MomentAboutYKnm = 2500.0;

            FootingDesignResult result = new FootingDesignModule().Design(Footing(), settings, null);

            // La resultante sort largement du noyau central : le moteur le dit.
            Assert.False(result.Pressure.WithinCore);
            Assert.Equal(CheckStatus.Fail, Find(result, "Absence de soulevement").Status);
            Assert.NotEqual("OK", result.Status);
        }

        private static CheckResult Find(FootingDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
