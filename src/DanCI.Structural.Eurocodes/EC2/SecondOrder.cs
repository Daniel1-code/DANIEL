using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Resultat de l'analyse d'elancement et du second ordre pour une direction.</summary>
    public sealed class SecondOrderResult
    {
        public double Slenderness { get; set; }
        public double SlendernessLimit { get; set; }
        public bool Required { get; set; }
        /// <summary>Excentricite du second ordre e_2 (mm).</summary>
        public double SecondOrderEccentricityMm { get; set; }
        /// <summary>Moment du second ordre M_2 (N.mm).</summary>
        public double SecondOrderMomentNmm { get; set; }
        public string Justification { get; set; }
    }

    /// <summary>
    /// Elancement et effets du second ordre selon l'EN 1992-1-1:2004, articles 5.8.3
    /// (elancement limite) et 5.8.8 (methode de la courbure nominale).
    /// </summary>
    public static class SecondOrder
    {
        /// <summary>
        /// Elancement lambda = l0 / i, avec i = h / sqrt(12) pour une section rectangulaire
        /// ou circulaire flechie autour d'un axe principal.
        /// </summary>
        public static double Slenderness(double bucklingLengthMm, double sectionHeightMm)
        {
            double radiusOfGyration = sectionHeightMm / Math.Sqrt(12.0);
            return bucklingLengthMm / radiusOfGyration;
        }

        /// <summary>
        /// Elancement limite lambda_lim = 20 A B C / sqrt(n) (5.8.3.1).
        /// A depend du fluage effectif, B du taux mecanique d'armature, C du rapport des
        /// moments d'extremite ; C = 0,7 est la valeur retenue quand ce rapport est inconnu.
        /// </summary>
        public static double SlendernessLimit(double relativeAxialForce, double mechanicalSteelRatio,
                                              double effectiveCreepCoefficient, double momentRatioC = 0.7)
        {
            double factorA = 1.0 / (1.0 + 0.2 * effectiveCreepCoefficient);
            double factorB = Math.Sqrt(1.0 + 2.0 * mechanicalSteelRatio);
            return 20.0 * factorA * factorB * momentRatioC / Math.Sqrt(Math.Max(relativeAxialForce, 1e-6));
        }

        /// <summary>
        /// Moment du second ordre par la methode de la courbure nominale (5.8.8.2 et 5.8.8.3).
        /// </summary>
        /// <param name="axialForceN">Effort normal de calcul N_Ed (N).</param>
        /// <param name="bucklingLengthMm">Longueur efficace l_0 (mm).</param>
        /// <param name="effectiveDepthMm">Hauteur utile d (mm).</param>
        /// <param name="slenderness">Elancement lambda de la direction consideree.</param>
        /// <param name="relativeAxialForce">n = N_Ed / (A_c f_cd).</param>
        /// <param name="mechanicalSteelRatio">omega = A_s f_yd / (A_c f_cd).</param>
        /// <param name="steelYieldStrain">eps_yd = f_yd / E_s.</param>
        /// <param name="concreteStrengthMPa">f_ck, pour le coefficient beta.</param>
        /// <param name="effectiveCreepCoefficient">phi_ef.</param>
        public static SecondOrderResult NominalCurvature(
            double axialForceN, double bucklingLengthMm, double effectiveDepthMm,
            double slenderness, double relativeAxialForce, double mechanicalSteelRatio,
            double steelYieldStrain, double concreteStrengthMPa, double effectiveCreepCoefficient)
        {
            // 5.8.8.3(1) : 1/r0 = eps_yd / (0,45 d)
            double baseCurvature = steelYieldStrain / (0.45 * effectiveDepthMm);

            // 5.8.8.3(3) : Kr = (nu - n) / (nu - nbal), nu = 1 + omega, nbal = 0,4
            double nu = 1.0 + mechanicalSteelRatio;
            double kr = (nu - relativeAxialForce) / (nu - 0.4);
            if (kr > 1.0) kr = 1.0;
            if (kr < 0.0) kr = 0.0;

            // 5.8.8.3(4) : Kphi = 1 + beta phi_ef >= 1, beta = 0,35 + fck/200 - lambda/150
            double beta = 0.35 + concreteStrengthMPa / 200.0 - slenderness / 150.0;
            double kPhi = 1.0 + beta * effectiveCreepCoefficient;
            if (kPhi < 1.0) kPhi = 1.0;

            double curvature = kr * kPhi * baseCurvature;

            // 5.8.8.2(3) : e2 = (1/r) l0^2 / c, c = 10 pour une courbure sinusoidale
            double eccentricity = curvature * bucklingLengthMm * bucklingLengthMm / 10.0;

            return new SecondOrderResult
            {
                Slenderness = slenderness,
                Required = true,
                SecondOrderEccentricityMm = eccentricity,
                SecondOrderMomentNmm = axialForceN * eccentricity,
                Justification = string.Format(
                    "EC2 5.8.8 : Kr = {0:0.00}, Kphi = {1:0.00}, 1/r = {2:0.00E+00} 1/mm, " +
                    "e2 = {3:0} mm, M2 = {4:0.0} kN.m",
                    kr, kPhi, curvature, eccentricity, axialForceN * eccentricity / 1e6)
            };
        }

        /// <summary>
        /// Excentricite minimale e_0 = max(h/30 ; 20 mm) (EN 1992-1-1 6.1(4)).
        /// </summary>
        public static double MinimumEccentricityMm(double sectionHeightMm)
        {
            return Math.Max(sectionHeightMm / 30.0, 20.0);
        }

        /// <summary>
        /// Exposant a de la formule d'interaction biaxiale, interpole selon N_Ed / N_Rd
        /// (EN 1992-1-1 5.8.9(4)).
        /// </summary>
        public static double BiaxialExponent(double relativeAxialForce)
        {
            if (relativeAxialForce <= 0.1) return 1.0;
            if (relativeAxialForce >= 1.0) return 2.0;
            if (relativeAxialForce <= 0.7)
            {
                return 1.0 + 0.5 * (relativeAxialForce - 0.1) / 0.6;
            }
            return 1.5 + 0.5 * (relativeAxialForce - 0.7) / 0.3;
        }
    }
}
