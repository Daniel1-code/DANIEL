using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.IsolatedFooting;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// FOOT-01 : semelle isolee 2400 x 2400 x 600 sous poteau 400 x 400.
    /// Fiche de validation : docs/validation/FOOT-01.md
    ///
    /// C25/30, B500, XC2, beton coule sur beton de proprete, sol a 250 kPa,
    /// N_Ed = 1200 kN centre, poids propre inclus.
    ///
    /// Poids propre : 2,4 x 2,4 x 0,6 x 25 = 86,4 kN -> N total = 1 286,4 kN.
    /// Contraintes  : sigma_geo = 1 286,4 / 5,76 = 223,3 kPa (verification du sol)
    ///                sigma_net = 1 200 / 5,76 = 208,3 kPa (calcul structurel).
    /// Enrobage     : XC2 classe S4 -> c_min,dur = 25 mm, c_min,b = 12 mm,
    ///                c_nom = 35 mm, releve a 40 mm par l'article 4.4.1.3(4).
    ///                d_x = 600 - 40 - 6 = 554 mm ; d_y = 554 - 12 = 542 mm.
    /// Flexion      : console de 1 000 mm, M = 2 400 x 1 000^2 x 0,20833 / 2 = 250 kN.m,
    ///                mu = 0,0204 -> As = 1 059 mm2, mais
    ///                As,min = 0,26 x 2,6 / 500 x 2 400 x 554 = 1 797 mm2 gouverne,
    ///                soit 749 mm2/m -> HA12 e = 150 (754 mm2/m).
    /// Poinconnement: v_Rd,c de base = v_min = 0,035 k^1,5 sqrt(f_ck) = 0,356 MPa
    ///                (k = 1,604 avec d = 548 mm) ; le perimetre le plus defavorable
    ///                se situe vers a = 400 mm, soit 0,73 d, avec un taux de 0,42.
    ///                Au nu du poteau : 1 200 000 / (1 600 x 548) = 1,369 MPa
    ///                contre v_Rd,max = 0,5 x 0,54 x 16,667 = 4,50 MPa.
    /// Tranchant    : section a d du nu, a 446 mm du bord :
    ///                V_Ed = 0,20833 x 2 400 x 446 = 223 kN
    ///                V_Rd,c = 0,354 x 2 400 x 554 = 471 kN -> taux 0,47.
    /// </summary>
    public class Foot01DesignTests
    {
        private static FootingData Footing()
        {
            return new FootingData
            {
                Id = "FOOT-01",
                Name = "S1",
                Mark = "S1",
                WidthXMm = 2400.0,
                WidthYMm = 2400.0,
                ThicknessMm = 600.0,
                ColumnWidthXMm = 400.0,
                ColumnWidthYMm = 400.0
            };
        }

        private static FootingDesignSettings Settings()
        {
            return new FootingDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                AllowableBearingPressureKpa = 250.0,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                CastDirectlyAgainstSoil = false,
                AxialLoadKn = 1200.0,
                IncludeSelfWeight = true,
                AutoMeshDiameter = true,
                StarterBarCount = 4,
                StarterBarDiameterMm = 16.0
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
        public void SelfWeight_MatchesHandCalculation()
        {
            // 2,4 x 2,4 x 0,6 x 25 = 86,4 kN
            Assert.Equal(86400.0, Footing().SelfWeightN(25.0), 0);
        }

        [Fact]
        public void SoilPressure_MatchesHandCalculation()
        {
            FootingDesignResult result = Design();

            // Charge centree : la distribution est uniforme.
            Assert.True(result.Pressure.WithinCore);
            Assert.InRange(result.Pressure.EffectivePressureKpa, 222.0, 225.0);
            Assert.InRange(result.Pressure.NetPressureKpa, 207.0, 210.0);
        }

        [Fact]
        public void Cover_IsRaisedToFortyMillimetres()
        {
            // EC2 4.4.1.3(4) : plancher de 40 mm sur beton de proprete.
            FootingDesignResult result = Design();
            Assert.Equal(40.0, result.Reinforcement.CoverMm, 1);
        }

        [Fact]
        public void EffectiveDepths_MatchHandCalculation()
        {
            FootingDesignResult result = Design();
            Assert.InRange(result.Reinforcement.EffectiveDepthXMm, 552.0, 556.0);
            Assert.InRange(result.Reinforcement.EffectiveDepthYMm, 540.0, 544.0);
        }

        [Fact]
        public void MinimumSteel_Governs()
        {
            FootingDesignResult result = Design();

            // As,min = 1 797 mm2 sur 2,40 m, soit 749 mm2/m ; le calcul de flexion
            // seul n'en demanderait que 441 mm2/m.
            Assert.InRange(result.RequiredSteelXMm2PerM, 735.0, 760.0);
            Assert.Contains(result.Notes, n => n.Contains("As,min"));
        }

        [Fact]
        public void BottomMesh_IsSelected()
        {
            FootingDesignResult result = Design();

            // HA12 e = 150 (754 mm2/m) et HA14 e = 200 (770 mm2/m) obtiennent des notes
            // presque identiques : ce qui est verifie ici est la substance du choix,
            // pas le vainqueur au centieme de point.
            Assert.True(result.Reinforcement.BottomX.AreaPerMetreMm2
                        >= result.RequiredSteelXMm2PerM,
                        "La nappe retenue doit fournir la section requise.");
            Assert.True(result.Reinforcement.BottomY.AreaPerMetreMm2
                        >= result.RequiredSteelYMm2PerM,
                        "La nappe retenue doit fournir la section requise.");

            // Pas de gaspillage : l'optimiseur ne doit pas depasser de plus de 10 %.
            Assert.InRange(result.Reinforcement.BottomX.AreaPerMetreMm2
                           / result.RequiredSteelXMm2PerM, 1.0, 1.10);

            // Espacement reglementaire, art. 9.3.1.1(3).
            Assert.InRange(result.Reinforcement.BottomX.SpacingMm, 100.0, 400.0);
            Assert.InRange(result.Reinforcement.BottomX.DiameterMm, 10.0, 16.0);
        }

        [Fact]
        public void Bearing_UtilizationMatchesHandCalculation()
        {
            CheckResult bearing = Find(Design(), "Capacite portante");

            // 223,3 / 250 = 0,893
            Assert.InRange(bearing.Utilization, 0.87, 0.92);
            Assert.Equal(CheckStatus.Pass, bearing.Status);
        }

        [Fact]
        public void ColumnFacePunching_MatchesHandCalculation()
        {
            FootingDesignResult result = Design();

            // v_Ed = 1 200 000 / (1 600 x 548) = 1,369 MPa
            Assert.InRange(result.Punching.ColumnFaceStressMPa, 1.33, 1.41);
            // v_Rd,max = 0,5 x 0,54 x 16,667 = 4,50 MPa
            Assert.InRange(result.Punching.MaxStressMPa, 4.4, 4.6);
        }

        [Fact]
        public void CriticalPunchingPerimeter_IsNotAtTwoD()
        {
            FootingDesignResult result = Design();
            double d = result.Reinforcement.MeanEffectiveDepthMm;

            Assert.NotNull(result.Punching.Critical);
            // Le balayage de l'article 6.4.4(2) place le perimetre le plus defavorable
            // vers 0,73 d, et non a 2 d.
            Assert.InRange(result.Punching.Critical.DistanceMm / d, 0.60, 0.90);
            Assert.InRange(result.Punching.Critical.Utilization, 0.38, 0.47);
        }

        [Fact]
        public void OneWayShear_MatchesHandCalculation()
        {
            CheckResult shear = Find(Design(), "Effort tranchant unidirectionnel");

            // V_Ed = 223 kN
            Assert.InRange(shear.Demand.Value, 215.0, 232.0);
            // V_Rd,c = 471 kN
            Assert.InRange(shear.Resistance.Value, 450.0, 490.0);
            Assert.Equal(CheckStatus.Pass, shear.Status);
        }

        [Fact]
        public void Plan_ContainsBothMeshesAndStarters()
        {
            FootingDesignResult result = Design();

            // 2 nappes inferieures + 4 attentes, sans nappe superieure.
            Assert.Equal(6, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
        }

        [Fact]
        public void EveryCheck_CarriesItsClause()
        {
            FootingDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
            });
        }

        [Fact]
        public void UndersizedFooting_IsReportedNotSilentlyReinforced()
        {
            FootingData small = Footing();
            small.WidthXMm = 1200.0;
            small.WidthYMm = 1200.0;

            FootingDesignResult result = new FootingDesignModule().Design(small, Settings(), null);

            // 1 200 / 1,44 = 833 kPa contre 250 kPa admissibles.
            CheckResult bearing = Find(result, "Capacite portante");
            Assert.Equal(CheckStatus.Fail, bearing.Status);
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
