using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// Met le quantitatif en tableau : une ligne par poteau, une ligne de total, au format
    /// CSV point-virgule directement exploitable dans Excel en configuration francaise.
    /// </summary>
    public static class QuantityReport
    {
        private const char Separator = ';';

        public static string BuildCsv(IEnumerable<DesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Separator.ToString(), new[]
            {
                "Poteau", "Section (mm)", "Hauteur (mm)", "Volume beton (m3)",
                "Longitudinales", "Diametre (mm)", "Nombre", "Longueur de coupe (mm)",
                "Acier longitudinal (kg)",
                "Cadres", "Diametre cadre (mm)", "Nombre de cadres", "Developpe cadre (mm)",
                "Acier cadres (kg)", "Epingles (kg)",
                "Acier total (kg)", "Ratio (kg/m3)", "Taux d'armature (%)",
                "Recouvrement (mm)", "Verification N-M", "Taux de travail"
            }));

            var total = new SteelQuantities();
            int count = 0;

            foreach (DesignResult r in results)
            {
                if (!r.IsValid || r.Quantities == null) continue;
                count++;
                total.Merge(r.Quantities);
                SteelQuantities q = r.Quantities;

                sb.AppendLine(string.Join(Separator.ToString(), new[]
                {
                    Escape(r.Geometry.HostName),
                    Escape(r.Geometry.SectionLabel),
                    Number(r.Geometry.HeightMm, 0),
                    Number(q.ConcreteVolumeM3, 3),
                    Escape(r.LongitudinalLabel),
                    Number(r.BarDiameterMm, 0),
                    r.TotalBars.ToString(CultureInfo.InvariantCulture),
                    Number(q.LongitudinalCutLengthMm, 0),
                    Number(q.LongitudinalMassKg, 1),
                    Escape(r.TransverseLabel),
                    Number(r.StirrupDiameterMm, 0),
                    q.StirrupCount.ToString(CultureInfo.InvariantCulture),
                    Number(q.StirrupCutLengthMm, 0),
                    Number(q.StirrupMassKg, 1),
                    Number(q.CrossTieMassKg, 1),
                    Number(q.TotalMassKg, 1),
                    Number(q.RatioKgPerM3, 0),
                    Number(r.RatioPercent, 2),
                    Number(r.LapLengthMm, 0),
                    r.Check != null && r.Check.Performed ? (r.Check.Passes ? "OK" : "NON") : "-",
                    r.Check != null && r.Check.Performed ? Number(r.Check.Utilisation, 2) : "-"
                }));
            }

            sb.AppendLine();
            sb.AppendLine(string.Join(Separator.ToString(), new[]
            {
                string.Format("TOTAL ({0} poteaux)", count),
                "", "",
                Number(total.ConcreteVolumeM3, 3),
                "", "",
                total.LongitudinalBarCount.ToString(CultureInfo.InvariantCulture),
                "",
                Number(total.LongitudinalMassKg, 1),
                "", "",
                total.StirrupCount.ToString(CultureInfo.InvariantCulture),
                "",
                Number(total.StirrupMassKg, 1),
                Number(total.CrossTieMassKg, 1),
                Number(total.TotalMassKg, 1),
                Number(total.RatioKgPerM3, 0),
                "", "", "", ""
            }));

            sb.AppendLine();
            sb.AppendLine("Repartition par diametre" + Separator + Escape(total.DiameterBreakdown()));
            return sb.ToString();
        }

        /// <summary>Cumul de tous les poteaux, pour l'affichage de synthese dans la fenetre.</summary>
        public static SteelQuantities Total(IEnumerable<DesignResult> results)
        {
            var total = new SteelQuantities();
            foreach (DesignResult r in results)
            {
                if (r.IsValid && r.Quantities != null) total.Merge(r.Quantities);
            }
            return total;
        }

        private static string Number(double value, int decimals)
        {
            // Virgule decimale : Excel en configuration francaise lit directement les nombres.
            return value.ToString("F" + decimals, CultureInfo.InvariantCulture).Replace('.', ',');
        }

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Replace(Separator, ' ').Replace("\r", " ").Replace("\n", " ");
        }
    }
}
