using System;
using System.Globalization;

namespace DanCI.Structural.Engine.Stair
{
    /// <summary>Sollicitations d'une volee isostatique chargee par deux bandes de charge.</summary>
    public sealed class StairStaticsResult
    {
        /// <summary>Reaction a l'appui bas, au pied de la volee (kN/m).</summary>
        public double ReactionLowerKnPerM { get; set; }

        /// <summary>Reaction a l'appui haut, au bord du palier (kN/m).</summary>
        public double ReactionUpperKnPerM { get; set; }

        /// <summary>Moment maximal en travee (kN.m/m).</summary>
        public double SpanMomentKnmPerM { get; set; }

        /// <summary>Abscisse du moment maximal depuis l'appui bas (m).</summary>
        public double CriticalPositionM { get; set; }

        /// <summary>Effort tranchant de calcul, la plus grande des deux reactions (kN/m).</summary>
        public double ShearKnPerM
        {
            get { return Math.Max(ReactionLowerKnPerM, ReactionUpperKnPerM); }
        }

        /// <summary>Moment qu'aurait donne la charge de volee appliquee sur toute la portee.</summary>
        public double UniformFlightLoadMomentKnmPerM { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Statique d'une volee isostatique portant longitudinalement, avec la charge de la
    /// paillasse sur une longueur et celle du palier sur le reste.
    ///
    /// Une volee et son palier ne pesent pas la meme chose : la paillasse porte des marches
    /// et sa hauteur de beton est corrigee de la pente, le palier non. Etaler la charge de
    /// la volee sur toute la portee est securitaire mais faux, et l'ecart n'est pas
    /// negligeable. Le calcul exact ne coute rien : c'est une poutre isostatique sous deux
    /// charges reparties, et sa solution est fermee.
    /// </summary>
    public static class StairStatics
    {
        /// <summary>
        /// Resout la travee isostatique.
        /// </summary>
        /// <param name="spanM">Portee de calcul L (m).</param>
        /// <param name="flightLengthM">Longueur chargee par la volee depuis l'appui bas (m).</param>
        /// <param name="flightLoadKnM2">Charge de calcul sur la volee (kN/m2).</param>
        /// <param name="landingLoadKnM2">Charge de calcul sur le palier (kN/m2).</param>
        public static StairStaticsResult Solve(double spanM, double flightLengthM,
                                               double flightLoadKnM2, double landingLoadKnM2)
        {
            var result = new StairStaticsResult();
            if (spanM <= 0)
            {
                result.Justification = "Portee nulle : aucune sollicitation calculee.";
                return result;
            }

            double a = Math.Max(0.0, Math.Min(flightLengthM, spanM));
            double w1 = flightLoadKnM2;
            double w2 = landingLoadKnM2;

            double loadFlight = w1 * a;                 // resultante de la volee (kN/m de largeur)
            double loadLanding = w2 * (spanM - a);      // resultante du palier

            // Moments autour de l'appui haut : R_bas L = W1 (L - a/2) + W2 (L - a)/2
            double reactionLower =
                (loadFlight * (spanM - a / 2.0) + loadLanding * (spanM - a) / 2.0) / spanM;
            double reactionUpper = loadFlight + loadLanding - reactionLower;

            result.ReactionLowerKnPerM = reactionLower;
            result.ReactionUpperKnPerM = reactionUpper;

            // Le moment est maximal la ou l'effort tranchant s'annule.
            double x, moment;
            if (reactionLower <= loadFlight && w1 > 0)
            {
                x = reactionLower / w1;
                moment = reactionLower * x - w1 * x * x / 2.0;
            }
            else if (w2 > 0)
            {
                x = a + (reactionLower - loadFlight) / w2;
                moment = reactionLower * x
                         - loadFlight * (x - a / 2.0)
                         - w2 * (x - a) * (x - a) / 2.0;
            }
            else
            {
                x = a;
                moment = reactionLower * a - loadFlight * a / 2.0;
            }

            result.CriticalPositionM = x;
            result.SpanMomentKnmPerM = Math.Max(moment, 0.0);
            result.UniformFlightLoadMomentKnmPerM = w1 * spanM * spanM / 8.0;

            result.Justification = string.Format(CultureInfo.InvariantCulture,
                "Travee isostatique sous deux charges reparties : volee {0:0.000} kN/m2 sur " +
                "{1:0.000} m puis palier {2:0.000} kN/m2 sur {3:0.000} m. R_bas = {4:0.00} kN/m, " +
                "R_haut = {5:0.00} kN/m, effort tranchant nul a x = {6:0.000} m de l'appui bas, " +
                "M_Ed = {7:0.00} kN.m/m. La charge de volee etalee sur toute la portee aurait " +
                "donne {8:0.00} kN.m/m.",
                w1, a, w2, spanM - a, reactionLower, reactionUpper, x,
                result.SpanMomentKnmPerM, result.UniformFlightLoadMomentKnmPerM);

            return result;
        }
    }
}
