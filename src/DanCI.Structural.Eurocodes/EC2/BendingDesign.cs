using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Resultat du dimensionnement en flexion simple d'une section.</summary>
    public sealed class BendingResult
    {
        /// <summary>Moment reduit mu = M_Ed / (b d^2 f_cd).</summary>
        public double Mu { get; set; }

        /// <summary>Moment reduit limite au-dela duquel des aciers comprimes sont necessaires.</summary>
        public double MuLimit { get; set; }

        /// <summary>Hauteur relative de l'axe neutre x/d.</summary>
        public double NeutralAxisRatio { get; set; }

        /// <summary>Bras de levier z (mm).</summary>
        public double LeverArmMm { get; set; }

        /// <summary>Section d'acier tendu requise (mm2).</summary>
        public double TensionSteelMm2 { get; set; }

        /// <summary>Section d'acier comprime requise (mm2), 0 si inutile.</summary>
        public double CompressionSteelMm2 { get; set; }

        public bool NeedsCompressionSteel
        {
            get { return CompressionSteelMm2 > 0.0; }
        }

        /// <summary>La table suffit-elle a equilibrer la compression (section en T) ?</summary>
        public bool NeutralAxisInFlange { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Flexion simple selon l'EN 1992-1-1:2004, articles 6.1 et 9.2.1.
    /// Le calcul utilise le diagramme rectangulaire simplifie (3.1.7(3)) : lambda = 0,8 et
    /// eta = 1,0 pour les betons jusqu'a C50/60.
    /// </summary>
    public static class BendingDesign
    {
        /// <summary>
        /// Hauteur relative limite de l'axe neutre sans redistribution, article 5.5(4) :
        /// x/d &lt;= (delta - k1) / k2 avec k1 = 0,44 et k2 = 1,25 pour eps_cu2 = 0,0035.
        /// </summary>
        public static double NeutralAxisLimit(double redistributionRatio = 1.0)
        {
            const double k1 = 0.44;
            const double k2 = 1.25;
            double limit = (redistributionRatio - k1) / k2;
            return Math.Max(0.10, Math.Min(limit, 0.45));
        }

        /// <summary>Moment reduit limite correspondant a <see cref="NeutralAxisLimit"/>.</summary>
        public static double ReducedMomentLimit(double neutralAxisLimit)
        {
            return 0.8 * neutralAxisLimit * (1.0 - 0.4 * neutralAxisLimit);
        }

        /// <summary>
        /// Dimensionne une section rectangulaire en flexion simple.
        /// </summary>
        /// <param name="momentNmm">Moment de calcul M_Ed (N.mm), positif.</param>
        /// <param name="widthMm">Largeur comprimee b.</param>
        /// <param name="effectiveDepthMm">Hauteur utile d.</param>
        /// <param name="compressionSteelDepthMm">Distance d2 du parement comprime aux aciers comprimes.</param>
        /// <param name="materials">Proprietes des materiaux.</param>
        /// <param name="redistributionRatio">Coefficient de redistribution delta (1,0 = aucune).</param>
        public static BendingResult Rectangular(double momentNmm, double widthMm,
                                                double effectiveDepthMm,
                                                double compressionSteelDepthMm,
                                                ConcreteProperties materials,
                                                double redistributionRatio = 1.0)
        {
            var result = new BendingResult { NeutralAxisInFlange = true };
            if (momentNmm <= 0 || widthMm <= 0 || effectiveDepthMm <= 0)
            {
                result.Justification = "Moment ou geometrie nuls : aucune armature de flexion requise.";
                return result;
            }

            double fcd = materials.Fcd;
            double fyd = materials.Fyd;

            double mu = momentNmm / (widthMm * effectiveDepthMm * effectiveDepthMm * fcd);
            double xiLimit = NeutralAxisLimit(redistributionRatio);
            double muLimit = ReducedMomentLimit(xiLimit);

            result.Mu = mu;
            result.MuLimit = muLimit;

            if (mu <= muLimit)
            {
                // Section simplement armee. L'equilibre du diagramme rectangulaire donne
                // mu = 0,8 xi (1 - 0,4 xi), soit 0,32 xi^2 - 0,8 xi + mu = 0, dont la racine
                // utile est xi = 1,25 (1 - racine(1 - 2 mu)). C'est exactement l'inverse de
                // ReducedMomentLimit : les deux fonctions doivent se composer en l'identite.
                double xi = 1.25 * (1.0 - Math.Sqrt(Math.Max(1.0 - 2.0 * mu, 0.0)));
                double lever = effectiveDepthMm * (1.0 - 0.4 * xi);

                result.NeutralAxisRatio = xi;
                result.LeverArmMm = lever;
                result.TensionSteelMm2 = momentNmm / (lever * fyd);
                result.Justification = string.Format(
                    "EC2 6.1 : mu = {0:0.0000} <= mu_lim = {1:0.0000} -> x/d = {2:0.000}, " +
                    "z = {3:0} mm, As = {4:0} mm2 (section simplement armee)",
                    mu, muLimit, xi, lever, result.TensionSteelMm2);
                return result;
            }

            // Section doublement armee : la part au-dela du moment limite est reprise par un
            // couple acier tendu / acier comprime.
            double leverLimit = effectiveDepthMm * (1.0 - 0.4 * xiLimit);
            double momentLimit = muLimit * widthMm * effectiveDepthMm * effectiveDepthMm * fcd;
            double excess = momentNmm - momentLimit;
            double innerArm = effectiveDepthMm - compressionSteelDepthMm;

            result.NeutralAxisRatio = xiLimit;
            result.LeverArmMm = leverLimit;

            if (innerArm <= 0)
            {
                result.Justification =
                    "La position des aciers comprimes est incompatible avec la hauteur utile.";
                return result;
            }

            // Contrainte de l'acier comprime : eps_sc = eps_cu2 (x - d2) / x
            double neutralAxis = xiLimit * effectiveDepthMm;
            double compressionStrain = materials.StrainCu2
                                       * (neutralAxis - compressionSteelDepthMm) / neutralAxis;
            double compressionStress = Math.Min(materials.Es * compressionStrain, fyd);
            if (compressionStress < 0) compressionStress = 0;

            result.CompressionSteelMm2 = compressionStress > 0
                ? excess / (innerArm * compressionStress)
                : 0.0;
            result.TensionSteelMm2 = momentLimit / (leverLimit * fyd) + excess / (innerArm * fyd);

            result.Justification = string.Format(
                "EC2 6.1 : mu = {0:0.0000} > mu_lim = {1:0.0000} -> aciers comprimes necessaires. " +
                "x/d fixe a {2:0.000}, z = {3:0} mm, As = {4:0} mm2, A's = {5:0} mm2 " +
                "(sigma_sc = {6:0} MPa)",
                mu, muLimit, xiLimit, leverLimit, result.TensionSteelMm2,
                result.CompressionSteelMm2, compressionStress);
            return result;
        }

        /// <summary>
        /// Dimensionne une section en T en flexion simple. Si l'axe neutre reste dans la
        /// table, le calcul se ramene a une section rectangulaire de largeur b_eff ; sinon la
        /// section est decomposee en debords de table et ame.
        /// </summary>
        public static BendingResult TSection(double momentNmm, double effectiveFlangeWidthMm,
                                             double webWidthMm, double flangeThicknessMm,
                                             double effectiveDepthMm, double compressionSteelDepthMm,
                                             ConcreteProperties materials,
                                             double redistributionRatio = 1.0)
        {
            if (effectiveFlangeWidthMm <= webWidthMm || flangeThicknessMm <= 0)
            {
                return Rectangular(momentNmm, webWidthMm, effectiveDepthMm,
                                   compressionSteelDepthMm, materials, redistributionRatio);
            }

            // Hypothese : axe neutre dans la table.
            BendingResult asRectangular = Rectangular(momentNmm, effectiveFlangeWidthMm,
                effectiveDepthMm, compressionSteelDepthMm, materials, redistributionRatio);

            double compressionBlockDepth = 0.8 * asRectangular.NeutralAxisRatio * effectiveDepthMm;
            if (compressionBlockDepth <= flangeThicknessMm)
            {
                asRectangular.NeutralAxisInFlange = true;
                asRectangular.Justification = string.Format(
                    "EC2 6.1 (section en T) : 0,8 x = {0:0} mm <= h_f = {1:0} mm, la table suffit. ",
                    compressionBlockDepth, flangeThicknessMm) + asRectangular.Justification;
                return asRectangular;
            }

            // Axe neutre dans l'ame : moment repris par les debords de table, reste pour l'ame.
            double fcd = materials.Fcd;
            double fyd = materials.Fyd;
            double overhangWidth = effectiveFlangeWidthMm - webWidthMm;
            double overhangForce = overhangWidth * flangeThicknessMm * fcd;
            double overhangMoment = overhangForce * (effectiveDepthMm - flangeThicknessMm / 2.0);
            double webMoment = momentNmm - overhangMoment;

            BendingResult web = Rectangular(webMoment, webWidthMm, effectiveDepthMm,
                compressionSteelDepthMm, materials, redistributionRatio);

            var result = new BendingResult
            {
                Mu = web.Mu,
                MuLimit = web.MuLimit,
                NeutralAxisRatio = web.NeutralAxisRatio,
                LeverArmMm = web.LeverArmMm,
                CompressionSteelMm2 = web.CompressionSteelMm2,
                NeutralAxisInFlange = false,
                TensionSteelMm2 = web.TensionSteelMm2 + overhangForce / fyd
            };
            result.Justification = string.Format(
                "EC2 6.1 (section en T) : 0,8 x depasse h_f = {0:0} mm. Debords de table : " +
                "F = {1:0} N, M = {2:0.0} kN.m, As = {3:0} mm2. Ame : M = {4:0.0} kN.m, " +
                "As = {5:0} mm2. Total As = {6:0} mm2.",
                flangeThicknessMm, overhangForce, overhangMoment / 1e6, overhangForce / fyd,
                webMoment / 1e6, web.TensionSteelMm2, result.TensionSteelMm2);
            return result;
        }

        /// <summary>
        /// Section minimale d'armature tendue, article 9.2.1.1(1) :
        /// A_s,min = max(0,26 f_ctm / f_yk b_t d ; 0,0013 b_t d).
        /// </summary>
        public static double MinimumTensionSteel(double widthMm, double effectiveDepthMm,
                                                 ConcreteProperties materials,
                                                 out string justification)
        {
            double fctm = materials.Fctm;
            double fyk = materials.Steel.FykMPa;
            double byBond = 0.26 * fctm / fyk * widthMm * effectiveDepthMm;
            double byGeometry = 0.0013 * widthMm * effectiveDepthMm;
            double result = Math.Max(byBond, byGeometry);

            justification = string.Format(
                "EC2 9.2.1.1(1) : As,min = max(0,26 f_ctm/f_yk b_t d ; 0,0013 b_t d) = " +
                "max({0:0} ; {1:0}) = {2:0} mm2 (f_ctm = {3:0.00} MPa)",
                byBond, byGeometry, result, fctm);
            return result;
        }

        /// <summary>Section maximale d'armature, article 9.2.1.1(3) : 0,04 A_c.</summary>
        public static double MaximumSteel(double grossAreaMm2)
        {
            return 0.04 * grossAreaMm2;
        }
    }
}
