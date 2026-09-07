using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DanCI.Structural.Documentation.Reports;
using DanCI.Structural.Engine.Column;

namespace DanCI.Structural.Documentation.Quantities
{
    /// <summary>
    /// Met le quantitatif en tableau : une ligne par element, une ligne de total, au format
    /// CSV point-virgule directement exploitable dans Excel en configuration francaise.
    /// </summary>
    public static class QuantityCsvReport
    {
        private const char Separator = ';';

        public static string Build(IEnumerable<ColumnReportItem> items)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Join(Separator.ToString(), new[]
            {
                "Element", "Section (mm)", "Hauteur (mm)", "Volume beton (m3)",
                "Longitudinales", "Diametre (mm)", "Nombre", "Longueur de coupe (mm)",
                "Acier longitudinal (kg)",
                "Cadres", "Diametre cadre (mm)", "Nombre de cadres", "Developpe cadre (mm)",
                "Acier cadres (kg)", "Epingles (kg)",
                "Acier total (kg)", "Ratio (kg/m3)", "Taux d'armature (%)",
                "Recouvrement (mm)", "Etat", "Taux de travail"
            }));

            var total = new SteelQuantities();
            int count = 0;

            foreach (ColumnReportItem item in items)
            {
                ColumnDesignResult r = item.Result;
                if (r == null || !r.IsValid || item.Quantities == null) continue;
                count++;
                total.Merge(item.Quantities);
                SteelQuantities q = item.Quantities;

                sb.AppendLine(string.Join(Separator.ToString(), new[]
                {
                    Escape(r.Column.Name),
                    Escape(r.Column.SectionLabel),
                    Number(r.Column.HeightMm, 0),
                    Number(q.ConcreteVolumeM3, 3),
                    Escape(r.Reinforcement.LongitudinalLabel),
                    Number(r.Reinforcement.BarDiameterMm, 0),
                    r.Reinforcement.TotalBars.ToString(CultureInfo.InvariantCulture),
                    Number(q.LongitudinalCutLengthMm, 0),
                    Number(q.LongitudinalMassKg, 1),
                    Escape(r.Reinforcement.TransverseLabel),
                    Number(r.Reinforcement.StirrupDiameterMm, 0),
                    q.StirrupCount.ToString(CultureInfo.InvariantCulture),
                    Number(q.StirrupCutLengthMm, 0),
                    Number(q.StirrupMassKg, 1),
                    Number(q.CrossTieMassKg, 1),
                    Number(q.TotalMassKg, 1),
                    Number(q.RatioKgPerM3, 0),
                    Number(r.SteelRatioPercent, 2),
                    Number(r.Reinforcement.LapLengthMm, 0),
                    Escape(r.Status),
                    Number(r.MaxUtilization, 2)
                }));
            }

            sb.AppendLine();
            sb.AppendLine(string.Join(Separator.ToString(), new[]
            {
                string.Format("TOTAL ({0} elements)", count),
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

        /// <summary>Cumul de tous les elements, pour la synthese affichee dans la fenetre.</summary>
        public static SteelQuantities Total(IEnumerable<ColumnReportItem> items)
        {
            var total = new SteelQuantities();
            foreach (ColumnReportItem item in items)
            {
                if (item.Quantities != null) total.Merge(item.Quantities);
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
