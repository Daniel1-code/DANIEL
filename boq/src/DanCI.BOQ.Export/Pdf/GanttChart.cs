using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using DanCI.BOQ.Core.Planning;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// Diagramme de Gantt en SVG (page A4 paysage, 1600 x 1130) : tableau des taches a gauche,
    /// echelle des mois et des semaines, barres a la couleur du lot, chemin critique cercle de rouge,
    /// liens de precedence, jalon de reception. Plusieurs pages si besoin, meme echelle de temps.
    /// </summary>
    public static class GanttChart
    {
        public const double Width = 1600, Height = 1130;
        private const double Top = 118, RowH = 25, LabelW = 600, Left = 610, Right = 1580;
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        public static int RowsPerPage => (int)((Height - Top - 90) / RowH);

        /// <summary>Une page SVG par tranche de lignes.</summary>
        public static List<string> Pages(WorkPlan plan, string title)
        {
            var rows = GanttRows.Build(plan);
            var pages = new List<string>();
            if (rows.Count == 0) return pages;
            int per = RowsPerPage;
            int count = (rows.Count + per - 1) / per;
            for (int p = 0; p < count; p++)
            {
                pages.Add(Page(plan, title, rows.Skip(p * per).Take(per).ToList(), rows, p + 1, count));
            }
            return pages;
        }

        private static string Page(WorkPlan plan, string title, List<GanttRow> rows, List<GanttRow> all, int page, int pages)
        {
            var sb = new StringBuilder();
            DateTime d0 = plan.StartDate, d1 = plan.FinishDate.AddDays(1);
            double span = Math.Max(1, (d1 - d0).TotalDays);
            double X(DateTime d) => Left + (Right - Left) * (d - d0).TotalDays / span;
            string N(double v) => v.ToString("0.#", Inv);

            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='" + N(Width) + "' height='" + N(Height) + "' viewBox='0 0 " + N(Width) + " " + N(Height) + "' font-family='Lato'>");
            sb.Append("<rect width='100%' height='100%' fill='#FFFFFF'/>");
            // Titre
            sb.Append("<rect x='0' y='0' width='" + N(Width) + "' height='64' fill='#0B1F3A'/>");
            sb.Append(Text(24, 30, "PLANNING DES TRAVAUX — DIAGRAMME DE GANTT", 20, "#FFFFFF", "bold"));
            sb.Append(Text(24, 52, title, 12, "#9FB6D3"));
            sb.Append(Text(Width - 24, 30, "Du " + d0.ToString("dd/MM/yyyy", Fr) + " au " + plan.FinishDate.ToString("dd/MM/yyyy", Fr), 14, "#21E6A1", "bold", "end"));
            sb.Append(Text(Width - 24, 52, plan.TotalDays + " jours ouvrés (" + plan.WorkingDaysPerWeek + " j/semaine) · " + plan.CalendarDays + " jours calendaires · page " + page + "/" + pages, 11, "#9FB6D3", null, "end"));

            // En-tetes de colonnes
            double hy = Top - 34;
            sb.Append("<rect x='0' y='" + N(hy - 18) + "' width='" + N(Width) + "' height='52' fill='#EAF1FB'/>");
            sb.Append(Text(24, hy + 2, "Tâche (lot · niveau)", 11, "#1F4E79", "bold"));
            sb.Append(Text(392, hy + 2, "Durée", 11, "#1F4E79", "bold", "end"));
            sb.Append(Text(470, hy + 2, "Début", 11, "#1F4E79", "bold", "end"));
            sb.Append(Text(548, hy + 2, "Fin", 11, "#1F4E79", "bold", "end"));
            sb.Append(Text(598, hy + 2, "Marge", 11, "#1F4E79", "bold", "end"));
            // Mois
            for (var m = new DateTime(d0.Year, d0.Month, 1); m < d1; m = m.AddMonths(1))
            {
                DateTime a = m < d0 ? d0 : m, b = m.AddMonths(1) > d1 ? d1 : m.AddMonths(1);
                double xa = X(a), xb = X(b);
                sb.Append("<rect x='" + N(xa) + "' y='" + N(hy - 18) + "' width='" + N(xb - xa) + "' height='26' fill='" + (m.Month % 2 == 0 ? "#D6E4F5" : "#EAF1FB") + "'/>");
                if (xb - xa > 40) sb.Append(Text((xa + xb) / 2, hy, Fr.TextInfo.ToTitleCase(m.ToString("MMM yyyy", Fr)), 11, "#1F4E79", "bold", "middle"));
            }
            // Semaines (lundis) : graduation et grille
            double gridBottom = Top + rows.Count * RowH;
            for (DateTime w = d0.AddDays(((int)DayOfWeek.Monday - (int)d0.DayOfWeek + 7) % 7); w < d1; w = w.AddDays(7))
            {
                double x = X(w);
                sb.Append("<line x1='" + N(x) + "' y1='" + N(hy + 8) + "' x2='" + N(x) + "' y2='" + N(gridBottom) + "' stroke='#E3E8EF' stroke-width='0.8'/>");
                if ((Right - Left) / span * 7 > 22) sb.Append(Text(x + 2, hy + 24, w.ToString("dd/MM", Fr), 8, "#6B7C93"));
            }

            // Lignes
            var y = new Dictionary<string, double>();
            for (int i = 0; i < rows.Count; i++)
            {
                double ry = Top + i * RowH;
                GanttRow r = rows[i];
                if (r.IsHeader)
                {
                    sb.Append("<rect x='0' y='" + N(ry) + "' width='" + N(Width) + "' height='" + N(RowH) + "' fill='#0B1F3A' fill-opacity='0.9'/>");
                    sb.Append(Text(16, ry + 17, r.Title, 11, "#FFFFFF", "bold"));
                    continue;
                }
                PlanTask t = r.Task;
                y[t.Id] = ry + RowH / 2;
                if (i % 2 == 0) sb.Append("<rect x='0' y='" + N(ry) + "' width='" + N(Width) + "' height='" + N(RowH) + "' fill='#F7F9FC'/>");
                sb.Append("<rect x='12' y='" + N(ry + 6) + "' width='6' height='13' rx='2' fill='" + t.Color + "'/>");
                sb.Append(Text(24, ry + 17, Short("Lot " + t.Lot + (t.Level != null ? " · " + t.Level : string.Empty) + " — " + t.LotDesignation + (t.IsDurationImposed ? " ✎" : string.Empty) + (t.DelayDays > 0 ? " (+" + t.DelayDays + " j)" : string.Empty), 56), 10.5, t.IsCritical ? "#B42318" : "#1B2430", t.IsCritical ? "bold" : null));
                sb.Append(Text(392, ry + 17, t.DurationDays + " j", 10.5, "#1B2430", null, "end"));
                sb.Append(Text(470, ry + 17, t.Start.ToString("dd/MM/yy", Fr), 10, "#56677D", null, "end"));
                sb.Append(Text(548, ry + 17, t.Finish.ToString("dd/MM/yy", Fr), 10, "#56677D", null, "end"));
                sb.Append(Text(598, ry + 17, t.IsCritical ? "critique" : t.FloatDays + " j", 9.5, t.IsCritical ? "#B42318" : "#56677D", t.IsCritical ? "bold" : null, "end"));
                double xa = X(t.Start), xb = Math.Max(xa + 3, X(t.Finish.AddDays(1)));
                sb.Append("<rect x='" + N(xa) + "' y='" + N(ry + 5) + "' width='" + N(xb - xa) + "' height='" + N(RowH - 10) + "' rx='3' fill='" + t.Color + "'" +
                          (t.IsCritical ? " stroke='#D92D20' stroke-width='2.2'" : " stroke='#1B2430' stroke-opacity='0.25' stroke-width='0.8'") + "/>");
                if (xb - xa > 34) sb.Append(Text((xa + xb) / 2, ry + 16.5, t.DurationDays + " j", 9, "#0B1F3A", "bold", "middle"));
            }
            // Liens de precedence (fin -> debut), sur la page
            foreach (GanttRow r in rows.Where(r => !r.IsHeader))
            {
                foreach (string p in r.Task.Predecessors)
                {
                    if (!y.ContainsKey(p)) continue;
                    PlanTask pt = all.First(a => a.Task?.Id == p).Task;
                    double x1 = X(pt.Finish.AddDays(1)), y1 = y[p], x2 = X(r.Task.Start), y2 = y[r.Task.Id];
                    string col = pt.IsCritical && r.Task.IsCritical ? "#D92D20" : "#8A99AD";
                    double xm = Math.Max(x1 + 4, Math.Min(x2 - 4, x1 + 8));
                    sb.Append("<path d='M" + N(x1) + "," + N(y1) + " H" + N(xm) + " V" + N(y2) + " H" + N(x2 - 1) + "' fill='none' stroke='" + col + "' stroke-width='0.9' stroke-opacity='0.8'/>");
                    sb.Append("<path d='M" + N(x2 - 5) + "," + N(y2 - 3) + " L" + N(x2) + "," + N(y2) + " L" + N(x2 - 5) + "," + N(y2 + 3) + " Z' fill='" + col + "'/>");
                }
            }
            // Jalon de reception (derniere page)
            if (page == pages)
            {
                double xr = X(plan.FinishDate.AddDays(1)), yr = Math.Min(gridBottom + 18, Height - 80);
                sb.Append("<path d='M" + N(xr) + "," + N(yr - 9) + " L" + N(xr + 9) + "," + N(yr) + " L" + N(xr) + "," + N(yr + 9) + " L" + N(xr - 9) + "," + N(yr) + " Z' fill='#0B1F3A'/>");
                sb.Append(Text(xr - 14, yr + 4, "Réception des travaux · " + plan.FinishDate.ToString("dd/MM/yyyy", Fr), 11, "#0B1F3A", "bold", "end"));
            }
            // Legende
            double ly = Height - 46;
            sb.Append("<rect x='0' y='" + N(ly - 16) + "' width='" + N(Width) + "' height='62' fill='#F3F5F8'/>");
            double lx = 24;
            foreach (var lot in all.Where(r => !r.IsHeader).Select(r => r.Task).GroupBy(t => t.Lot).Select(g => g.First()).OrderBy(t => t.Lot, StringComparer.Ordinal))
            {
                if (lx > Width - 200) break;
                sb.Append("<rect x='" + N(lx) + "' y='" + N(ly - 8) + "' width='14' height='10' rx='2' fill='" + lot.Color + "'/>");
                sb.Append(Text(lx + 18, ly + 1, "Lot " + lot.Lot, 10, "#1B2430"));
                lx += 70;
            }
            sb.Append("<rect x='" + N(Width - 360) + "' y='" + N(ly - 8) + "' width='14' height='10' rx='2' fill='#FFFFFF' stroke='#D92D20' stroke-width='2'/>");
            sb.Append(Text(Width - 340, ly + 1, "chemin critique (marge nulle)", 10, "#B42318", "bold"));
            sb.Append(Text(24, ly + 24, "Durées ESTIMÉES : quantités du BOQ par niveau ÷ rendements des équipes (config\\planning.json) ; dimanches et jours fériés chômés. " +
                "Enchaînement du niveau bas vers le niveau haut : gros œuvre (délai de décoffrage entre niveaux), maçonnerie, réseaux, menuiseries, finitions ; toiture, puis VRD.", 9.5, "#56677D"));
            sb.Append("</svg>");
            return sb.ToString();
        }

        private static string Short(string s, int max) => s == null ? string.Empty : s.Length <= max ? s : s.Substring(0, max - 1) + "…";

        private static string Text(double x, double y, string text, double size, string color, string weight = null, string anchor = null) =>
            "<text x='" + x.ToString("0.#", Inv) + "' y='" + y.ToString("0.#", Inv) + "' font-size='" + size.ToString("0.#", Inv) + "' fill='" + color + "'" +
            (weight != null ? " font-weight='" + weight + "'" : string.Empty) + (anchor != null ? " text-anchor='" + anchor + "'" : string.Empty) + ">" +
            SecurityElement.Escape(text ?? string.Empty) + "</text>";
    }
}
