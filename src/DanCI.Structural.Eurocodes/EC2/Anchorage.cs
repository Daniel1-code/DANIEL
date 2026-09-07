using System;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Longueur d'ancrage et de recouvrement calculees, avec leur justification.</summary>
    public sealed class AnchorageResult
    {
        /// <summary>Contrainte ultime d'adherence f_bd (MPa).</summary>
        public double BondStressMPa { get; set; }

        /// <summary>Longueur d'ancrage de reference l_b,rqd (mm).</summary>
        public double RequiredAnchorageMm { get; set; }

        /// <summary>Longueur d'ancrage de calcul l_bd (mm).</summary>
        public double DesignAnchorageMm { get; set; }

        /// <summary>Longueur de recouvrement l_0 (mm).</summary>
        public double LapLengthMm { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Ancrages et recouvrements selon l'EN 1992-1-1:2004, articles 8.4 et 8.7.
    /// </summary>
    public static class Anchorage
    {
        /// <summary>
        /// Calcule l'ancrage et le recouvrement d'une barre haute adherence pleinement
        /// sollicitee, en bonnes conditions d'adherence.
        /// </summary>
        /// <param name="barDiameterMm">Diametre nominal de la barre.</param>
        /// <param name="properties">Proprietes des materiaux.</param>
        /// <param name="annex">Parametres nationaux.</param>
        /// <param name="goodBondConditions">Conditions d'adherence au sens de 8.4.2(2).</param>
        public static AnchorageResult Compute(double barDiameterMm, ConcreteProperties properties,
                                              INationalAnnex annex, bool goodBondConditions = true)
        {
            // 8.4.2 : f_bd = 2,25 eta1 eta2 f_ctd
            double eta1 = goodBondConditions ? 1.0 : 0.7;
            double eta2 = barDiameterMm <= 32.0 ? 1.0 : (132.0 - barDiameterMm) / 100.0;
            double fbd = 2.25 * eta1 * eta2 * properties.Fctd;

            // 8.4.3 : l_b,rqd = (phi/4) (sigma_sd / f_bd), barre supposee pleinement sollicitee
            double sigmaSd = properties.Fyd;
            double lbRqd = barDiameterMm / 4.0 * (sigmaSd / fbd);

            // 8.4.4 : l_bd = alpha1..alpha5 l_b,rqd >= l_b,min, coefficients pris egaux a 1
            // (barre droite, enrobage et armatures transversales non valorises : securitaire).
            double lbMin = Math.Max(0.3 * lbRqd, Math.Max(10.0 * barDiameterMm, 100.0));
            double lbd = Math.Max(lbRqd, lbMin);

            // 8.7.3 : l_0 = alpha_6 l_b,rqd >= l_0,min
            double alpha6 = annex.LapCoefficientAlpha6;
            double lap = alpha6 * lbRqd;
            double lapMin = Math.Max(0.3 * alpha6 * lbRqd, Math.Max(15.0 * barDiameterMm, 200.0));
            lap = Math.Max(lap, lapMin);

            return new AnchorageResult
            {
                BondStressMPa = fbd,
                RequiredAnchorageMm = lbRqd,
                DesignAnchorageMm = lbd,
                LapLengthMm = lap,
                Justification = string.Format(
                    "EC2 8.4 et 8.7 : f_ctm = {0:0.00} MPa, f_bd = {1:0.00} MPa, " +
                    "l_b,rqd = {2:0} mm, l_bd = {3:0} mm, l_0 = alpha6 l_b,rqd = {4:0.0} x {2:0} = {5:0} mm " +
                    "(soit {6:0} phi)",
                    properties.Fctm, fbd, lbRqd, lbd, alpha6, lap, lap / barDiameterMm)
            };
        }
    }
}
