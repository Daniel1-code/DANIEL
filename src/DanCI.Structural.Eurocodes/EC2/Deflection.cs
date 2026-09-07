using System;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Systeme structurel, tableau 7.4N, dont depend le coefficient K.</summary>
    public enum StructuralSystem
    {
        /// <summary>Poutre ou dalle isostatique : K = 1,0.</summary>
        SimplySupported,
        /// <summary>Travee de rive d'une poutre continue ou d'une dalle continue : K = 1,3.</summary>
        EndSpan,
        /// <summary>Travee intermediaire : K = 1,5.</summary>
        InteriorSpan,
        /// <summary>Plancher-dalle, sur la base de la plus grande portee : K = 1,2.</summary>
        FlatSlab,
        /// <summary>Console : K = 0,4.</summary>
        Cantilever
    }

    /// <summary>Resultat de la verification de fleche par l'elancement limite.</summary>
    public sealed class DeflectionResult
    {
        /// <summary>Elancement de base, equations 7.16a ou 7.16b.</summary>
        public double BasicRatio { get; set; }

        /// <summary>Coefficient K du systeme structurel.</summary>
        public double SystemFactor { get; set; }

        /// <summary>Correction As,prov / As,req, plafonnee a 1,5.</summary>
        public double SteelProvisionFactor { get; set; }

        /// <summary>Correction des sections en T (b_eff / b_w &gt; 3) : 0,8.</summary>
        public double FlangeFactor { get; set; }

        /// <summary>Correction des grandes portees supportant des cloisons : 7 / l_eff.</summary>
        public double LongSpanFactor { get; set; }

        /// <summary>Elancement limite admissible apres corrections.</summary>
        public double AllowableRatio { get; set; }

        /// <summary>Elancement reel l / d.</summary>
        public double ActualRatio { get; set; }

        /// <summary>Taux de travail : elancement reel / elancement admissible.</summary>
        public double Utilization
        {
            get { return AllowableRatio > 0 ? ActualRatio / AllowableRatio : 0.0; }
        }

        public bool Passes { get { return Utilization <= 1.0; } }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Verification de la fleche par l'elancement limite, EN 1992-1-1 article 7.4.2.
    ///
    /// C'est la methode « sans calcul explicite » : elle ne rend pas une fleche en
    /// millimetres, elle repond a la question « la fleche est-elle admissible sans
    /// verification detaillee ? ». Elle vise l / 250 sous charge quasi-permanente.
    /// Lorsqu'elle est depassee, la reponse honnete n'est pas d'annoncer une fleche
    /// calculee ailleurs, mais d'exiger le calcul detaille de l'article 7.4.3.
    /// </summary>
    public static class Deflection
    {
        /// <summary>Coefficient K du tableau 7.4N.</summary>
        public static double SystemFactor(StructuralSystem system)
        {
            switch (system)
            {
                case StructuralSystem.SimplySupported: return 1.0;
                case StructuralSystem.EndSpan: return 1.3;
                case StructuralSystem.InteriorSpan: return 1.5;
                case StructuralSystem.FlatSlab: return 1.2;
                case StructuralSystem.Cantilever: return 0.4;
                default: return 1.0;
            }
        }

        /// <summary>
        /// Elancement de base l/d, equations 7.16a et 7.16b, pour K = 1.
        /// </summary>
        /// <param name="tensionRatio">rho = A_s / (b d) au milieu de la travee.</param>
        /// <param name="compressionRatio">rho' = A_s2 / (b d), nul si pas d'acier comprime.</param>
        public static double BasicRatio(double tensionRatio, double compressionRatio,
                                        double fckMPa)
        {
            double rho0 = 1e-3 * Math.Sqrt(fckMPa);
            double rho = Math.Max(tensionRatio, 1e-6);
            double rhoPrime = Math.Max(compressionRatio, 0.0);
            double sqrtFck = Math.Sqrt(fckMPa);

            if (rho <= rho0)
            {
                // Eq. 7.16a : section peu armee.
                double ratio = rho0 / rho;
                return 11.0 + 1.5 * sqrtFck * ratio
                       + 3.2 * sqrtFck * Math.Pow(ratio - 1.0, 1.5);
            }

            // Eq. 7.16b : section fortement armee.
            double denominator = Math.Max(rho - rhoPrime, 1e-6);
            return 11.0 + 1.5 * sqrtFck * rho0 / denominator
                   + sqrtFck / 12.0 * Math.Sqrt(rhoPrime / rho0);
        }

        /// <summary>
        /// Verification complete de l'article 7.4.2, corrections comprises.
        /// </summary>
        /// <param name="effectiveSpanMm">Portee de calcul l_eff.</param>
        /// <param name="effectiveDepthMm">Hauteur utile d.</param>
        /// <param name="widthMm">Largeur de la section comprimee b.</param>
        /// <param name="steelRequiredMm2">A_s requis par le calcul de flexion.</param>
        /// <param name="steelProvidedMm2">A_s reellement pose.</param>
        /// <param name="compressionSteelMm2">A_s2 comprime, nul en general dans une dalle.</param>
        /// <param name="system">Systeme structurel, tableau 7.4N.</param>
        /// <param name="fckMPa">Resistance caracteristique du beton.</param>
        /// <param name="flangeToWebRatio">b_eff / b_w ; 1 pour une section rectangulaire.</param>
        /// <param name="supportsPartitions">La portee supporte-t-elle des cloisons fragiles ?</param>
        public static DeflectionResult Check(double effectiveSpanMm, double effectiveDepthMm,
                                             double widthMm, double steelRequiredMm2,
                                             double steelProvidedMm2, double compressionSteelMm2,
                                             StructuralSystem system, double fckMPa,
                                             double flangeToWebRatio = 1.0,
                                             bool supportsPartitions = false)
        {
            var result = new DeflectionResult { FlangeFactor = 1.0, LongSpanFactor = 1.0 };
            if (effectiveDepthMm <= 0 || widthMm <= 0)
            {
                result.Justification = "Geometrie nulle : elancement non verifiable.";
                return result;
            }

            double area = widthMm * effectiveDepthMm;
            double rho = steelRequiredMm2 / area;
            double rhoPrime = compressionSteelMm2 / area;

            result.BasicRatio = BasicRatio(rho, rhoPrime, fckMPa);
            result.SystemFactor = SystemFactor(system);

            // 7.4.2(2) : correction 310/sigma_s, approchee par A_s,prov / A_s,req,
            // plafonnee a 1,5. Sur-armer ameliore la fleche, mais pas indefiniment.
            result.SteelProvisionFactor = steelRequiredMm2 > 0
                ? Math.Min(steelProvidedMm2 / steelRequiredMm2, 1.5) : 1.0;

            // 7.4.2(2) : sections en T dont la table depasse trois fois l'ame.
            if (flangeToWebRatio > 3.0) result.FlangeFactor = 0.8;

            // 7.4.2(2) : portees superieures a 7 m supportant des cloisons fragiles.
            double spanM = effectiveSpanMm / 1000.0;
            if (supportsPartitions && spanM > 7.0)
            {
                result.LongSpanFactor = 7.0 / spanM;
            }

            result.AllowableRatio = result.BasicRatio * result.SystemFactor
                                    * result.SteelProvisionFactor * result.FlangeFactor
                                    * result.LongSpanFactor;
            result.ActualRatio = effectiveSpanMm / effectiveDepthMm;

            result.Justification = string.Format(
                "EC2 7.4.2 : rho = {0:0.0000}, rho_0 = {1:0.0000} -> (l/d)_base = {2:0.0} " +
                "(eq. {3}) ; K = {4:0.0} ; A_s,prov/A_s,req = {5:0.00}{6}{7} -> " +
                "(l/d)_adm = {8:0.0} contre l/d = {9:0.0}",
                rho, 1e-3 * Math.Sqrt(fckMPa), result.BasicRatio,
                rho <= 1e-3 * Math.Sqrt(fckMPa) ? "7.16a" : "7.16b",
                result.SystemFactor, result.SteelProvisionFactor,
                result.FlangeFactor < 1.0 ? " ; table large x 0,80" : "",
                result.LongSpanFactor < 1.0
                    ? string.Format(" ; portee > 7 m x {0:0.00}", result.LongSpanFactor) : "",
                result.AllowableRatio, result.ActualRatio);
            return result;
        }
    }
}
