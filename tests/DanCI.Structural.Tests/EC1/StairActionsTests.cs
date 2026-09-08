using DanCI.Structural.Core.Elements;
using DanCI.Structural.Eurocodes.EC1;
using Xunit;

namespace DanCI.Structural.Tests.EC1
{
    /// <summary>
    /// Descente de charge d'une volee, EN 1991-1-1.
    ///
    /// Volee de reference : 9 contremarches de 170 mm, giron 280 mm, paillasse 150 mm,
    /// beton arme a 25 kN/m3, revetement de marche 1,00 kN/m2, sous-face 0,30 kN/m2.
    ///   cos alpha = 280 / racine(280^2 + 170^2) = 0,85479
    /// </summary>
    public class StairActionsTests
    {
        private const double Cos = 0.8547875152;

        [Fact]
        public void La_Paillasse_Est_Corrigee_De_La_Pente()
        {
            // gamma t / cos alpha = 25 x 0,150 / 0,85479 = 4,387 kN/m2
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(150.0, 170.0, Cos, 25.0, 1.0, 0.3);

            Assert.Equal(4.387, load.WaistKnM2, 3);
        }

        [Fact]
        public void Les_Marches_Valent_Gamma_R_Sur_Deux()
        {
            // Chaque marche est un prisme triangulaire R x G / 2 occupant un giron G :
            // 25 x 0,170 / 2 = 2,125 kN/m2, quelle que soit la pente.
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(150.0, 170.0, Cos, 25.0, 1.0, 0.3);

            Assert.Equal(2.125, load.StepsKnM2, 3);
        }

        [Fact]
        public void Le_Terme_Des_Marches_Ne_Depend_Pas_De_La_Pente()
        {
            // Le giron disparait de gamma R G / 2 / G : le terme est purement geometrique.
            StairLoadBreakdown raide =
                StairActions.FlightSelfWeight(150.0, 170.0, 0.80, 25.0, 0.0, 0.0);
            StairLoadBreakdown douce =
                StairActions.FlightSelfWeight(150.0, 170.0, 0.95, 25.0, 0.0, 0.0);

            Assert.Equal(raide.StepsKnM2, douce.StepsKnM2, 6);
            // La paillasse, elle, en depend.
            Assert.True(raide.WaistKnM2 > douce.WaistKnM2);
        }

        [Fact]
        public void L_Enduit_De_Sous_Face_Suit_La_Surface_Inclinee()
        {
            // 0,30 / 0,85479 = 0,351 kN/m2 : la sous-face est plus grande que sa projection.
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(150.0, 170.0, Cos, 25.0, 1.0, 0.3);

            Assert.Equal(0.351, load.SoffitFinishKnM2, 3);
        }

        [Fact]
        public void Le_Revetement_De_Marche_Reste_En_Projection_Horizontale()
        {
            // Il est pose sur les marches, dont la projection est justement le plan.
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(150.0, 170.0, Cos, 25.0, 1.0, 0.3);

            Assert.Equal(1.0, load.TreadFinishKnM2, 6);
        }

        [Fact]
        public void Le_Total_Permanent_Vaut_7_863_kN_m2()
        {
            // 4,387 + 2,125 + 1,000 + 0,351 = 7,863 kN/m2 de projection horizontale.
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(150.0, 170.0, Cos, 25.0, 1.0, 0.3);

            Assert.Equal(7.863, load.PermanentKnM2, 3);
        }

        [Fact]
        public void Une_Volee_Plate_Redonne_Une_Dalle()
        {
            // Sans pente ni contremarche, il ne reste que gamma t : le cas degenere doit
            // redonner exactement la dalle, sinon la formule est fausse quelque part.
            StairLoadBreakdown load =
                StairActions.FlightSelfWeight(200.0, 0.0, 1.0, 25.0, 0.0, 0.0);

            Assert.Equal(5.0, load.WaistKnM2, 6);
            Assert.Equal(0.0, load.StepsKnM2, 6);
        }

