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
    /// WALL-02 : les limites du voile, et ce que le moteur doit dire au lieu de les cacher.
    /// Fiche de validation : docs/validation/WALL-02.md
    ///
    /// Quatre situations qu'un module de voile ne doit jamais traiter en silence :
    /// l'element qui n'est pas un voile, le maintien lateral qui ne sert a rien,
    /// le voile trop mince pour son elancement, et le contreventement en zone sismique.
    /// </summary>
    public class Wall02SlenderTests
    {
        private static WallDesignSettings Settings()
        {
            return new WallDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                Restraint = WallRestraint.TopAndBottom,
                CreepCoefficient = 2.0,
                AxialLoadKnPerM = 400.0,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoVerticalDiameter = true,
                AutoHorizontalDiameter = true
            };
        }

        [Fact]
        public void ShortElement_IsAnnouncedAsAColumn()
        {
            // 600 / 200 = 3,0 < 4 : l'article 9.6.1 dit que c'est un poteau.
            var element = new WallData
            {
                Name = "V-court", ThicknessMm = 200.0, LengthMm = 600.0, ClearHeightMm = 3000.0
            };

            WallDesignResult result = new WallDesignModule().Design(element, Settings(), null);

            Assert.False(element.IsWallByCode);
            Assert.Contains(result.Warnings,
                w => w.Contains("POTEAU") && w.Contains("9.5"));
            Assert.Contains(result.Warnings, w => w.Contains("Column"));
        }

        [Fact]
        public void DistantLateralRestraint_IsNotCountedAsStiffening()
        {
            // Un voile de 3 m de haut avec des retours a 15 m : au milieu, il ne les voit pas.
            var wall = new WallData
            {
                Name = "V-long", ThicknessMm = 200.0, LengthMm = 15000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            settings.Restraint = WallRestraint.ThreeEdges;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            Assert.False(result.Buckling.LateralRestraintEffective);
            Assert.Contains(result.Warnings, w => w.Contains("trop eloigne"));
        }

        [Fact]
        public void CloseLateralRestraint_ReducesTheBucklingLength()
        {
            var wall = new WallData
            {
                ThicknessMm = 200.0, LengthMm = 4000.0, ClearHeightMm = 3000.0
            };

            WallDesignSettings braced = Settings();
            braced.Restraint = WallRestraint.FourEdges;

            var module = new WallDesignModule();
            WallDesignResult plain = module.Design(wall, Settings(), null);
            WallDesignResult stiffened = module.Design(wall, braced, null);

            // beta passe de 1,00 a 0,64 : l'elancement chute et le second ordre s'efface.
            Assert.True(stiffened.Buckling.BucklingLengthMm < plain.Buckling.BucklingLengthMm);
            Assert.True(stiffened.SlendernessRatio < plain.SlendernessRatio);
            Assert.True(plain.SecondOrder.Required);
            Assert.False(stiffened.SecondOrder.Required);
        }

        [Fact]
        public void CantileverWall_DoublesTheBucklingLength()
        {
            var wall = new WallData
            {
                ThicknessMm = 250.0, LengthMm = 5000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            settings.Restraint = WallRestraint.Cantilever;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            Assert.Equal(2.0, result.Buckling.Beta, 3);
            Assert.Equal(6000.0, result.Buckling.BucklingLengthMm, 1);
            Assert.True(result.SecondOrder.Required);
        }

        [Fact]
        public void VerySlenderWall_IsFlagged()
        {
            // h/t = 5 000 / 120 = 41,7 : au-dela de 40, un voile devient penible a betonner.
            var wall = new WallData
            {
                ThicknessMm = 120.0, LengthMm = 4000.0, ClearHeightMm = 5000.0
            };

            WallDesignResult result = new WallDesignModule().Design(wall, Settings(), null);

            Assert.Contains(result.Warnings, w => w.Contains("h/t"));
        }

        [Fact]
        public void OverloadedThinWall_SaysWhatToDo()
        {
            // Voile de 140 mm sur 4,50 m de hauteur libre sous 1 200 kN/m : l'elancement
            // et la charge se combinent, le second ordre explose.
            var wall = new WallData
            {
                ThicknessMm = 140.0, LengthMm = 4000.0, ClearHeightMm = 4500.0
            };
            WallDesignSettings settings = Settings();
            settings.AxialLoadKnPerM = 1200.0;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            // Soit la section est insuffisante et le moteur le dit avec les actions
            // possibles, soit elle passe mais au prix d'un ferraillage tres au-dessus
            // du minimum. Dans les deux cas, rien n'est genere en silence.
            if (!result.IsValid)
            {
                Assert.Contains(result.Warnings,
                    w => w.Contains("SECTION INSUFFISANTE") && w.Contains("epaissir"));
            }
            else
            {
                Assert.True(result.VerticalSteelRequiredMm2PerM
                            > 0.002 * wall.StripAreaMm2 * 1.5);
            }
        }

        [Fact]
        public void InPlaneShear_UsesTheHorizontalBarsAsLinks()
        {
            var wall = new WallData
            {
                Name = "V-contreventement",
                ThicknessMm = 250.0, LengthMm = 5000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            settings.InPlaneShearKn = 900.0;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            CheckResult shear = Find(result, "Effort tranchant dans le plan");
            Assert.Contains("console verticale", shear.Comment);
            Assert.True(shear.Utilization > 0);
        }

        [Fact]
        public void InPlaneBending_IsFlaggedAsInsufficientForSeismicDesign()
        {
            var wall = new WallData
            {
                ThicknessMm = 250.0, LengthMm = 5000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            settings.InPlaneMomentKnm = 2500.0;
            settings.EdgeBars = true;
            settings.EdgeBarCount = 6;
            settings.EdgeBarDiameterMm = 20.0;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            Assert.True(result.EdgeSteelRequiredMm2 > 0);
            Assert.True(result.Reinforcement.HasEdgeBars);
            // Le modele simplifie est annonce comme insuffisant en zone sismique.
            Assert.Contains(result.Warnings,
                w => w.Contains("EN 1998-1") && w.Contains("sismique"));
            // Les barres de rive apparaissent dans le plan de ferraillage.
            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("rive"));
        }

        [Fact]
        public void AxialCompression_RelievesTheEdgeTension()
        {
            var wall = new WallData
            {
                ThicknessMm = 250.0, LengthMm = 5000.0, ClearHeightMm = 3000.0
            };

            WallDesignSettings light = Settings();
            light.AxialLoadKnPerM = 100.0;
            light.InPlaneMomentKnm = 2000.0;

            WallDesignSettings heavy = Settings();
            heavy.AxialLoadKnPerM = 800.0;
            heavy.InPlaneMomentKnm = 2000.0;

            var module = new WallDesignModule();
            WallDesignResult lightResult = module.Design(wall, light, null);
            WallDesignResult heavyResult = module.Design(wall, heavy, null);

            // Sous le meme moment, plus le voile est comprime, moins sa rive est tendue.
            Assert.True(heavyResult.EdgeSteelRequiredMm2 < lightResult.EdgeSteelRequiredMm2);
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
