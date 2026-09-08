using System;
using System.Globalization;

namespace DanCI.Structural.Eurocodes.EC1
{
    /// <summary>Descente de charge d'une volee, par metre carre de projection horizontale.</summary>
    public sealed class StairLoadBreakdown
    {
        /// <summary>Poids propre de la paillasse (kN/m2 de projection horizontale).</summary>
        public double WaistKnM2 { get; set; }

        /// <summary>Poids propre des marches (kN/m2 de projection horizontale).</summary>
        public double StepsKnM2 { get; set; }

        /// <summary>Revetement de marche, applique sur la projection horizontale (kN/m2).</summary>
        public double TreadFinishKnM2 { get; set; }

        /// <summary>Enduit de sous-face, applique sur la surface inclinee (kN/m2 en projection).</summary>
        public double SoffitFinishKnM2 { get; set; }

        /// <summary>Total des charges permanentes (kN/m2 de projection horizontale).</summary>
        public double PermanentKnM2
        {
            get { return WaistKnM2 + StepsKnM2 + TreadFinishKnM2 + SoffitFinishKnM2; }
        }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Actions sur les escaliers selon l'EN 1991-1-1.
    ///
    /// Une volee n'est pas une dalle inclinee de meme epaisseur : elle porte des marches,
    /// et sa paillasse est mesuree perpendiculairement a la pente. Les deux corrections
    /// vont dans le meme sens et se cumulent.
    /// </summary>
    public static class StairActions
    {
        /// <summary>
        /// Poids propre d'une volee, ramene au metre carre de **projection horizontale**,
        /// qui est l'unite dans laquelle on calcule la portee.
        ///
        /// Paillasse : l'epaisseur t est perpendiculaire a la pente, donc la hauteur de
        /// beton au-dessus d'un point du plan vaut t / cos alpha. Prendre gamma x t revient
        /// a oublier ce facteur : 10 % d'erreur a 25 degres, 22 % a 35 degres, toujours du
        /// cote non securitaire.
        ///
        /// Marches : chaque marche est un prisme triangulaire de section R x G / 2 occupant
        /// un giron G en plan, soit gamma x R / 2 par metre carre de projection. Ce terme
        /// est purement geometrique et ne depend pas de la pente.
        /// </summary>
        /// <param name="waistThicknessMm">Epaisseur de paillasse t, perpendiculaire a la pente.</param>
        /// <param name="riserHeightMm">Hauteur de contremarche R.</param>
        /// <param name="slopeCosine">cos alpha de la paillasse.</param>
        /// <param name="unitWeightKnM3">Poids volumique du beton arme.</param>
        /// <param name="treadFinishKnM2">Revetement de marche, sur la projection horizontale.</param>
        /// <param name="soffitFinishKnM2">Enduit de sous-face, sur la surface inclinee reelle.</param>
        public static StairLoadBreakdown FlightSelfWeight(double waistThicknessMm,
                                                          double riserHeightMm,
                                                          double slopeCosine,
                                                          double unitWeightKnM3,
                                                          double treadFinishKnM2,
                                                          double soffitFinishKnM2)
        {
            double cos = slopeCosine > 0.05 ? slopeCosine : 1.0;

            var breakdown = new StairLoadBreakdown
            {
                WaistKnM2 = unitWeightKnM3 * (waistThicknessMm / 1000.0) / cos,
                StepsKnM2 = unitWeightKnM3 * (riserHeightMm / 1000.0) / 2.0,
                TreadFinishKnM2 = treadFinishKnM2,
                SoffitFinishKnM2 = soffitFinishKnM2 / cos
            };

            breakdown.Justification = string.Format(CultureInfo.InvariantCulture,
                "EN 1991-1-1 annexe A : paillasse = gamma t / cos alpha = {0:0.00} x {1:0.000} / " +
                "{2:0.0000} = {3:0.000} kN/m2 ; marches = gamma R / 2 = {0:0.00} x {4:0.000} / 2 = " +
                "{5:0.000} kN/m2 ; revetement de marche {6:0.000} kN/m2 ; sous-face {7:0.000} / " +
                "{2:0.0000} = {8:0.000} kN/m2. Total g = {9:0.000} kN/m2 de projection horizontale.",
                unitWeightKnM3, waistThicknessMm / 1000.0, cos, breakdown.WaistKnM2,
                riserHeightMm / 1000.0, breakdown.StepsKnM2, treadFinishKnM2,
                soffitFinishKnM2, breakdown.SoffitFinishKnM2, breakdown.PermanentKnM2);

            return breakdown;
        }

