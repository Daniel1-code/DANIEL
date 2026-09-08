using DanCI.Structural.Engine.Stair;
using Xunit;

namespace DanCI.Structural.Tests.Engine
{
    /// <summary>
    /// Statique d'une volee isostatique sous deux charges reparties.
    ///
    /// Cas de reference : portee 3,540 m, volee chargee a 15,115 kN/m2 sur 2,240 m depuis
    /// l'appui bas, palier a 11,318 kN/m2 sur les 1,300 m restants.
    ///   W1 = 33,858   W2 = 14,713 kN/m
    ///   R_bas = [33,858 x (3,540 - 1,120) + 14,713 x 1,300/2] / 3,540 = 25,847 kN/m
    ///   R_haut = 48,571 - 25,847 = 22,724 kN/m
    ///   Effort tranchant nul a x = 25,847 / 15,115 = 1,710 m, dans la volee.
    ///   M = 25,847 x 1,710 - 15,115 x 1,710^2 / 2 = 22,10 kN.m/m
    /// </summary>
    public class StairStaticsTests
    {
        private const double SpanM = 3.540;
        private const double FlightM = 2.240;
        private const double FlightLoad = 15.1151;
        private const double LandingLoad = 11.3175;

        private static StairStaticsResult Reference()
        {
            return StairStatics.Solve(SpanM, FlightM, FlightLoad, LandingLoad);
        }

        [Fact]
        public void Les_Reactions_Suivent_Le_Calcul_Manuel()
        {
            StairStaticsResult r = Reference();

            Assert.Equal(25.847, r.ReactionLowerKnPerM, 2);
            Assert.Equal(22.724, r.ReactionUpperKnPerM, 2);
        }

        [Fact]
        public void L_Appui_Bas_Est_Le_Plus_Charge()
        {
            // La volee, plus lourde que le palier, est du cote de l'appui bas.
            StairStaticsResult r = Reference();

            Assert.True(r.ReactionLowerKnPerM > r.ReactionUpperKnPerM);
            Assert.Equal(r.ReactionLowerKnPerM, r.ShearKnPerM, 6);
        }

        [Fact]
        public void Le_Moment_Maximal_Tombe_Dans_La_Volee()
        {
            StairStaticsResult r = Reference();

            Assert.Equal(1.710, r.CriticalPositionM, 3);
            Assert.True(r.CriticalPositionM < FlightM);
            Assert.Equal(22.10, r.SpanMomentKnmPerM, 2);
        }

        [Fact]
        public void L_Equilibre_Vertical_Est_Respecte()
        {
            // Controle independant de la formule : la somme des reactions doit valoir la
            // charge totale.
            StairStaticsResult r = Reference();
            double total = FlightLoad * FlightM + LandingLoad * (SpanM - FlightM);

            Assert.Equal(total, r.ReactionLowerKnPerM + r.ReactionUpperKnPerM, 6);
        }

        [Fact]
        public void Deux_Charges_Egales_Redonnent_wL2_Sur_8()
        {
            // Cas degenere : si les deux bandes portent la meme charge, la solution doit
            // retomber exactement sur la travee isostatique classique.
            StairStaticsResult r = StairStatics.Solve(4.0, 2.0, 10.0, 10.0);

            Assert.Equal(20.0, r.SpanMomentKnmPerM, 6);
            Assert.Equal(20.0, r.ReactionLowerKnPerM, 6);
            Assert.Equal(20.0, r.ReactionUpperKnPerM, 6);
            Assert.Equal(2.0, r.CriticalPositionM, 6);
        }

        [Fact]
        public void Une_Volee_Sans_Palier_Redonne_Aussi_wL2_Sur_8()
        {
            // 15,1151 x 2,24^2 / 8 = 9,480 kN.m/m
            StairStaticsResult r = StairStatics.Solve(2.24, 2.24, FlightLoad, 0.0);

            Assert.Equal(9.480, r.SpanMomentKnmPerM, 3);
        }

        [Fact]
        public void Etaler_La_Charge_De_Volee_Surestime_Le_Moment()
        {
            // 15,1151 x 3,540^2 / 8 = 23,68 contre 22,10 : securitaire de 7 %, mais faux.
            // Le moteur rend les deux pour que l'ecart soit visible plutot que suppose.
            StairStaticsResult r = Reference();

            Assert.Equal(23.68, r.UniformFlightLoadMomentKnmPerM, 2);
            Assert.True(r.UniformFlightLoadMomentKnmPerM > r.SpanMomentKnmPerM);
        }

        [Fact]
        public void La_Justification_Nomme_Les_Deux_Charges_Et_Le_Point_Critique()
        {
            StairStaticsResult r = Reference();

            Assert.Contains("deux charges reparties", r.Justification);
            Assert.Contains("effort tranchant nul", r.Justification);
        }

        [Fact]
        public void Une_Portee_Nulle_Ne_Produit_Rien()
        {
            StairStaticsResult r = StairStatics.Solve(0.0, 0.0, 15.0, 11.0);

            Assert.Equal(0.0, r.SpanMomentKnmPerM, 6);
            Assert.Contains("Portee nulle", r.Justification);
        }
    }
}