        [Fact]
        public void Le_Palier_N_A_Ni_Pente_Ni_Marches()
        {
            // 25 x 0,150 + 1,00 + 0,30 = 5,05 kN/m2
            StairLoadBreakdown load = StairActions.LandingSelfWeight(150.0, 25.0, 1.0, 0.3);

            Assert.Equal(3.75, load.WaistKnM2, 6);
            Assert.Equal(0.0, load.StepsKnM2, 6);
            Assert.Equal(5.05, load.PermanentKnM2, 6);
        }

        [Fact]
        public void Negliger_La_Pente_Et_Les_Marches_Coute_42_Pourcent()
        {
            // Naif : 25 x 0,150 = 3,750 kN/m2
            // Reel : 4,387 + 2,125 = 6,512 kN/m2
            // Ecart : 2,762 / 6,512 = 42,4 % du poids propre reel, du cote non securitaire.
            double error = StairActions.NaiveSelfWeightErrorPercent(150.0, 170.0, Cos, 25.0);

            Assert.Equal(42.41, error, 2);
        }

        [Fact]
        public void Les_Rappels_Normatifs_Citent_Leur_Article()
        {
            // Un escalier n'a pas de categorie propre : la regle doit le dire, et dire
            // aussi que la charge concentree n'est pas couverte par le moteur.
            Assert.Contains("6.3.1(1)", StairActions.CategoryRule);
            Assert.Contains("zone qu'il dessert", StairActions.CategoryRule);
            Assert.Contains("6.3.1.2(1)", StairActions.ConcentratedLoadReminder);
            Assert.Contains("Q_k", StairActions.ConcentratedLoadReminder);
            // Q_k s'applique en ALTERNATIVE a q_k, et sa valeur est saisie, jamais devinee.
            Assert.Contains("ALTERNATIVE", StairActions.ConcentratedLoadReminder);
            Assert.Contains("saisie, jamais", StairActions.ConcentratedLoadReminder);
        }

        // ------------------------------------------------------------------
        // Geometrie de la volee
        // ------------------------------------------------------------------

        private static StairData Flight()
        {
            return new StairData
            {
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = 150.0,
                WidthMm = 1200.0,
                LandingThicknessMm = 150.0,
                LandingSpanMm = 1300.0,
                SpanKind = StairSpanKind.AlongFlightWithLanding
            };
        }

        [Fact]
        public void Neuf_Contremarches_Ne_Font_Que_Huit_Girons()
        {
            // La derniere contremarche debouche sur le palier : compter 9 girons
            // allongerait la volee de 280 mm et fausserait la portee comme la pente.
            StairData stair = Flight();

            Assert.Equal(1530.0, stair.TotalRiseMm, 6);
            Assert.Equal(2240.0, stair.TotalGoingMm, 6);
        }

        [Fact]
        public void La_Pente_Sort_De_La_Geometrie_De_La_Marche()
        {
            StairData stair = Flight();

            Assert.Equal(0.85479, stair.SlopeCosine, 5);
            Assert.Equal(31.26, stair.SlopeAngleDegrees, 2);
        }

        [Fact]
        public void La_Portee_Depend_Du_Mode_D_Appui()
        {
            StairData stair = Flight();

            // Le palier participe : 2 240 + 1 300
            Assert.Equal(3540.0, stair.SpanMm, 6);

            stair.SpanKind = StairSpanKind.AlongFlightOnly;
            Assert.Equal(2240.0, stair.SpanMm, 6);

            stair.SpanKind = StairSpanKind.TransverseBetweenWalls;
            Assert.Equal(1200.0, stair.SpanMm, 6);
        }

        [Fact]
        public void La_Formule_De_Blondel_Est_Rendue_Sans_Etre_Normative()
        {
            // 2 x 170 + 280 = 620 mm, dans la plage confortable de 600 a 650 mm.
            Assert.Equal(620.0, Flight().BlondelValueMm, 6);
        }
    }
}