        /// <summary>
        /// Poids propre d'un palier : une dalle horizontale ordinaire. Ni correction de
        /// pente, ni marches.
        /// </summary>
        public static StairLoadBreakdown LandingSelfWeight(double thicknessMm,
                                                           double unitWeightKnM3,
                                                           double finishKnM2,
                                                           double soffitFinishKnM2)
        {
            var breakdown = new StairLoadBreakdown
            {
                WaistKnM2 = unitWeightKnM3 * (thicknessMm / 1000.0),
                StepsKnM2 = 0.0,
                TreadFinishKnM2 = finishKnM2,
                SoffitFinishKnM2 = soffitFinishKnM2
            };

            breakdown.Justification = string.Format(CultureInfo.InvariantCulture,
                "Palier : dalle horizontale, g = {0:0.00} x {1:0.000} + {2:0.000} + {3:0.000} = " +
                "{4:0.000} kN/m2. Aucune correction de pente, aucune marche.",
                unitWeightKnM3, thicknessMm / 1000.0, finishKnM2, soffitFinishKnM2,
                breakdown.PermanentKnM2);

            return breakdown;
        }

        /// <summary>
        /// Erreur commise en negligeant la pente et les marches, en pourcentage du poids
        /// propre reel. Sert a le dire au lecteur plutot qu'a le laisser deviner.
        /// </summary>
        public static double NaiveSelfWeightErrorPercent(double waistThicknessMm,
                                                         double riserHeightMm,
                                                         double slopeCosine,
                                                         double unitWeightKnM3)
        {
            double cos = slopeCosine > 0.05 ? slopeCosine : 1.0;
            double naive = unitWeightKnM3 * (waistThicknessMm / 1000.0);
            double real = unitWeightKnM3 * (waistThicknessMm / 1000.0) / cos
                          + unitWeightKnM3 * (riserHeightMm / 1000.0) / 2.0;
            return real > 0 ? (real - naive) / real * 100.0 : 0.0;
        }

        /// <summary>
        /// Rappel de l'article 6.3.1(1) : un escalier n'a pas de categorie d'usage propre.
        /// Il prend celle de la zone qu'il dessert, et c'est elle qui fixe q_k et psi_2.
        /// </summary>
        public const string CategoryRule =
            "EN 1991-1-1 6.3.1(1) : un escalier ne possede pas de categorie d'usage propre, " +
            "il prend celle de la zone qu'il dessert. La categorie saisie fixe donc q_k et " +
            "psi_2 : verifiez qu'elle correspond bien aux locaux desservis, et non a " +
            "l'escalier lui-meme.";

        /// <summary>
        /// Rappel de la charge concentree du tableau 6.2. Le moteur ne la combine pas : sur
        /// une volee courante la charge repartie gouverne, mais l'affirmer sans le verifier
        /// serait une hypothese cachee.
        /// </summary>
        public const string ConcentratedLoadReminder =
            "EN 1991-1-1 6.3.1.2(1) : une charge concentree Q_k s'applique aussi aux " +
            "escaliers, sur une surface de 50 x 50 mm, en alternative a la charge repartie. " +
            "Le moteur ne dimensionne que sous la charge repartie, qui gouverne une volee " +
            "courante ; sur une volee courte ou une marche isolee, verifiez Q_k separement.";
    }
}
