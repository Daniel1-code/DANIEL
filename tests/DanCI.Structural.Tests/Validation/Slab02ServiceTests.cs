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
    /// SLAB-02 : ce qui gouverne reellement une dalle.
    /// Fiche de validation : docs/validation/SLAB-02.md
    ///
    /// Une dalle est rarement limitee par sa resistance. Cette fiche verifie les trois
    /// situations que le module doit annoncer clairement plutot que de les masquer :
    /// la fleche qui gouverne, les coefficients de continuite qui ne sont pas de
    /// l'Eurocode, et le panneau qui ne porte pas dans un seul sens.
    /// </summary>
    public class Slab02ServiceTests
    {
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
                Category = UseCategory.Residential,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true
            };
        }

        [Fact]
        public void ThinSlabOnALongSpan_FailsOnDeflectionNotOnStrength()
        {
            var slab = new SlabData
            {
                Id = "SLAB-02a", Name = "D2", Mark = "D2",
                ThicknessMm = 160.0, SpanMm = 6000.0, WidthMm = 15000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };

            SlabDesignResult result = new SlabDesignModule().Design(slab, Settings(), null);

            // La flexion passe : le moteur trouve une nappe qui fournit l'acier requis.
            CheckResult bending = Find(result, "Flexion en travee");
            Assert.Equal(CheckStatus.Pass, bending.Status);

            // C'est la fleche qui refuse : l/d = 6 000 / ~130 est bien au-dela de la limite.
            CheckResult deflection = Find(result, "Fleche");
            Assert.Equal(CheckStatus.Fail, deflection.Status);
            Assert.True(result.HasFailedCheck);

            // Et le message nomme l'action la plus efficace.
            Assert.Contains(result.Warnings, w => w.Contains("FLECHE") && w.Contains("epaissir"));
        }

        [Fact]
        public void ThickeningTheSlab_FixesTheDeflection()
        {
            var thin = new SlabData
            {
                ThicknessMm = 160.0, SpanMm = 6000.0, WidthMm = 15000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };
            var thick = new SlabData
            {
                ThicknessMm = 260.0, SpanMm = 6000.0, WidthMm = 15000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };

            var module = new SlabDesignModule();
            SlabDesignResult thinResult = module.Design(thin, Settings(), null);
            SlabDesignResult thickResult = module.Design(thick, Settings(), null);

            Assert.False(thinResult.Deflection.Passes);
            Assert.True(thickResult.Deflection.Passes);
            Assert.Equal("OK", thickResult.Status);
        }

        [Fact]
        public void ContinuityCoefficients_AreAnnouncedAsBeingOutsideTheEurocode()
        {
            var slab = new SlabData
            {
                ThicknessMm = 220.0, SpanMm = 5000.0, WidthMm = 12000.0,
                SpanKind = SlabSpanKind.EndSpan
            };

            SlabDesignResult result = new SlabDesignModule().Design(slab, Settings(), null);

            // w l2/11 en travee et w l2/9 sur appui : ce sont des coefficients de pratique.
            Assert.True(result.SupportMomentKnmPerM > result.SpanMomentKnmPerM);
            Assert.Contains(result.Notes,
                n => n.Contains("PAS des") && n.Contains("Eurocode"));
            Assert.Contains(result.Warnings, w => w.Contains("hors") && w.Contains("Eurocode"));

            // Des chapeaux apparaissent d'eux-memes des qu'il y a un moment negatif.
            Assert.True(result.Reinforcement.HasTopReinforcement);
            Assert.True(result.Reinforcement.TopBarLengthMm > 0);
        }

        [Fact]
        public void EnteredMoments_AreNotRecomputed()
        {
            var slab = new SlabData
            {
                ThicknessMm = 220.0, SpanMm = 5000.0, WidthMm = 12000.0,
                SpanKind = SlabSpanKind.EndSpan
            };
            SlabDesignSettings settings = Settings();
            settings.MomentSource = SlabMomentSource.Entered;
            settings.SpanMomentKnmPerM = 38.0;
            settings.SupportMomentKnmPerM = 47.0;
            settings.ShearKnPerM = 42.0;

            SlabDesignResult result = new SlabDesignModule().Design(slab, settings, null);

            Assert.Equal(38.0, result.SpanMomentKnmPerM, 3);
            Assert.Equal(47.0, result.SupportMomentKnmPerM, 3);
            Assert.Equal(42.0, result.ShearKnPerM, 3);
            // Aucun avertissement de coefficient de continuite : rien n'a ete estime.
            Assert.DoesNotContain(result.Warnings, w => w.Contains("coefficients"));
        }

        [Fact]
        public void SquarePanel_IsFlaggedAsTwoWay()
        {
            var slab = new SlabData
            {
                ThicknessMm = 220.0, SpanMm = 5000.0, WidthMm = 6000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };

            SlabDesignResult result = new SlabDesignModule().Design(slab, Settings(), null);

            // 6 000 / 5 000 = 1,20 < 2 : la dalle porte dans les deux sens.
            Assert.False(slab.IsGenuinelyOneWay);
            Assert.Contains(result.Warnings, w => w.Contains("deux sens"));
            Assert.Equal("A verifier", result.Status);
        }

        [Fact]
        public void SpanDeclaredInTheLongDirection_IsFlagged()
        {
            var slab = new SlabData
            {
                ThicknessMm = 220.0, SpanMm = 6000.0, WidthMm = 4000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };

            SlabDesignResult result = new SlabDesignModule().Design(slab, Settings(), null);

            Assert.True(slab.SpanIsTheLongDirection);
            Assert.Contains(result.Warnings, w => w.Contains("plus court chemin"));
        }

        [Fact]
        public void Cantilever_UsesStaticsAndPutsSteelOnTop()
        {
            var slab = new SlabData
            {
                ThicknessMm = 200.0, SpanMm = 1500.0, WidthMm = 6000.0,
                SpanKind = SlabSpanKind.Cantilever
            };
            SlabDesignSettings settings = Settings();
            settings.TopReinforcement = true;

            SlabDesignResult result = new SlabDesignModule().Design(slab, settings, null);

            // pp 5,0 + 2,0 = 7,0 ; ELU = 1,35 x 7,0 + 1,5 x 2,5 = 13,20 kN/m2
            //   M = w l2/2 = 13,20 x 1,5^2 / 2 = 14,85 kN.m/m ; V = 19,8 kN/m
            Assert.Equal(14.85, result.SupportMomentKnmPerM, 2);
            Assert.Equal(19.8, result.ShearKnPerM, 2);
            Assert.Equal(0.0, result.SpanMomentKnmPerM, 6);

            Assert.True(result.Reinforcement.HasTopReinforcement);
            // K = 0,4 pour une console, tableau 7.4N.
            Assert.Equal(0.4, result.Deflection.SystemFactor, 3);
            Assert.Contains(result.Notes, n => n.Contains("statique seule"));
        }

        [Fact]
        public void StorageLoading_IsHarderOnServiceabilityThanOffices()
        {
            var slab = new SlabData
            {
                ThicknessMm = 220.0, SpanMm = 5000.0, WidthMm = 12000.0,
                SpanKind = SlabSpanKind.SimplySupported
            };

            SlabDesignSettings office = Settings();
            office.Category = UseCategory.Office;
            SlabDesignSettings storage = Settings();
            storage.Category = UseCategory.Storage;

            var module = new SlabDesignModule();
            SlabDesignResult officeResult = module.Design(slab, office, null);
            SlabDesignResult storageResult = module.Design(slab, storage, null);

            // Meme ELU, donc meme ferraillage ; mais la combinaison quasi-permanente est
            // plus lourde en stockage, donc sigma_s plus elevee et la fissuration plus dure.
            Assert.Equal(officeResult.UltimateLoadKnM2, storageResult.UltimateLoadKnM2, 4);
            Assert.True(storageResult.QuasiPermanentLoadKnM2 > officeResult.QuasiPermanentLoadKnM2);
            Assert.True(storageResult.Cracking.SteelStressMPa
                        > officeResult.Cracking.SteelStressMPa);
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
