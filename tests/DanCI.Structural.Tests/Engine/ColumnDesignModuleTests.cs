using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Engine
{
    /// <summary>
    /// Dimensionnement complet d'un poteau, de bout en bout : le module doit produire un
    /// ferraillage, un plan geometrique coherent et des verifications toutes satisfaites.
    /// </summary>
    public class ColumnDesignModuleTests
    {
        private static ColumnData Column(double width = 400.0, double depth = 400.0,
                                         double height = 3000.0)
        {
            return new ColumnData
            {
                Id = "test-column",
                Name = "P1",
                Shape = SectionShape.Rectangular,
                WidthMm = width,
                DepthMm = depth,
                HeightMm = height
            };
        }

        private static ColumnDesignResult Design(ColumnData column, ColumnDesignSettings settings)
        {
            return new ColumnDesignModule().Design(column, settings, null);
        }

        [Fact]
        public void Un_Poteau_Courant_Est_Dimensionne_Sans_Anomalie()
        {
            var settings = new ColumnDesignSettings { AxialLoadKn = 1500.0 };
            ColumnDesignResult result = Design(Column(), settings);

            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck,
                "Aucune disposition constructive ne doit etre en defaut.");
            Assert.True(result.Reinforcement.TotalBars >= 4);
            Assert.True(result.Reinforcement.SteelAreaMm2 >= result.MinSteelAreaMm2);
            Assert.True(result.Reinforcement.SteelAreaMm2 <= result.MaxSteelAreaMm2);
            Assert.True(result.Reinforcement.LapLengthMm > 0);
        }

        [Fact]
        public void Chaque_Verification_Porte_Sa_Clause_Et_Son_Equation()
        {
            ColumnDesignResult result = Design(Column(), new ColumnDesignSettings { AxialLoadKn = 1000.0 });

            Assert.NotEmpty(result.Checks);
            foreach (CheckResult check in result.Checks)
            {
                Assert.False(string.IsNullOrWhiteSpace(check.Code));
                Assert.False(string.IsNullOrWhiteSpace(check.Clause));
                Assert.False(string.IsNullOrWhiteSpace(check.Equation));
                Assert.False(string.IsNullOrWhiteSpace(check.Description));
            }
        }

        [Fact]
        public void Le_Plan_De_Ferraillage_Contient_Barres_Et_Cadres()
        {
            ColumnDesignResult result = Design(Column(), new ColumnDesignSettings { AxialLoadKn = 1000.0 });

            Assert.NotEmpty(result.Plan.OfKind(RebarKind.Longitudinal));
            Assert.NotEmpty(result.Plan.OfKind(RebarKind.Stirrup));

            int barsInPlan = result.Plan.OfKind(RebarKind.Longitudinal).Sum(g => g.BarCount);
            Assert.Equal(result.Reinforcement.TotalBars, barsInPlan);

            foreach (RebarGroup group in result.Plan.Groups)
            {
                Assert.True(group.BarLengthMm > 0, "Chaque groupe doit avoir une longueur non nulle.");
                Assert.False(string.IsNullOrWhiteSpace(group.Mark), "Chaque groupe doit etre repere.");
            }
        }

        [Fact]
        public void Les_Barres_Tiennent_Dans_La_Section()
        {
            ColumnData column = Column(300.0, 300.0);
            ColumnDesignResult result = Design(column, new ColumnDesignSettings { AxialLoadKn = 900.0 });

            double halfWidth = column.WidthMm / 2.0;
            double halfDepth = column.DepthMm / 2.0;
            foreach (BarPosition bar in ColumnLayoutGeometry.Bars(column, result.Reinforcement))
            {
                Assert.InRange(bar.XMm, -halfWidth, halfWidth);
                Assert.InRange(bar.YMm, -halfDepth, halfDepth);
            }
        }

        [Fact]
        public void Le_Quantitatif_Correspond_Au_Plan()
        {
            ColumnData column = Column();
            ColumnDesignResult result = Design(column, new ColumnDesignSettings { AxialLoadKn = 1200.0 });
            SteelQuantities quantities = QuantityCalculator.Compute(column, result.Plan);

            Assert.Equal(result.Reinforcement.TotalBars, quantities.LongitudinalBarCount);
            Assert.True(quantities.TotalMassKg > 0);
            Assert.True(quantities.ConcreteVolumeM3 > 0);
            // Un poteau courant se situe dans une fourchette de 80 a 350 kg/m3.
            Assert.InRange(quantities.RatioKgPerM3, 60.0, 400.0);
        }

        [Fact]
        public void Les_Zones_Critiques_Resserrent_Les_Cadres()
        {
            var settings = new ColumnDesignSettings { AxialLoadKn = 1200.0, UseCriticalZones = true };
            ColumnDesignResult result = Design(Column(), settings);

            Assert.True(result.Reinforcement.SpacingCriticalMm <= result.Reinforcement.SpacingCurrentMm);
            Assert.True(result.Reinforcement.CriticalZoneLengthMm > 0);
        }

        [Fact]
        public void La_Verification_N_M_Renforce_Le_Poteau_Quand_Le_Moment_Croit()
        {
            var light = new ColumnDesignSettings
            {
                VerifyCapacity = true,
                AxialLoadKn = 800.0,
                MomentAboutXKnm = 20.0,
                BucklingFactor = 0.7
            };
            var heavy = new ColumnDesignSettings
            {
                VerifyCapacity = true,
                AxialLoadKn = 800.0,
                MomentAboutXKnm = 220.0,
                BucklingFactor = 0.7
            };

            ColumnDesignResult withLightMoment = Design(Column(), light);
            ColumnDesignResult withHeavyMoment = Design(Column(), heavy);

            Assert.True(withLightMoment.IsValid);
            Assert.True(withHeavyMoment.IsValid);
            Assert.True(withHeavyMoment.Reinforcement.SteelAreaMm2
                        >= withLightMoment.Reinforcement.SteelAreaMm2,
                "Un moment plus eleve doit conduire a au moins autant d'acier.");
        }

        [Fact]
        public void Une_Section_Insuffisante_Est_Signalee_Sans_Ferraillage_Arbitraire()
        {
            // 250 x 250 sous 3 000 kN : la section ne peut pas equilibrer l'effort.
            var settings = new ColumnDesignSettings
            {
                VerifyCapacity = true,
                AxialLoadKn = 3000.0,
                MomentAboutXKnm = 100.0
            };
            ColumnDesignResult result = Design(Column(250.0, 250.0), settings);

            Assert.NotNull(result);
            bool reported = result.HasFailedCheck || result.Warnings.Count > 0;
            Assert.True(reported, "L'echec doit etre signale explicitement a l'utilisateur.");
        }

        [Fact]
        public void La_Combinaison_Est_Tracee_Dans_Les_Verifications()
        {
            ColumnDesignResult result = Design(Column(),
                new ColumnDesignSettings { AxialLoadKn = 1000.0, VerifyCapacity = true });

            Assert.Contains(result.Checks, c => c.GoverningCombination == "ULS-COMB-001");
        }
    }
}
