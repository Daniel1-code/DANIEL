using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// COLUMN-05 : dispositions constructives completes d'un poteau 300 x 500.
    /// Fiche de validation : docs/validation/COLUMN-05.md
    ///
    /// Le ferraillage est impose (10 HA16, 3 barres par face // X et 4 par face // Y) afin que
    /// toutes les cotes soient calculables a la main :
    ///   Enrobage XC1 / C25 / HA16 : S4, c_min,dur = 15, c_min = max(16 ; 15 ; 10) = 16,
    ///                               c_nom = 26 mm
    ///   Cadre     : phi_t = max(6 ; 16/4) = 6 mm
    ///   Espacement: scl,tmax = min(20 x 16 ; 300 ; 400) = 300 mm
    ///   Zones critiques : 0,6 x 300 = 180 -> 175 mm, sur max(300 ; 500) = 500 mm
    ///   Demi-portees : X = 150 - 26 - 6 - 8 = 110 mm ; Y = 250 - 26 - 6 - 8 = 210 mm
    ///   Entraxes     : 2 x 110 / 2 = 110 mm ; 2 x 210 / 3 = 140 mm
    ///   Aucune epingle : les deux entraxes restent sous 150 mm
    ///   Recouvrement HA16 en C25 : 968 mm -> arrondi a 1 000 mm
    /// </summary>
    public class Column05DetailingTests
    {
        private static ColumnDesignResult Design()
        {
            var column = new ColumnData
            {
                Id = "COLUMN-05",
                Name = "P5",
                Mark = "C5",
                Shape = SectionShape.Rectangular,
                WidthMm = 300.0,
                DepthMm = 500.0,
                HeightMm = 3000.0
            };

            var settings = new ColumnDesignSettings
            {
                AxialLoadKn = 1200.0,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                ConcreteStrengthMPa = 25.0,
                AutoLongitudinalDiameter = false,
                ForcedLongitudinalDiameterMm = 16.0,
                AutoBarCount = false,
                ForcedBarsAlongX = 3,
                ForcedBarsAlongY = 4,
                AutoTransverse = true,
                UseCriticalZones = true,
                AddCrossTies = true
            };

            return new ColumnDesignModule().Design(column, settings, null);
        }

        [Fact]
        public void L_Enrobage_Calcule_Vaut_26_mm()
        {
            Assert.Equal(26.0, Design().Reinforcement.CoverMm, 6);
        }

        [Fact]
        public void Le_Ferraillage_Impose_Est_Respecte()
        {
            ColumnReinforcement r = Design().Reinforcement;
            Assert.Equal(16.0, r.BarDiameterMm, 6);
            Assert.Equal(3, r.BarsAlongX);
            Assert.Equal(4, r.BarsAlongY);
            Assert.Equal(10, r.TotalBars);
            // 10 x 201,06 = 2 010,6 mm2
            Assert.InRange(r.SteelAreaMm2, 2009.0, 2012.0);
        }

        [Fact]
        public void Les_Cadres_Suivent_L_Article_9_5_3()
        {
            ColumnReinforcement r = Design().Reinforcement;
            Assert.Equal(6.0, r.StirrupDiameterMm, 6);
            Assert.Equal(300.0, r.SpacingCurrentMm, 6);
            Assert.Equal(175.0, r.SpacingCriticalMm, 6);
            Assert.Equal(500.0, r.CriticalZoneLengthMm, 6);
        }

        [Fact]
        public void Les_Entraxes_Correspondent_Au_Calcul_Manuel()
        {
            ColumnDesignResult result = Design();
            Assert.Equal(110.0, ColumnLayoutGeometry.BarHalfSpanX(result.Column, result.Reinforcement), 6);
            Assert.Equal(210.0, ColumnLayoutGeometry.BarHalfSpanY(result.Column, result.Reinforcement), 6);
            Assert.Equal(110.0, ColumnLayoutGeometry.PitchX(result.Column, result.Reinforcement), 6);
            Assert.Equal(140.0, ColumnLayoutGeometry.PitchY(result.Column, result.Reinforcement), 6);
        }

        [Fact]
        public void Aucune_Epingle_N_Est_Necessaire()
        {
            ColumnReinforcement r = Design().Reinforcement;
            Assert.Equal(0, r.CrossTiesAlongX);
            Assert.Equal(0, r.CrossTiesAlongY);
        }

        [Fact]
        public void Le_Recouvrement_Vaut_1000_mm()
        {
            Assert.Equal(1000.0, Design().Reinforcement.LapLengthMm, 6);
        }

        [Fact]
        public void Toutes_Les_Verifications_Sont_Satisfaites()
        {
            ColumnDesignResult result = Design();
            Assert.True(result.IsValid);
            foreach (CheckResult check in result.Checks)
            {
                Assert.NotEqual(CheckStatus.Fail, check.Status);
            }
        }

        [Fact]
        public void Les_Reperes_De_Barres_Portent_Le_Repere_Du_Poteau()
        {
            ColumnDesignResult result = Design();
            Assert.All(result.Plan.Groups, group => Assert.StartsWith("C5-", group.Mark));
            // Aucun doublon de repere a l'interieur d'un element.
            int distinct = result.Plan.Groups.Select(g => g.Mark).Distinct().Count();
            Assert.Equal(result.Plan.Groups.Count, distinct);
        }

        [Fact]
        public void Le_Nombre_De_Cadres_Correspond_Aux_Zones()
        {
            // Hauteur utile 3 000 - 2 x 50 = 2 900 mm, decoupee en 500 / 1 900 / 500 mm.
            //   zone basse  : ceil(500/175) + 1 = 4 cadres
            //   zone courante : ceil(1900/300) + 1 - 2 = 6 cadres
            //   zone haute  : 4 cadres
            ColumnDesignResult result = Design();
            SteelQuantities quantities = QuantityCalculator.Compute(result.Column, result.Plan);
            Assert.Equal(14, quantities.StirrupCount);
        }
    }
}
