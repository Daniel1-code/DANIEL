using System;
using System.Collections.Generic;

namespace ArmaturesPoteaux.Core
{
    /// <summary>
    /// Resultat du dimensionnement d'un poteau : ce que le generateur doit modeliser,
    /// accompagne de la note de calcul justifiant chaque valeur.
    /// </summary>
    public class DesignResult
    {
        public ColumnGeometry Geometry { get; set; }

        public bool IsValid { get; set; }

        // --- Armatures longitudinales ---
        public double BarDiameterMm { get; set; }
        /// <summary>Barres par face parallele a X, angles compris (section rectangulaire).</summary>
        public int BarsAlongX { get; set; }
        /// <summary>Barres par face parallele a Y, angles compris (section rectangulaire).</summary>
        public int BarsAlongY { get; set; }
        /// <summary>Nombre total de barres longitudinales.</summary>
        public int TotalBars { get; set; }

        public double AsProvidedMm2 { get; set; }
        public double AsMinMm2 { get; set; }
        public double AsMaxMm2 { get; set; }

        public double RatioPercent
        {
            get
            {
                if (Geometry == null || Geometry.GrossAreaMm2 <= 0) return 0;
                return 100.0 * AsProvidedMm2 / Geometry.GrossAreaMm2;
            }
        }

        // --- Armatures transversales ---
        public double StirrupDiameterMm { get; set; }
        public double SpacingCurrentMm { get; set; }
        public double SpacingCriticalMm { get; set; }
        public double CriticalZoneLengthMm { get; set; }

        // --- Epingles ---
        public int CrossTiesAlongX { get; set; }
        public int CrossTiesAlongY { get; set; }

        // --- Recouvrement ---
        public double LapLengthMm { get; set; }
        public double TopExtensionMm { get; set; }
        public double BottomOffsetMm { get; set; }

        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        public DesignResult()
        {
            Notes = new List<string>();
            Warnings = new List<string>();
        }

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

        public string Status
        {
            get
            {
                if (!IsValid) return "Echec";
                return Warnings.Count > 0 ? "A verifier" : "OK";
            }
        }

        /// <summary>Note de calcul complete, en texte, pour l'export ou l'affichage.</summary>
        public string BuildReport()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("=== " + (Geometry != null ? Geometry.HostName : "Poteau") + " ===");
            if (Geometry != null)
            {
                sb.AppendLine(string.Format("Section : {0} mm - Hauteur : {1:0} mm - Ac = {2:0} mm2",
                    Geometry.SectionLabel, Geometry.HeightMm, Geometry.GrossAreaMm2));
            }
            foreach (var note in Notes) sb.AppendLine("  - " + note);
            foreach (var warning in Warnings) sb.AppendLine("  ! " + warning);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
