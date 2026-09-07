using System;
using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>
    /// Largeur participante de la table de compression, EN 1992-1-1:2004 article 5.3.2.1.
    /// </summary>
    public static class EffectiveFlangeWidth
    {
        /// <summary>
        /// Distance entre points de moment nul l0, estimee a partir du type de travee
        /// (figure 5.2). Valeurs valables lorsque les portees adjacentes ne different pas
        /// de plus de 50 %.
        /// </summary>
        public static double ZeroMomentLengthMm(BeamSpanKind kind, double spanMm)
        {
            switch (kind)
            {
                case BeamSpanKind.EndSpan:
                    return 0.85 * spanMm;
                case BeamSpanKind.InteriorSpan:
                    return 0.70 * spanMm;
                case BeamSpanKind.Cantilever:
                    return 1.50 * spanMm;
                default:
                    return spanMm;      // travee isostatique
            }
        }

        /// <summary>
        /// Largeur participante b_eff (mm).
        /// b_eff = somme(b_eff,i) + b_w &lt;= b, avec
        /// b_eff,i = min(0,2 b_i + 0,1 l0 ; 0,2 l0 ; b_i).
        /// </summary>
        /// <param name="webWidthMm">Largeur de l'ame b_w.</param>
        /// <param name="totalFlangeWidthMm">Largeur totale disponible de la table b.</param>
        /// <param name="zeroMomentLengthMm">Distance entre moments nuls l0.</param>
        public static double Compute(double webWidthMm, double totalFlangeWidthMm,
                                     double zeroMomentLengthMm)
        {
            if (totalFlangeWidthMm <= webWidthMm) return webWidthMm;

            // Debord de chaque cote de l'ame.
            double overhang = (totalFlangeWidthMm - webWidthMm) / 2.0;
            double contribution = Math.Min(0.2 * overhang + 0.1 * zeroMomentLengthMm,
                                  Math.Min(0.2 * zeroMomentLengthMm, overhang));

            double effective = 2.0 * contribution + webWidthMm;
            return Math.Min(effective, totalFlangeWidthMm);
        }
    }
}
