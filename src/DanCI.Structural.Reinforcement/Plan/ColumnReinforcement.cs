using System;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Ferraillage retenu pour un poteau : ce que le moteur a decide, avant traduction en
    /// geometrie. Toutes les dimensions sont en millimetres.
    /// </summary>
    public sealed class ColumnReinforcement
    {
        // --- Armatures longitudinales ---
        public double BarDiameterMm { get; set; }
        /// <summary>Barres par face parallele a X, angles compris (section rectangulaire).</summary>
        public int BarsAlongX { get; set; }
        /// <summary>Barres par face parallele a Y, angles compris (section rectangulaire).</summary>
        public int BarsAlongY { get; set; }
        public int TotalBars { get; set; }
        public double SteelAreaMm2 { get; set; }

        // --- Armatures transversales ---
        public double StirrupDiameterMm { get; set; }
        public double SpacingCurrentMm { get; set; }
        public double SpacingCriticalMm { get; set; }
        public double CriticalZoneLengthMm { get; set; }
        public bool UseCriticalZones { get; set; }
        public double FirstStirrupOffsetMm { get; set; }

        // --- Epingles ---
        /// <summary>Nombre d'epingles orientees suivant X, par lit.</summary>
        public int CrossTiesAlongX { get; set; }
        /// <summary>Nombre d'epingles orientees suivant Y, par lit.</summary>
        public int CrossTiesAlongY { get; set; }

        // --- Cotes ---
        public double CoverMm { get; set; }
        public double BottomOffsetMm { get; set; }
        public double TopExtensionMm { get; set; }
        public double LapLengthMm { get; set; }

        public string LongitudinalLabel
        {
            get
            {
                if (TotalBars <= 0) return "-";
                return string.Format("{0} HA{1:0}", TotalBars, BarDiameterMm);
            }
        }

        public string TransverseLabel
        {
            get
            {
                if (StirrupDiameterMm <= 0) return "-";
                if (Math.Abs(SpacingCriticalMm - SpacingCurrentMm) < 1.0)
                {
                    return string.Format("HA{0:0} e={1:0}", StirrupDiameterMm, SpacingCurrentMm);
                }
                return string.Format("HA{0:0} e={1:0} / {2:0} (zones critiques)",
                    StirrupDiameterMm, SpacingCurrentMm, SpacingCriticalMm);
            }
        }
    }
}
