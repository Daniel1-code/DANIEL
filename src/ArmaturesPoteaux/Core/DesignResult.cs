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

        // --- Cotes reprises par le modeleur, l'apercu et le quantitatif ---
        public double CoverMm { get; set; }
        public double FirstStirrupOffsetMm { get; set; }
        public bool UseCriticalZones { get; set; }

        /// <summary>Quantitatif d'acier correspondant au ferraillage retenu.</summary>
        public SteelQuantities Quantities { get; set; }

        /// <summary>Verification de resistance en flexion composee (facultative).</summary>
        public CapacityCheck Check { get; set; }

        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        public DesignResult()
        {
            Notes = new List<string>();
            Warnings = new List<string>();
            Quantities = new SteelQuantities();
            Check = new CapacityCheck();
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
                if (Check != null && Check.Performed && !Check.Passes) return "Ne resiste pas";
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

            if (Check != null && Check.Performed)
            {
                sb.AppendLine();
                sb.AppendLine("  Verification de resistance (flexion composee) :");
                foreach (var note in Check.Notes) sb.AppendLine("    - " + note);
                sb.AppendLine(string.Format("    => Taux de travail {0:0.00} : {1}",
                    Check.Utilisation, Check.Passes ? "la section resiste" : "LA SECTION NE RESISTE PAS"));
            }

            if (Quantities != null && Quantities.TotalMassKg > 0)
            {
                sb.AppendLine();
                sb.AppendLine("  Quantitatif :");
                sb.AppendLine(string.Format(
                    "    - Longitudinales : {0} barres de {1:0} mm = {2:0.0} m, {3:0.0} kg",
                    Quantities.LongitudinalBarCount, Quantities.LongitudinalCutLengthMm,
                    Quantities.LongitudinalLengthM, Quantities.LongitudinalMassKg));
                sb.AppendLine(string.Format(
                    "    - Cadres : {0} unites de {1:0} mm developpes = {2:0.0} m, {3:0.0} kg",
                    Quantities.StirrupCount, Quantities.StirrupCutLengthMm,
                    Quantities.StirrupLengthM, Quantities.StirrupMassKg));
                if (Quantities.CrossTieCount > 0)
                {
                    sb.AppendLine(string.Format("    - Epingles : {0} unites = {1:0.0} m, {2:0.0} kg",
                        Quantities.CrossTieCount, Quantities.CrossTieLengthM, Quantities.CrossTieMassKg));
                }
                sb.AppendLine(string.Format("    - Total : {0:0.0} kg pour {1:0.000} m3 de beton, " +
                                            "soit {2:0} kg/m3",
                    Quantities.TotalMassKg, Quantities.ConcreteVolumeM3, Quantities.RatioKgPerM3));
            }

            foreach (var warning in Warnings) sb.AppendLine("  ! " + warning);
            sb.AppendLine();
            return sb.ToString();
        }
    }
}
