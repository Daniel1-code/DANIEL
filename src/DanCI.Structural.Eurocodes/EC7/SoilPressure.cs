using System;

namespace DanCI.Structural.Eurocodes.EC7
{
    /// <summary>Distribution des contraintes sous une semelle rectangulaire.</summary>
    public sealed class SoilPressureResult
    {
        /// <summary>Excentricite suivant X (mm).</summary>
        public double EccentricityXMm { get; set; }
        /// <summary>Excentricite suivant Y (mm).</summary>
        public double EccentricityYMm { get; set; }

        /// <summary>La resultante reste-t-elle dans le noyau central (aucun soulevement) ?</summary>
        public bool WithinCore { get; set; }

        /// <summary>Contrainte maximale de la distribution lineaire (kPa).</summary>
        public double MaxPressureKpa { get; set; }
        /// <summary>Contrainte minimale de la distribution lineaire (kPa) ; negative = traction.</summary>
        public double MinPressureKpa { get; set; }

        /// <summary>Largeur effective B' = B - 2 e_x (mm), methode de l'aire effective.</summary>
        public double EffectiveWidthMm { get; set; }
        /// <summary>Longueur effective L' = L - 2 e_y (mm).</summary>
        public double EffectiveLengthMm { get; set; }

        /// <summary>Contrainte uniforme sur l'aire effective (kPa), EN 1997-1 annexe D.</summary>
        public double EffectivePressureKpa { get; set; }

        /// <summary>Contrainte moyenne nette utilisee pour le calcul structurel (kPa).</summary>
        public double NetPressureKpa { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Contraintes sous une semelle rectangulaire chargee de facon excentree.
    ///
    /// Deux modeles cohabitent, comme le veut la pratique :
    /// - la **distribution lineaire** (trapezoidale) sert a verifier qu'aucune traction
    ///   n'apparait sous la semelle et a evaluer la contrainte de bord ;
    /// - l'**aire effective** de Meyerhof, retenue par l'EN 1997-1 annexe D, sert a la
    ///   verification de la capacite portante : la charge est supposee uniformement
    ///   repartie sur B' x L' = (B - 2 e_x)(L - 2 e_y).
    /// </summary>
    public static class SoilPressure
    {
        /// <summary>
        /// Calcule la distribution des contraintes.
        /// </summary>
        /// <param name="widthXMm">Dimension de la semelle suivant X (B).</param>
        /// <param name="widthYMm">Dimension suivant Y (L).</param>
        /// <param name="totalVerticalLoadN">Charge verticale totale a la base, poids propre inclus (N).</param>
        /// <param name="momentAboutYNmm">Moment autour de Y, qui excentre la charge suivant X (N.mm).</param>
        /// <param name="momentAboutXNmm">Moment autour de X, qui excentre la charge suivant Y (N.mm).</param>
        /// <param name="netColumnLoadN">Charge du poteau seule, pour la contrainte nette structurelle (N).</param>
        public static SoilPressureResult Compute(double widthXMm, double widthYMm,
                                                 double totalVerticalLoadN,
                                                 double momentAboutYNmm, double momentAboutXNmm,
                                                 double netColumnLoadN)
        {
            var result = new SoilPressureResult();
            double area = widthXMm * widthYMm;
            if (area <= 0 || totalVerticalLoadN <= 0)
            {
                result.Justification = "Charge ou dimensions nulles : aucune contrainte calculee.";
                return result;
            }

            double ex = Math.Abs(momentAboutYNmm) / totalVerticalLoadN;
            double ey = Math.Abs(momentAboutXNmm) / totalVerticalLoadN;
            result.EccentricityXMm = ex;
            result.EccentricityYMm = ey;

            // Noyau central : e <= B/6 dans chaque direction.
            result.WithinCore = ex <= widthXMm / 6.0 + 1e-9 && ey <= widthYMm / 6.0 + 1e-9;

            // Distribution lineaire, en kPa (1 N/mm2 = 1 000 kPa).
            double average = totalVerticalLoadN / area;
            double variation = 6.0 * ex / widthXMm + 6.0 * ey / widthYMm;
            result.MaxPressureKpa = ToKpa(average * (1.0 + variation));
            result.MinPressureKpa = ToKpa(average * (1.0 - variation));

            // Aire effective, EN 1997-1 annexe D.
            double effectiveWidth = Math.Max(widthXMm - 2.0 * ex, 0.0);
            double effectiveLength = Math.Max(widthYMm - 2.0 * ey, 0.0);
            result.EffectiveWidthMm = effectiveWidth;
            result.EffectiveLengthMm = effectiveLength;
            double effectiveArea = effectiveWidth * effectiveLength;
            result.EffectivePressureKpa = effectiveArea > 0
                ? ToKpa(totalVerticalLoadN / effectiveArea) : double.PositiveInfinity;

            // Contrainte nette : le poids propre de la semelle est equilibre par le sol
            // qui la porte, il ne sollicite donc pas la structure.
            result.NetPressureKpa = ToKpa(netColumnLoadN / area);

            result.Justification = string.Format(
                "e_x = {0:0} mm, e_y = {1:0} mm ; noyau central {2} ; " +
                "distribution lineaire sigma = {3:0} / {4:0} kPa ; " +
                "aire effective B' x L' = {5:0} x {6:0} mm -> sigma' = {7:0} kPa " +
                "(EN 1997-1 annexe D) ; contrainte nette structurelle {8:0} kPa",
                ex, ey, result.WithinCore ? "respecte" : "DEPASSE",
                result.MaxPressureKpa, result.MinPressureKpa,
                effectiveWidth, effectiveLength, result.EffectivePressureKpa,
                result.NetPressureKpa);
            return result;
        }

        private static double ToKpa(double newtonsPerSquareMillimetre)
        {
            return newtonsPerSquareMillimetre * 1000.0;
        }
    }
}
