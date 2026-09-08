using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Conditions de maintien d'un voile sur ses rives, article 12.6.5.1.</summary>
    public enum WallRestraint
    {
        /// <summary>Maintenu en tete et en pied seulement (cas courant).</summary>
        TopAndBottom,

        /// <summary>Maintenu en tete, en pied et sur une rive verticale (retour de voile).</summary>
        ThreeEdges,

        /// <summary>Maintenu sur ses quatre rives (voile entre deux retours).</summary>
        FourEdges,

        /// <summary>Console verticale, libre en tete (mur de soutenement, acrotere).</summary>
        Cantilever
    }

    /// <summary>Longueur de flambement d'un voile et justification du coefficient retenu.</summary>
    public sealed class WallBucklingResult
    {
        /// <summary>Coefficient beta tel que l_0 = beta l_w.</summary>
        public double Beta { get; set; }

        /// <summary>Longueur de flambement l_0 (mm).</summary>
        public double BucklingLengthMm { get; set; }

        /// <summary>
        /// Le voile est-il assez long pour que le maintien latéral compte encore ?
        /// Au-dela de certaines proportions, une rive verticale ne raidit plus rien.
        /// </summary>
        public bool LateralRestraintEffective { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Longueur de flambement d'un voile, article 12.6.5.1, que l'article 5.8.3.2(6)
    /// autorise a reprendre pour les voiles en beton arme.
    ///
    /// Le point que l'on oublie souvent : un maintien sur une rive verticale ne raidit le
    /// voile que s'il est **proche**. Un voile de 8 m de long tenu a une seule extremite
    /// se comporte, en son milieu, exactement comme un voile tenu seulement en tete et en
    /// pied. L'Eurocode le traduit par les conditions b &gt;= l_w (trois rives) et
    /// b &gt;= l_w (quatre rives) : en deca, beta est ramene a 1,0.
    /// </summary>
    public static class WallEffectiveLength
    {
        /// <summary>
        /// Calcule l_0 = beta l_w.
        /// </summary>
        /// <param name="clearHeightMm">Hauteur libre du voile l_w.</param>
        /// <param name="lengthBetweenRestraintsMm">
        /// Distance b entre les rives verticales maintenues ; ignoree pour les deux cas
        /// sans maintien lateral.
        /// </param>
        /// <param name="restraint">Conditions de maintien.</param>
        public static WallBucklingResult Compute(double clearHeightMm,
                                                 double lengthBetweenRestraintsMm,
                                                 WallRestraint restraint)
        {
            var result = new WallBucklingResult { LateralRestraintEffective = true };
            double height = Math.Max(clearHeightMm, 0.0);
            double b = Math.Max(lengthBetweenRestraintsMm, 0.0);

            switch (restraint)
            {
                case WallRestraint.Cantilever:
                    result.Beta = 2.0;
                    result.Justification =
                        "EC2 12.6.5.1 : voile en console, libre en tete -> beta = 2,00.";
                    break;

                case WallRestraint.ThreeEdges:
                    if (b >= height && b > 0)
                    {
                        // beta = 1 / (1 + (l_w / 3b)^2)
                        result.Beta = 1.0 / (1.0 + Math.Pow(height / (3.0 * b), 2.0));
                        result.Justification = string.Format(
                            "EC2 12.6.5.1 : maintien sur trois rives, b = {0:0} mm >= l_w = " +
                            "{1:0} mm -> beta = 1 / (1 + (l_w/3b)^2) = {2:0.000}.",
                            b, height, result.Beta);
                    }
                    else
                    {
                        // b < l_w : l'article ramene beta a b / l_w, plafonne a 1.
                        result.Beta = height > 0 ? Math.Min(b / height, 1.0) : 1.0;
                        result.Justification = string.Format(
                            "EC2 12.6.5.1 : maintien sur trois rives mais b = {0:0} mm < " +
                            "l_w = {1:0} mm -> beta = b / l_w = {2:0.000}.",
                            b, height, result.Beta);
                    }
                    break;

                case WallRestraint.FourEdges:
                    if (b >= height && b > 0)
                    {
                        // beta = 1 / (1 + (l_w / b)^2)
                        result.Beta = 1.0 / (1.0 + Math.Pow(height / b, 2.0));
                        result.Justification = string.Format(
                            "EC2 12.6.5.1 : maintien sur quatre rives, b = {0:0} mm >= l_w = " +
                            "{1:0} mm -> beta = 1 / (1 + (l_w/b)^2) = {2:0.000}.",
                            b, height, result.Beta);
                    }
                    else
                    {
                        result.Beta = height > 0 ? Math.Min(b / (2.0 * height), 1.0) : 1.0;
                        result.Justification = string.Format(
                            "EC2 12.6.5.1 : maintien sur quatre rives mais b = {0:0} mm < " +
                            "l_w = {1:0} mm -> beta = b / (2 l_w) = {2:0.000}.",
                            b, height, result.Beta);
                    }
                    break;

                default:
                    result.Beta = 1.0;
                    result.Justification =
                        "EC2 12.6.5.1 : maintien en tete et en pied seulement -> beta = 1,00.";
                    break;
            }

            // Un maintien lateral trop eloigne ne raidit plus la partie centrale du voile.
            if ((restraint == WallRestraint.ThreeEdges || restraint == WallRestraint.FourEdges)
                && height > 0 && b > 3.0 * height)
            {
                result.LateralRestraintEffective = false;
                result.Justification += string.Format(
                    " Attention : b = {0:0} mm depasse trois fois la hauteur libre. En partie " +
                    "courante, le voile se comporte comme s'il n'etait tenu qu'en tete et en " +
                    "pied ; le coefficient calcule n'est representatif que pres des rives.",
                    b);
            }

            result.BucklingLengthMm = result.Beta * height;
            return result;
        }
    }
}
