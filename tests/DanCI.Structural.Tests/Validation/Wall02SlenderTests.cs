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
        public void CloseLateralRestraint_ReducesTheSecondOrderEffect()
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

            // beta = 1 / (1 + (3 000/4 000)^2) = 0,640 -> l_0 = 1 920 mm, lambda = 33,3
            // contre 52,0 sans retour. La limite vaut 30,3 : le second ordre reste
            // obligatoire dans les deux cas, mais il est bien plus faible.
            Assert.Equal(0.64, stiffened.Buckling.Beta, 3);
            Assert.InRange(stiffened.SlendernessRatio, 32.5, 34.0);
            Assert.True(plain.SecondOrder.Required);
            Assert.True(stiffened.SecondOrder.Required);

            Assert.True(stiffened.SecondOrder.SecondOrderEccentricityMm
                        < plain.SecondOrder.SecondOrderEccentricityMm);
            Assert.True(stiffened.DesignOutOfPlaneMomentKnmPerM
                        < plain.DesignOutOfPlaneMomentKnmPerM);
        }

        [Fact]
        public void ThickBracedWall_EscapesTheSecondOrderAltogether()
        {
            // 300 mm entre quatre rives distantes de 4,00 m, hauteur libre 2,50 m :
            //   beta = 1 / (1 + (2 500/4 000)^2) = 0,719 -> l_0 = 1 798 mm
            //   i = 300/sqrt(12) = 86,6 -> lambda = 20,8
            //   n = 0,080 -> lambda_lim = 37,2   ->  20,8 < 37,2, second ordre neglige
            var wall = new WallData
            {
                ThicknessMm = 300.0, LengthMm = 4000.0, ClearHeightMm = 2500.0
            };
            WallDesignSettings settings = Settings();
            settings.Restraint = WallRestraint.FourEdges;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            Assert.InRange(result.SlendernessRatio, 20.0, 21.5);
            Assert.False(result.SecondOrder.Required);
            Assert.Contains("5.8.3.1", result.SecondOrder.Justification);
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

            // Regression : A_sw/s sort en mm2 PAR MILLIMETRE, les nappes sont en mm2 par
            // METRE. Une version anterieure comparait les deux directement, et la
            // verification passait toujours a un facteur 1 000 pres. Les deux grandeurs
            // doivent etre du meme ordre.
            Assert.Equal("mm2/m", shear.Demand.Unit);
            Assert.Equal("mm2/m", shear.Resistance.Unit);
            Assert.InRange(shear.Demand.Value, 50.0, 20000.0);
        }

        [Fact]
        public void InPlaneShear_ComparesLikeWithLike()
        {
            // Un effort tranchant assez fort pour que les aciers horizontaux soient
            // reellement sollicites : le taux doit etre significatif, pas ecrase par une
            // erreur d'unite.
            var wall = new WallData
            {
                ThicknessMm = 200.0, LengthMm = 4000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            settings.InPlaneShearKn = 1400.0;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            CheckResult shear = Find(result, "Effort tranchant dans le plan");
            // Requis et fourni sont tous deux des mm2/m : leur rapport a un sens.
            Assert.True(shear.Demand.Value > 100.0,
                        "La demande doit etre exprimee en mm2/m, pas en mm2/mm.");
            Assert.True(shear.Utilization > 0.05,
                        "Un effort de 1 400 kN ne peut pas donner un taux quasi nul.");
        }

        [Fact]
        public void InPlaneBending_IsFlaggedAsInsufficientForSeismicDesign()
        {
            var wall = new WallData
            {
                ThicknessMm = 250.0, LengthMm = 5000.0, ClearHeightMm = 3000.0
            };
            WallDesignSettings settings = Settings();
            // Le voile est comprime a 2 000 kN au total ; il faut depasser
            // M = N z / 2 = 2 000 x 4,00 / 2 = 4 000 kN.m pour que la rive se tende.
            settings.InPlaneMomentKnm = 6000.0;
            settings.EdgeBars = true;
            settings.EdgeBarCount = 6;
            settings.EdgeBarDiameterMm = 20.0;

            WallDesignResult result = new WallDesignModule().Design(wall, settings, null);

            // (6 000e6 / 4 000 - 2 000 000 / 2) / 434,78 = 1 150 mm2
            Assert.InRange(result.EdgeSteelRequiredMm2, 1050.0, 1250.0);
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

            // Moment choisi pour que la rive soit tendue dans les DEUX cas : comparer
            // 1 750 kN a 1 000 kN a du sens, comparer quelque chose a zero n'en aurait pas.
            WallDesignSettings light = Settings();
            light.AxialLoadKnPerM = 100.0;
            light.InPlaneMomentKnm = 8000.0;

            WallDesignSettings heavy = Settings();
            heavy.AxialLoadKnPerM = 400.0;
            heavy.InPlaneMomentKnm = 8000.0;

            var module = new WallDesignModule();
            WallDesignResult lightResult = module.Design(wall, light, null);
            WallDesignResult heavyResult = module.Design(wall, heavy, null);

            // Sous le meme moment, plus le voile est comprime, moins sa rive est tendue.
            //   leger : (8 000e6/4 000 - 500 000/2) / 434,78 = 4 025 mm2
            //   lourd : (8 000e6/4 000 - 2 000 000/2) / 434,78 = 2 300 mm2
            Assert.True(lightResult.EdgeSteelRequiredMm2 > 0);
            Assert.True(heavyResult.EdgeSteelRequiredMm2 > 0);
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
