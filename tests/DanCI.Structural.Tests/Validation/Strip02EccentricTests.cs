using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.StripFooting;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STRIP-02 : ce qui distingue une semelle filante d'une semelle isolee.
    /// Fiche de validation : docs/validation/STRIP-02.md
    ///
    /// Quatre situations que le module doit traiter differemment de la semelle isolee :
    /// la semelle large qui cesse d'etre rigide, l'excentrement transversal, le debord
    /// assez long pour que le tranchant redevienne dimensionnant, et le fait qu'une
    /// semelle filante ne poinconne jamais.
    /// </summary>
    public class Strip02EccentricTests
    {
        private static StripFootingDesignSettings Settings()
        {
            return new StripFootingDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                AllowableBearingPressureKpa = 250.0,
                InterfaceFrictionAngleDeg = 30.0,
                AutoCover = true,
                Exposure = ExposureClass.XC2,
                CastDirectlyAgainstSoil = false,
                AxialLoadKnPerM = 200.0,
                IncludeSelfWeight = true,
                AutoMeshDiameter = true,
                Starters = true
            };
        }

        [Fact]
        public void WideThinFooting_IsFlaggedAsNoLongerRigid()
        {
            // Debord (1 800 - 200)/2 = 800 mm pour 300 mm d'epaisseur : rapport 2,67 > 2.
            var footing = new StripFootingData
            {
                Name = "SF-souple",
                WidthMm = 1800.0, ThicknessMm = 300.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, Settings(), null);

            Assert.False(footing.IsRigid);
            Assert.Contains(result.Warnings,
                w => w.Contains("cesse d'etre rigide") && w.Contains("Epaississez"));
        }

        [Fact]
        public void LongOverhang_BringsShearBack()
        {
            // Debord 800 mm contre d ~ 250 mm : la section a d du nu retombe dans la
            // semelle et l'effort tranchant redevient une vraie verification.
            var footing = new StripFootingData
            {
                WidthMm = 1800.0, ThicknessMm = 300.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, Settings(), null);

            CheckResult shear = Find(result, "Effort tranchant transversal");
            Assert.True(shear.Demand.Value > 0);
        }

        [Fact]
        public void CompactFooting_HasNoShearSectionAtAll()
        {
            // Debord 350 mm contre d ~ 350 mm : la section tombe hors de la semelle.
            var footing = new StripFootingData
            {
                WidthMm = 900.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, Settings(), null);

            Assert.DoesNotContain(result.Checks,
                c => c.Description.Contains("Effort tranchant"));
        }

        [Fact]
        public void Eccentricity_IncludesTheMomentFromTheHorizontalForce()
        {
            var footing = new StripFootingData
            {
                WidthMm = 1200.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };
            StripFootingDesignSettings settings = Settings();
            settings.MomentKnmPerM = 20.0;
            settings.HorizontalLoadKnPerM = 25.0;

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, settings, null);

            // M total = 20 + 25 x 0,40 = 30 kN.m/m ; N total = 200 + 1,2 x 0,4 x 25 = 212 kN/m
            //   e = 30 / 212 = 0,1415 m = 142 mm ; B/6 = 200 mm -> dans le noyau
            Assert.InRange(result.Pressure.EccentricityXMm, 135.0, 150.0);
            Assert.True(result.Pressure.WithinCore);

            // La largeur effective se reduit : B' = 1 200 - 2 x 142 = 916 mm
            Assert.InRange(result.Pressure.EffectiveWidthMm, 895.0, 935.0);
        }

        [Fact]
        public void SlidingIsCheckedWhenThereIsAHorizontalForce()
        {
            var footing = new StripFootingData
            {
                WidthMm = 1200.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };
            StripFootingDesignSettings settings = Settings();
            settings.HorizontalLoadKnPerM = 25.0;

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, settings, null);

            CheckResult sliding = Find(result, "Glissement");
            // R_d = 212 x tan(30)/1,25 = 98 kN/m contre 25 kN/m
            Assert.InRange(sliding.Resistance.Value, 88.0, 110.0);
            Assert.Equal(CheckStatus.Pass, sliding.Status);
        }

        [Fact]
        public void ExcessiveEccentricity_IsReportedNotAbsorbed()
        {
            var footing = new StripFootingData
            {
                WidthMm = 1000.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };
            StripFootingDesignSettings settings = Settings();
            settings.MomentKnmPerM = 80.0;

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, settings, null);

            // e = 80 / 210 = 381 mm contre B/6 = 167 mm : la semelle decolle.
            Assert.False(result.Pressure.WithinCore);
            Assert.Equal(CheckStatus.Fail, Find(result, "Absence de soulevement").Status);
            Assert.NotEqual("OK", result.Status);
        }

        [Fact]
        public void PunchingIsNeverApplicable_WhateverTheGeometry()
        {
            foreach (double width in new[] { 900.0, 1500.0, 2400.0 })
            {
                var footing = new StripFootingData
                {
                    WidthMm = width, ThicknessMm = 450.0, LengthMm = 8000.0,
                    WallThicknessMm = 200.0
                };

                StripFootingDesignResult result =
                    new StripFootingDesignModule().Design(footing, Settings(), null);

                CheckResult punching = Find(result, "Poinconnement");
                Assert.Equal(CheckStatus.NotApplicable, punching.Status);
                // Et il rappelle le cas ou ce serait faux de conclure.
                Assert.Contains("poteaux", punching.Comment);
            }
        }

        [Fact]
        public void NotApplicableChecks_DoNotInflateTheUtilisation()
        {
            var footing = new StripFootingData
            {
                WidthMm = 900.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, Settings(), null);

            // Le poinconnement sans objet a un taux de 0 : il ne doit ni compter dans le
            // maximum, ni le tirer vers le bas de facon trompeuse.
            Assert.True(result.MaxUtilization > 0);
            Assert.True(result.MaxUtilization <= 1.0);
        }

        [Fact]
        public void CastAgainstSoil_RaisesTheCoverToSeventyFive()
        {
            var footing = new StripFootingData
            {
                WidthMm = 900.0, ThicknessMm = 400.0, LengthMm = 8000.0,
                WallThicknessMm = 200.0
            };
            StripFootingDesignSettings settings = Settings();
            settings.CastDirectlyAgainstSoil = true;

            StripFootingDesignResult result =
                new StripFootingDesignModule().Design(footing, settings, null);

            Assert.Equal(75.0, result.Reinforcement.CoverMm, 1);
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
