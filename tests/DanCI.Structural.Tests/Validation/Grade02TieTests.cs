using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Eurocodes.EC8;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// GRADE-02 : ce qui fait qu'une longrine n'est pas une poutre posee bas.
    /// Fiche de validation : docs/validation/GRADE-02.md
    ///
    /// Quatre points sur lesquels le module doit se comporter autrement qu'un module
    /// poutre : l'effort de liaison qui n'existe pas hors seisme, le minimum sismique
    /// qui gouverne les deux nappes, la section minimale de l'article 5.8.1, et l'appui
    /// du sol dont il ne faut pas prendre credit sans analyse.
    /// </summary>
    public class Grade02TieTests
    {
        private static GradeBeamData Beam()
        {
            return new GradeBeamData
            {
                Name = "LG2",
                WidthMm = 300.0, HeightMm = 500.0, SpanMm = 5000.0,
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
                SeismicDesign = false,
                StoreyCount = 3,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                AutoLongitudinalDiameter = true,
                AutoStirrupDiameter = true,
                StirrupLegs = 2
            };
        }

        [Fact]
        public void OutsideSeismicDesign_NoTieForceIsInvented()
        {
            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(Beam(), Settings(), null);

            Assert.Equal(0.0, result.TieForceKn, 6);
            Assert.Null(result.Tie);
            // Le moteur dit pourquoi, et ou chercher si le projet en exige un.
            Assert.Contains(result.Notes,
                n => n.Contains("EN 1992") && n.Contains("EN 1991-1-7"));
            Assert.DoesNotContain(result.Checks,
                c => c.Description.Contains("Effort de liaison en compression"));
        }

        [Fact]
        public void ManualTieForce_IsUsedAndItsOriginStated()
        {
            GradeBeamDesignSettings settings = Settings();
            settings.ManualTieForceKn = 80.0;

            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(Beam(), settings, null);

            Assert.Equal(80.0, result.TieForceKn, 3);
            Assert.Contains(result.Notes,
                n => n.Contains("impose par l'utilisateur") && n.Contains("aucun article"));
            // Un effort de liaison declare fait apparaitre la verification en compression.
            Assert.Contains(result.Checks,
                c => c.Description.Contains("Effort de liaison en compression"));
        }

        [Fact]
        public void SeismicMinimum_IsFarMoreDemandingThanEurocode2()
        {
            var module = new GradeBeamDesignModule();

            GradeBeamDesignSettings ordinary = Settings();
            GradeBeamDesignSettings seismic = Settings();
            seismic.SeismicDesign = true;
            seismic.Ground = GroundType.C;
            seismic.GroundAccelerationRatio = 0.15;
            seismic.SoilFactor = 1.15;
            seismic.MeanColumnAxialLoadKn = 800.0;

            GradeBeamDesignResult ordinaryResult = module.Design(Beam(), ordinary, null);
            GradeBeamDesignResult seismicResult = module.Design(Beam(), seismic, null);

            // 0,4 % de 150 000 = 600 mm2 contre 177 mm2 pour l'EC2 : facteur 3,4.
            Assert.Equal(600.0, seismicResult.TopSteelRequiredMm2, 1);
            Assert.True(ordinaryResult.TopSteelRequiredMm2 < 250.0);
            Assert.True(seismicResult.Reinforcement.TopBars.AreaMm2
                        > ordinaryResult.Reinforcement.TopBars.AreaMm2);
        }

        [Fact]
        public void UndersizedSection_IsFlaggedAgainstClause5811()
        {
            // 200 x 350 : sous les 250 x 400 minimaux d'un batiment de 3 niveaux.
            var beam = new GradeBeamData
            {
                WidthMm = 200.0, HeightMm = 350.0, SpanMm = 4000.0,
                SpanKind = GradeBeamSpanKind.SimplySupported
            };
            GradeBeamDesignSettings settings = Settings();
            settings.SeismicDesign = true;
            settings.MeanColumnAxialLoadKn = 500.0;

            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(beam, settings, null);

            Assert.Contains(result.Warnings,
                w => w.Contains("5.8.1(4)") && w.Contains("250") && w.Contains("400"));
        }

        [Fact]
        public void TallBuilding_DemandsAFiveHundredDeepTie()
        {
            var beam = new GradeBeamData
            {
                WidthMm = 300.0, HeightMm = 450.0, SpanMm = 4000.0,
                SpanKind = GradeBeamSpanKind.SimplySupported
            };
            var module = new GradeBeamDesignModule();

            GradeBeamDesignSettings low = Settings();
            low.SeismicDesign = true;
            low.StoreyCount = 3;

            GradeBeamDesignSettings tall = Settings();
            tall.SeismicDesign = true;
            tall.StoreyCount = 6;

            // 450 mm passe a trois niveaux, pas a six : l'art. 5.8.1(4) exige 500 mm.
            Assert.DoesNotContain(module.Design(beam, low, null).Warnings,
                w => w.Contains("5.8.1(4)"));
            Assert.Contains(module.Design(beam, tall, null).Warnings,
                w => w.Contains("5.8.1(4)") && w.Contains("500"));
        }

        [Fact]
        public void SoilBearing_IsNeverCreditedWithoutAnalysis()
        {
            var module = new GradeBeamDesignModule();

            GradeBeamDesignSettings suspended = Settings();
            GradeBeamDesignSettings bearing = Settings();
            bearing.Bedding = GradeBeamBedding.SoilBearing;

            GradeBeamDesignResult suspendedResult = module.Design(Beam(), suspended, null);
            GradeBeamDesignResult bearingResult = module.Design(Beam(), bearing, null);

            // Le moment est identique : le moteur ne prend aucun credit de l'appui du sol.
            Assert.Equal(suspendedResult.SpanMomentKnm, bearingResult.SpanMomentKnm, 3);
            Assert.Contains(bearingResult.Notes,
                n => n.Contains("NEANMOINS") && n.Contains("sol elastique"));
        }

        [Fact]
        public void SuspendedBeam_RecallsTheSwellingSoilRule()
        {
            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(Beam(), Settings(), null);

            Assert.Contains(result.Notes,
                n => n.Contains("gonflant") && n.Contains("forme perdue"));
        }

        [Fact]
        public void SoilBearing_WarnsWhenTheContactStressIsImpossible()
        {
            var beam = new GradeBeamData
            {
                WidthMm = 250.0, HeightMm = 500.0, SpanMm = 5000.0,
                SpanKind = GradeBeamSpanKind.SimplySupported
            };
            GradeBeamDesignSettings settings = Settings();
            settings.Bedding = GradeBeamBedding.SoilBearing;
            settings.WallLoadKnPerM = 120.0;
            settings.AllowableBearingPressureKpa = 150.0;

            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(beam, settings, null);

            // (120 + 1,35 x 3,125) / 0,25 = 497 kPa, tres au-dela de 150.
            Assert.Contains(result.Warnings, w => w.Contains("contrainte de contact"));
        }

        [Fact]
        public void ContinuousSpan_AnnouncesItsCoefficientsAsOutsideTheEurocode()
        {
            var beam = Beam();
            beam.SpanKind = GradeBeamSpanKind.InteriorSpan;

            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(beam, Settings(), null);

            Assert.Contains(result.Notes, n => n.Contains("PAS des valeurs de l'Eurocode 2"));
            Assert.Contains(result.Warnings, w => w.Contains("hors") && w.Contains("Eurocode"));
        }

        [Fact]
        public void RockGround_NeedsNoTieEvenUnderSeismicDesign()
        {
            GradeBeamDesignSettings settings = Settings();
            settings.SeismicDesign = true;
            settings.Ground = GroundType.A;
            settings.MeanColumnAxialLoadKn = 1500.0;

            GradeBeamDesignResult result =
                new GradeBeamDesignModule().Design(Beam(), settings, null);

            Assert.Equal(0.0, result.TieForceKn, 6);
            Assert.False(result.Tie.TieRequired);
            // Mais le minimum de 0,4 % de l'art. 5.8.2(5) reste du, lui.
            Assert.Equal(600.0, result.TopSteelRequiredMm2, 1);
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
