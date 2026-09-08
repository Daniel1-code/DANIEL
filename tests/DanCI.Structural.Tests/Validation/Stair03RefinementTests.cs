using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Eurocodes.EC1;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STAIR-03 : les trois hypotheses que le module ne fait plus.
    /// Fiche de validation : docs/validation/STAIR-03.md
    ///
    /// 1. Une geometrie impossible ou une volee non droite ne produit plus de ferraillage.
    /// 2. La charge concentree Q_k de l'art. 6.3.1.2(1) est VERIFIEE, plus seulement citee.
    /// 3. La fissuration de l'art. 7.3.3 est verifiee.
    /// </summary>
    public class Stair03RefinementTests
    {
        private static StairData Flight(double waistMm = 180.0)
        {
            return new StairData
            {
                Name = "V3",
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = waistMm,
                WidthMm = 1200.0,
                LandingThicknessMm = waistMm,
                LandingSpanMm = 1300.0,
                SpanKind = StairSpanKind.AlongFlightWithLanding,
                Shape = StairFlightShape.Straight
            };
        }

        private static StairDesignSettings Settings()
        {
            return new StairDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                TreadFinishKnM2 = 1.0,
                SoffitFinishKnM2 = 0.3,
                VariableLoadKnM2 = 3.0,
                ConcentratedLoadKn = 2.0,
                IncludeSelfWeight = true,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true,
                TopReinforcement = true
            };
        }

        private static StairDesignResult Design(StairData stair, StairDesignSettings settings)
        {
            return new StairDesignModule().Design(stair, settings, null);
        }

        // ------------------------------------------------------------------
        // 1. Le moteur refuse ce qu'il ne sait pas calculer
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Geometrie_Impossible_Ne_Produit_Aucun_Ferraillage()
        {
            // Une contremarche, donc aucun giron, donc aucune portee. Jusqu'a la 3.8.0 le
            // moteur avertissait puis ferraillait quand meme une portee reduite au palier.
            StairData stair = Flight();
            stair.RiserCount = 1;

            StairDesignResult result = Design(stair, Settings());

            Assert.False(result.IsValid);
            Assert.Empty(result.Plan.Groups);
            Assert.Contains(result.Warnings, w => w.Contains("GEOMETRIE INCALCULABLE"));
        }

        [Theory]
        [InlineData(0.0, 280.0, 180.0, 1200.0)]   // pas de contremarche
        [InlineData(170.0, 0.0, 180.0, 1200.0)]   // pas de giron
        [InlineData(170.0, 280.0, 0.0, 1200.0)]   // pas de paillasse
        [InlineData(170.0, 280.0, 180.0, 0.0)]    // pas de largeur
        public void Chaque_Dimension_Nulle_Fait_Refuser_Le_Calcul(double riser, double tread,
                                                                  double waist, double width)
        {
            StairData stair = Flight();
            stair.RiserHeightMm = riser;
            stair.TreadDepthMm = tread;
            stair.WaistThicknessMm = waist;
            stair.WidthMm = width;

            Assert.False(stair.IsCalculable);
            Assert.False(Design(stair, Settings()).IsValid);
        }

        [Theory]
        [InlineData(StairFlightShape.Winder, "BALANCEE")]
        [InlineData(StairFlightShape.Spiral, "HELICOIDALE")]
        public void Une_Volee_Non_Droite_Est_Refusee_Et_Non_Approximee(StairFlightShape shape,
                                                                       string label)
        {
            // C'etait la limite la plus dangereuse du module : une volee balancee traitee
            // comme une volee droite aurait rendu un resultat d'apparence normale et faux.
            StairData stair = Flight();
            stair.Shape = shape;

            StairDesignResult result = Design(stair, Settings());

            Assert.False(result.IsValid);
            Assert.Empty(result.Plan.Groups);
            Assert.Contains(result.Warnings,
                w => w.Contains(label) && w.Contains("NE SAIT PAS LA CALCULER")
                     && w.Contains("torsion"));
        }

        [Fact]
        public void Une_Forme_Indeterminee_Est_Calculee_Mais_L_Hypothese_Est_Annoncee()
        {
            // Ne pas savoir n'est pas la meme chose que savoir que c'est droit. Le moteur
            // poursuit — refuser bloquerait un usage legitime — mais n'endosse pas
            // l'hypothese a la place de l'ingenieur.
            StairData stair = Flight();
            stair.Shape = StairFlightShape.Undetermined;

            StairDesignResult result = Design(stair, Settings());

            Assert.True(result.IsValid);
            Assert.Contains(result.Warnings,
                w => w.Contains("forme de la volee n'a pas pu etre determinee"));
        }

        // ------------------------------------------------------------------
        // 2. La charge concentree est verifiee, plus supposee
        // ------------------------------------------------------------------

        [Fact]
        public void La_Diffusion_Retenue_Est_La_Plus_Defavorable_Raisonnable()
        {
            // 45 degres a travers la seule paillasse : b = 50 + 2 x 180 = 410 mm.
            Assert.Equal(410.0, StairActions.ConcentratedLoadSpreadMm(180.0, 1200.0), 6);

            // Elle ne peut pas depasser la largeur de la volee.
            Assert.Equal(700.0, StairActions.ConcentratedLoadSpreadMm(400.0, 700.0), 6);
        }

        [Fact]
        public void Le_Moment_De_La_Charge_Concentree_Vaut_QL_Sur_4b()
        {
            // 3,00 x 3,540 / (4 x 0,410) = 6,476 kN.m/m
            Assert.Equal(6.476,
                StairActions.ConcentratedLoadMomentKnmPerM(3.0, 3.540, 410.0), 3);
        }

        [Fact]
        public void Sur_La_Volee_De_Reference_La_Charge_Repartie_Gouverne_Et_C_Est_Verifie()
        {
            // Permanentes seules : w1 = 11,800, w2 = 7,830 -> M = 16,844 kN.m/m
            // Q_k : 1,5 x 2,00 x 3,540 / (4 x 0,410)      =  6,476 kN.m/m
            //                                      total  = 23,320  contre 23,883 reparti
            StairDesignResult result = Design(Flight(), Settings());

            Assert.Equal(23.32, result.ConcentratedLoadMomentKnmPerM, 2);
            Assert.False(result.ConcentratedLoadGoverns);
            Assert.Equal(23.88, result.SpanMomentKnmPerM, 2);

            // La marge n'est que de 2,4 % : l'affirmation « la charge repartie gouverne »
            // etait vraie, mais bien moins confortable que ce que le mot laissait croire.
            Assert.InRange(result.ConcentratedLoadMomentKnmPerM / result.SpanMomentKnmPerM,
                           0.95, 1.00);
        }

        [Fact]
        public void Sur_Une_Volee_Courte_La_Charge_Concentree_Gouverne_Et_Est_Retenue()
        {
            // 5 contremarches, 4 girons = 1 120 mm, sans palier, paillasse 150 mm, Q_k = 4 kN
            //   reparti : 15,115 x 1,120^2/8                       = 2,370 kN.m/m
            //   Q_k     : 1,35 x 7,863 x 1,120^2/8 = 1,664
            //             + 1,5 x 4,00 x 1,120 / (4 x 0,350) = 4,800
            //                                                total = 6,464 kN.m/m
            var stair = new StairData
            {
                Name = "V3-courte",
                RiserHeightMm = 170.0, TreadDepthMm = 280.0, RiserCount = 5,
                WaistThicknessMm = 150.0, WidthMm = 1200.0, LandingThicknessMm = 150.0,
                SpanKind = StairSpanKind.AlongFlightOnly,
                Shape = StairFlightShape.Straight
            };
            StairDesignSettings settings = Settings();
            settings.ConcentratedLoadKn = 4.0;

            StairDesignResult result = Design(stair, settings);

            Assert.True(result.ConcentratedLoadGoverns);
            Assert.Equal(6.464, result.ConcentratedLoadMomentKnmPerM, 2);
            // Et c'est bien ce moment qui sert au dimensionnement, pas seulement un message.
            Assert.Equal(6.464, result.SpanMomentKnmPerM, 2);
            Assert.Contains(result.Warnings,
                w => w.Contains("charge concentree Q_k gouverne")
                     && w.Contains("poinconnement local"));
        }

        [Fact]
        public void La_Verification_De_Q_k_Est_Declaree_Sans_Objet_Quand_Elle_N_Est_Pas_Saisie()
        {
            StairDesignSettings settings = Settings();
            settings.ConcentratedLoadKn = 0.0;

            StairDesignResult result = Design(Flight(), settings);
            CheckResult check = Find(result, "Situation alternative a charge concentree");

            Assert.Equal(CheckStatus.NotApplicable, check.Status);
            Assert.Equal(0.0, check.Utilization, 6);
            Assert.Contains("c'est sa valeur qui manque", check.Comment);
        }

        [Fact]
        public void La_Regle_De_Diffusion_Est_Annoncee_Comme_Hors_Eurocode()
        {
            StairDesignResult result = Design(Flight(), Settings());

            Assert.Contains(result.Notes,
                n => n.Contains("Aucun article de l'EN 1992-1-1 ne fixe la largeur de diffusion")
                     && n.Contains("cote de la securite"));
        }

        // ------------------------------------------------------------------
        // 3. La fissuration est verifiee
        // ------------------------------------------------------------------

        [Fact]
        public void La_Fissuration_Est_Desormais_Verifiee()
        {
            StairDesignResult result = Design(Flight(), Settings());
            CheckResult check = Find(result, "Maitrise de la fissuration");

            Assert.NotNull(result.Cracking);
            Assert.Equal("EN 1992-1-1:2004", check.Code);
            Assert.Equal("7.3.3 (2)", check.Clause);
            Assert.Equal(CheckStatus.Pass, check.Status);
        }

        [Fact]
        public void La_Contrainte_D_Acier_Est_Annoncee_Comme_Une_Estimation()
        {
            StairDesignResult result = Design(Flight(), Settings());
            CheckResult check = Find(result, "Maitrise de la fissuration");

            Assert.Contains("estimation, pas", check.Comment);
            Assert.Contains("l'omettre revenait a le supposer", check.Comment);
        }

        [Fact]
        public void Une_Ouverture_De_Fissure_Imposee_Est_Prise_En_Compte()
        {
            StairDesignSettings strict = Settings();
            strict.CrackWidthLimitMm = 0.20;

            StairDesignResult loose = Design(Flight(), Settings());
            StairDesignResult tight = Design(Flight(), strict);

            // Une exigence plus severe reduit le diametre et l'espacement admissibles.
            Assert.True(tight.Cracking.MaxSpacingMm <= loose.Cracking.MaxSpacingMm);
            Assert.True(tight.Cracking.MaxBarDiameterMm <= loose.Cracking.MaxBarDiameterMm);
        }

        private static CheckResult Find(StairDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
