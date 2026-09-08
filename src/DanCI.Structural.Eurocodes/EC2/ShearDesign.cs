using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Resultat de la verification et du dimensionnement a l'effort tranchant.</summary>
    public sealed class ShearResult
    {
        /// <summary>Resistance de la section sans armatures d'effort tranchant V_Rd,c (N).</summary>
        public double VrdcN { get; set; }

        /// <summary>Resistance maximale des bielles de beton V_Rd,max (N).</summary>
        public double VrdmaxN { get; set; }

        /// <summary>Resistance apportee par les armatures V_Rd,s (N).</summary>
        public double VrdsN { get; set; }

        /// <summary>Cotangente de l'inclinaison des bielles retenue.</summary>
        public double CotTheta { get; set; }

        /// <summary>Inclinaison des bielles (degres).</summary>
        public double ThetaDegrees { get; set; }

        /// <summary>Bras de levier z retenu (mm).</summary>
        public double LeverArmMm { get; set; }

        /// <summary>
        /// Section d'armatures d'effort tranchant par unite de longueur A_sw/s,
        /// en **mm2 par millimetre** de longueur de poutre. Une valeur de 0,25 signifie
        /// 250 mm2 par metre : multiplier par 1 000 pour obtenir des mm2/m.
        /// </summary>
        public double AswPerMillimetreMm2 { get; set; }

        /// <summary>Minimum reglementaire de A_sw/s, meme unite : mm2 par millimetre.</summary>
        public double MinimumAswPerMillimetreMm2 { get; set; }

        /// <summary>Espacement longitudinal maximal (mm), article 9.2.2(6).</summary>
        public double MaxSpacingMm { get; set; }

        /// <summary>Decalage de la courbe des moments a_l (mm), article 9.2.1.3.</summary>
        public double ShiftLengthMm { get; set; }

        /// <summary>Des armatures d'effort tranchant sont-elles necessaires ?</summary>
        public bool RequiresShearReinforcement { get; set; }

        /// <summary>La section resiste-t-elle a l'ecrasement des bielles ?</summary>
        public bool IsWebAdequate { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Effort tranchant selon l'EN 1992-1-1:2004, article 6.2, et dispositions constructives
    /// de l'article 9.2.2. La methode est celle des bielles a inclinaison variable :
    /// l'inclinaison la plus economique (cot theta = 2,5) est retenue tant que les bielles
    /// resistent, sinon theta est redresse jusqu'a ce que V_Rd,max atteigne V_Ed.
    /// </summary>
    public static class ShearDesign
    {
        /// <summary>Cotangente maximale autorisee, article 6.2.3(2) : 1 &lt;= cot theta &lt;= 2,5.</summary>
        public const double MaxCotTheta = 2.5;
        public const double MinCotTheta = 1.0;

        /// <summary>
        /// Resistance sans armatures d'effort tranchant V_Rd,c (N), article 6.2.2(1).
        /// </summary>
        /// <param name="webWidthMm">Largeur de l'ame b_w.</param>
        /// <param name="effectiveDepthMm">Hauteur utile d.</param>
        /// <param name="tensionSteelMm2">Section d'acier tendu ancree A_sl.</param>
        /// <param name="axialStressMPa">Contrainte de compression sigma_cp = N_Ed / A_c.</param>
        public static double ShearResistanceWithoutReinforcement(
            double webWidthMm, double effectiveDepthMm, double tensionSteelMm2,
            double axialStressMPa, ConcreteProperties materials, double gammaC,
            out string justification)
        {
            double crdc = 0.18 / gammaC;
            double k = 1.0 + Math.Sqrt(200.0 / effectiveDepthMm);
            if (k > 2.0) k = 2.0;

            double rho = tensionSteelMm2 / (webWidthMm * effectiveDepthMm);
            if (rho > 0.02) rho = 0.02;
            if (rho < 0) rho = 0;

            const double k1 = 0.15;
            double compression = Math.Min(axialStressMPa, 0.2 * materials.Fcd);

            double main = (crdc * k * Math.Pow(100.0 * rho * materials.Fck, 1.0 / 3.0)
                           + k1 * compression) * webWidthMm * effectiveDepthMm;

            double vmin = 0.035 * Math.Pow(k, 1.5) * Math.Sqrt(materials.Fck);
            double floor = (vmin + k1 * compression) * webWidthMm * effectiveDepthMm;

            double result = Math.Max(main, floor);
            justification = string.Format(
                "EC2 6.2.2(1) : k = {0:0.000}, rho_l = {1:0.0000}, C_Rd,c = {2:0.000} -> " +
                "V_Rd,c = max({3:0.0} ; {4:0.0}) = {5:0.0} kN (v_min = {6:0.000} MPa)",
                k, rho, crdc, main / 1000.0, floor / 1000.0, result / 1000.0, vmin);
            return result;
        }

        /// <summary>
        /// Resistance maximale des bielles V_Rd,max (N), article 6.2.3(3), equation 6.9.
        /// </summary>
        public static double WebCrushingResistance(double webWidthMm, double leverArmMm,
                                                   double cotTheta, ConcreteProperties materials)
        {
            double nu1 = 0.6 * (1.0 - materials.Fck / 250.0);
            const double alphaCw = 1.0;    // beton non precontraint
            double tanTheta = 1.0 / cotTheta;
            return alphaCw * webWidthMm * leverArmMm * nu1 * materials.Fcd / (cotTheta + tanTheta);
        }

        /// <summary>
        /// Dimensionne les armatures d'effort tranchant d'une section.
        /// </summary>
        /// <param name="shearForceN">Effort tranchant de calcul V_Ed (N).</param>
        /// <param name="webWidthMm">Largeur de l'ame b_w.</param>
        /// <param name="effectiveDepthMm">Hauteur utile d.</param>
        /// <param name="tensionSteelMm2">Acier tendu ancre A_sl, pour V_Rd,c.</param>
        /// <param name="axialStressMPa">Contrainte de compression sigma_cp.</param>
        /// <param name="materials">Materiaux.</param>
        /// <param name="gammaC">Coefficient partiel du beton.</param>
        public static ShearResult Design(double shearForceN, double webWidthMm,
                                         double effectiveDepthMm, double tensionSteelMm2,
                                         double axialStressMPa, ConcreteProperties materials,
                                         double gammaC)
        {
            var result = new ShearResult
            {
                LeverArmMm = 0.9 * effectiveDepthMm,
                IsWebAdequate = true
            };

            string vrdcJustification;
            result.VrdcN = ShearResistanceWithoutReinforcement(webWidthMm, effectiveDepthMm,
                tensionSteelMm2, axialStressMPa, materials, gammaC, out vrdcJustification);

            // Minimum reglementaire, article 9.2.2(5) : rho_w,min = 0,08 sqrt(f_ck) / f_yk
            double rhoMin = 0.08 * Math.Sqrt(materials.Fck) / materials.Steel.FykMPa;
            result.MinimumAswPerMillimetreMm2 = rhoMin * webWidthMm;

            // Espacement maximal, article 9.2.2(6) : s_max = 0,75 d pour des cadres verticaux.
            result.MaxSpacingMm = 0.75 * effectiveDepthMm;

            if (shearForceN <= result.VrdcN)
            {
                result.RequiresShearReinforcement = false;
                result.CotTheta = MaxCotTheta;
                result.ThetaDegrees = Math.Atan(1.0 / MaxCotTheta) * 180.0 / Math.PI;
                result.AswPerMillimetreMm2 = result.MinimumAswPerMillimetreMm2;
                result.VrdsN = result.AswPerMillimetreMm2 * result.LeverArmMm * materials.Fyd * MaxCotTheta;
                result.VrdmaxN = WebCrushingResistance(webWidthMm, result.LeverArmMm,
                                                       MaxCotTheta, materials);
                result.ShiftLengthMm = result.LeverArmMm * MaxCotTheta / 2.0;
                result.Justification = vrdcJustification + string.Format(
                    " ; V_Ed = {0:0.0} kN <= V_Rd,c : seules les armatures minimales sont requises " +
                    "(EC2 9.2.2(5) : A_sw/s >= {1:0.000} mm2/mm).",
                    shearForceN / 1000.0, result.MinimumAswPerMillimetreMm2);
                return result;
            }

            result.RequiresShearReinforcement = true;

            // Inclinaison la plus economique tant que les bielles resistent.
            double cotTheta = MaxCotTheta;
            double vrdmax = WebCrushingResistance(webWidthMm, result.LeverArmMm, cotTheta, materials);

            if (shearForceN > vrdmax)
            {
                // On redresse les bielles : V_Rd,max = alpha_cw b_w z nu1 f_cd sin(2 theta) / 2
                double nu1 = 0.6 * (1.0 - materials.Fck / 250.0);
                double capacity = webWidthMm * result.LeverArmMm * nu1 * materials.Fcd;
                double sinTwoTheta = 2.0 * shearForceN / capacity;

                if (sinTwoTheta >= 1.0)
                {
                    // Meme a 45 degres les bielles ne suffisent pas.
                    result.IsWebAdequate = false;
                    cotTheta = MinCotTheta;
                    vrdmax = WebCrushingResistance(webWidthMm, result.LeverArmMm, cotTheta, materials);
                }
                else
                {
                    double theta = 0.5 * Math.Asin(sinTwoTheta);
                    cotTheta = 1.0 / Math.Tan(theta);
                    if (cotTheta > MaxCotTheta) cotTheta = MaxCotTheta;
                    if (cotTheta < MinCotTheta) cotTheta = MinCotTheta;
                    vrdmax = WebCrushingResistance(webWidthMm, result.LeverArmMm, cotTheta, materials);
                }
            }

            result.CotTheta = cotTheta;
            result.ThetaDegrees = Math.Atan(1.0 / cotTheta) * 180.0 / Math.PI;
            result.VrdmaxN = vrdmax;

            // Equation 6.8 : A_sw/s = V_Ed / (z f_ywd cot theta)
            double required = shearForceN / (result.LeverArmMm * materials.Fyd * cotTheta);
            result.AswPerMillimetreMm2 = Math.Max(required, result.MinimumAswPerMillimetreMm2);
            result.VrdsN = result.AswPerMillimetreMm2 * result.LeverArmMm * materials.Fyd * cotTheta;

            // Article 9.2.1.3 : decalage de la courbe des moments.
            result.ShiftLengthMm = result.LeverArmMm * cotTheta / 2.0;

            result.Justification = vrdcJustification + string.Format(
                " ; V_Ed = {0:0.0} kN > V_Rd,c -> armatures necessaires. " +
                "EC2 6.2.3 : z = {1:0} mm, cot theta = {2:0.00} (theta = {3:0.0} degres), " +
                "V_Rd,max = {4:0.0} kN, A_sw/s = {5:0.000} mm2/mm{6}",
                shearForceN / 1000.0, result.LeverArmMm, cotTheta, result.ThetaDegrees,
                vrdmax / 1000.0, result.AswPerMillimetreMm2,
                result.IsWebAdequate ? "" : " - SECTION INSUFFISANTE : les bielles s'ecrasent.");
            return result;
        }
    }
}
