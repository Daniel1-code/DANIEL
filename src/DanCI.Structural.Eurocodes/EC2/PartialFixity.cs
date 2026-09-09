using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>
    /// EN 1992-1-1 art. 9.3.1.2(2) — ENCASTREMENT PARTIEL NON PRIS EN COMPTE DANS L'ANALYSE.
    ///
    /// C'est exactement la situation d'une volee d'escalier. Le moteur la calcule en travee
    /// ISOSTATIQUE, ce qui est securitaire pour la travee, mais la realite ne l'est pas :
    /// une volee coulee en continuite avec son palier ou son plancher est bel et bien
    /// bridee sur ses appuis. Le moment negatif qui s'y developpe n'a pas ete calcule — il
    /// existe quand meme, et sans chapeau il fissure la face superieure.
    ///
    /// L'Eurocode ne demande donc pas de le calculer : il impose un MINIMUM FORFAITAIRE.
    ///
    /// - la nappe superieure doit pouvoir reprendre au moins 25 % du moment maximal de la
    ///   travee adjacente ;
    /// - elle doit s'etendre sur au moins 0,2 fois la portee de cette travee, depuis le nu
    ///   de l'appui.
    ///
    /// CES DEUX VALEURS NE SONT PAS DES REGLAGES. Un parametre de disposition peut allonger
    /// un chapeau ou en augmenter la section ; il ne peut pas descendre sous ce plancher,
    /// pas plus qu'un coefficient d'ancrage ne peut passer sous 1. Le module expose donc
    /// des choix de longueur, et les borne par cet article.
    /// </summary>
    public static class PartialFixity
    {
        /// <summary>Fraction du moment de travee que la nappe superieure doit reprendre.</summary>
        public const double MomentFraction = 0.25;

        /// <summary>Fraction de la portee sur laquelle le chapeau doit s'etendre.</summary>
        public const double ExtentFraction = 0.20;

        /// <summary>
        /// Moment negatif forfaitaire a reprendre sur appui (N.mm), a partir du moment
        /// maximal de la travee adjacente.
        /// </summary>
        public static double RequiredSupportMomentNmm(double spanMomentNmm)
        {
            return MomentFraction * Math.Max(spanMomentNmm, 0.0);
        }

        /// <summary>
        /// Longueur minimale du chapeau depuis le nu de l'appui (mm). C'est un PLANCHER :
        /// une disposition plus longue reste licite, une disposition plus courte non.
        /// </summary>
        public static double MinimumExtentMm(double spanMm)
        {
            return ExtentFraction * Math.Max(spanMm, 0.0);
        }

        /// <summary>
        /// La longueur retenue satisfait-elle l'article ? La tolerance est celle du
        /// faconnage : un arrondi au pas de 50 mm ne doit pas faire echouer un chapeau
        /// qui vaut exactement 0,2 l.
        /// </summary>
        public static bool ExtentIsSufficient(double lengthMm, double spanMm)
        {
            return lengthMm >= MinimumExtentMm(spanMm) - 1.0;
        }

        public static string Justification(double spanMm)
        {
            return string.Format(
                "EN 1992-1-1 art. 9.3.1.2(2) : l'encastrement partiel n'etant pas pris en "
                + "compte dans l'analyse, la nappe superieure reprend au moins {0:0} % du "
                + "moment de travee et s'etend sur au moins 0,2 l = {1:0} mm depuis le nu "
                + "de l'appui.",
                MomentFraction * 100.0, MinimumExtentMm(spanMm));
        }
    }
}
