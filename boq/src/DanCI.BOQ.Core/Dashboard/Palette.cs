using System;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Dashboard
{
    /// <summary>
    /// Couleurs partagees par la fenetre, le PDF et le classeur (fond nuit) : une couleur par lot
    /// pour reperer les lots d'un coup d'oeil, et une couleur par statut (mesure, estime...).
    /// Valeurs « #RRGGBB ».
    /// </summary>
    public static class Palette
    {
        public const string Cyan = "#00E5FF";
        public const string Violet = "#B388FF";
        public const string Green = "#21E6A1";
        public const string Amber = "#FFC233";
        public const string Orange = "#FF8A00";
        public const string Magenta = "#FF5CA8";
        public const string Blue = "#4DA3FF";
        public const string Red = "#FF4D6D";
        public const string Lime = "#A3E635";
        public const string Teal = "#2DD4BF";
        public const string Coral = "#FF7A59";
        public const string Sky = "#7DD3FC";
        public const string Yellow = "#FDE047";
        public const string Fuchsia = "#E879F9";

        private static readonly string[] LotColors = { Violet, Orange, Amber, Cyan, Blue, Green, Magenta, Lime, Teal, Coral, Yellow, Sky, Fuchsia };

        /// <summary>Couleur d'un lot : stable (00 violet, 03 cyan, 05 vert...), NC rouge, DEM corail.</summary>
        private static readonly string[] SeriesColors =
        {
            Cyan, Orange, Green, Violet, Amber, Magenta, Blue, Lime, Teal, Coral, Sky, Yellow, Fuchsia, Red, "#94A3B8"
        };

        /// <summary>Couleur n° i d'une serie (materiaux, niveaux), stable ; au-dela : gris.</summary>
        public static string Series(int i) => SeriesColors[Math.Max(0, Math.Min(i, SeriesColors.Length - 1))];

        public static string ForLot(string lotCode)
        {
            string code = (lotCode ?? string.Empty).Trim();
            if (code.Equals("NC", StringComparison.OrdinalIgnoreCase)) return Red;
            if (code.Equals("DEM", StringComparison.OrdinalIgnoreCase)) return Coral;
            if (int.TryParse(code, out int n) && n >= 0) return LotColors[n % LotColors.Length];
            int hash = 0;
            foreach (char c in code) hash = (hash * 31 + c) & 0x7FFFFFFF;
            return LotColors[hash % LotColors.Length];
        }

        public static string ForReliability(Reliability r)
        {
            switch (r)
            {
                case Reliability.Measured: return Green;
                case Reliability.Manual: return Violet;
                case Reliability.Estimated: return Amber;
                default: return Red;
            }
        }

        public static string ForSeverity(IssueSeverity s) =>
            s == IssueSeverity.Error ? Red : s == IssueSeverity.Warning ? Amber : Sky;
    }
}
