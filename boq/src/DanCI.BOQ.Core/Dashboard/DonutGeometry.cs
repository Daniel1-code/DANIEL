using System;
using System.Collections.Generic;
using System.Globalization;

namespace DanCI.BOQ.Core.Dashboard
{
    /// <summary>
    /// Secteurs d'un anneau (graphique « donut ») en syntaxe de chemin SVG / WPF.
    /// Calcul pur, sans dependance graphique : les angles partent de midi et tournent
    /// dans le sens horaire.
    /// </summary>
    public static class DonutGeometry
    {
        /// <summary>Plus petit secteur dessine (en fraction) : evite un trait invisible.</summary>
        public const double MinVisibleShare = 0.002;

        /// <summary>Chemin d'un secteur d'anneau entre deux fractions (0..1) du tour.</summary>
        public static string Segment(double cx, double cy, double outer, double inner, double startShare, double sweepShare)
        {
            if (sweepShare <= 0) return string.Empty;
            if (sweepShare >= 0.9999)
            {
                // Anneau complet : deux demi-arcs (un arc de 360 degres ne se dessine pas).
                return Segment(cx, cy, outer, inner, startShare, 0.5) + " " + Segment(cx, cy, outer, inner, startShare + 0.5, 0.5);
            }
            double a0 = startShare * 2 * Math.PI;
            double a1 = (startShare + sweepShare) * 2 * Math.PI;
            string large = sweepShare > 0.5 ? "1" : "0";
            return "M " + P(cx, cy, outer, a0)
                 + " A " + N(outer) + "," + N(outer) + " 0 " + large + " 1 " + P(cx, cy, outer, a1)
                 + " L " + P(cx, cy, inner, a1)
                 + " A " + N(inner) + "," + N(inner) + " 0 " + large + " 0 " + P(cx, cy, inner, a0)
                 + " Z";
        }

        /// <summary>Decoupe un tour en secteurs consecutifs proportionnels aux parts (negatifs ignores).</summary>
        public static IReadOnlyList<(double Start, double Sweep)> Split(IReadOnlyList<decimal> shares)
        {
            var result = new List<(double, double)>();
            decimal total = 0m;
            foreach (decimal s in shares) if (s > 0m) total += s;
            double start = 0;
            foreach (decimal s in shares)
            {
                double sweep = total > 0m && s > 0m ? (double)(s / total) : 0;
                result.Add((start, sweep));
                start += sweep;
            }
            return result;
        }

        private static string P(double cx, double cy, double r, double angle)
            => N(cx + r * Math.Sin(angle)) + "," + N(cy - r * Math.Cos(angle));

        private static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);
    }
}
