using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security;
using System.Text;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Revisions;
using QuestPDF.Fluent;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// Planche « tableau de bord » dessinee sur mesure (SVG 1600 x 1000), meme rendu dans la
    /// fenetre (image), le PDF (page) et le classeur (image). Chaque graphique est lu dans les
    /// donnees du devis :
    ///   - bandeau DYNAMIQUE DU DEVIS : 4 mini-histogrammes avec tendance (historique des
    ///     revisions, prix renseignes par lot, part mesuree par nature, lignes sans prix par lot) ;
    ///   - EMPREINTE DU DEVIS : une barre par ligne du BOQ, a la couleur de son statut, et les voyants ;
    ///   - REPARTITION PAR LOT : histogramme multicolore et tableau des lots ;
    ///   - STATUT DU MONTANT : anneau segmente (mesure, estime, forfait, manquant) ;
    ///   - SANTE DU DEVIS : jauge radiale ;
    ///   - QUANTITES CLES : curseurs de la part mesuree ;
    ///   - PARETO : arcs concentriques des 5 articles les plus lourds ;
    ///   - PROFIL D'AUDIT : radar des constats par rubrique.
    /// Aucune valeur n'est inventee : un panneau sans donnee le dit.
    /// </summary>
    public static class DashboardBoard
    {
        public const int Width = 1600;
        public const int Height = 1000;

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        // Palette de la planche (fond nuit, couleurs franches).
        private const string BgTop = "#1C2B4D";
        private const string BgBottom = "#0E1730";
        private const string PanelFill = "#111B36";
        private const string PanelStroke = "#22335A";
        private const string Inner = "#17244A";
        private const string TitleColor = "#DFE7F5";
        private const string TextColor = "#E8EEF9";
        private const string Muted = "#7F92B8";
        private const string Dim = "#2A3A63";
        private const string Cyan = "#1EC8F0";
        private const string Yellow = "#FFC21A";
        private const string Green = "#4CC93F";
        private const string Pink = "#F0306E";
        private const string Purple = "#9B5BD4";
        private const string Blue = "#3C8DF0";
        private const string Orange = "#F59E0B";
        private const string Font = "Lato, 'Segoe UI', Arial, sans-serif";

        private static readonly string[] Vivid = { Cyan, Yellow, Green, Pink, Purple, Blue, Orange, "#2DD4BF", "#FF7A59", "#A3E635" };

        /// <summary>Planche complete.</summary>
        /// <param name="history">Revisions enregistrees, de la plus recente a la plus ancienne (peut etre vide).</param>
        public static string Svg(ExportContext ctx, IReadOnlyList<BoqSnapshot> history = null)
        {
            BoqDashboard d = ctx.Dashboard ?? throw new ArgumentException("Aucun BOQ.", nameof(ctx));
            history = history ?? new BoqSnapshot[0];
            BoqSnapshot previous = history.FirstOrDefault(h => h.Fingerprint != ctx.Fingerprint || h.TotalIncludingVat != ctx.Boq.TotalIncludingVat) ?? history.FirstOrDefault();
            var sb = new StringBuilder(64 * 1024);
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='" + Width + "' height='" + Height + "' viewBox='0 0 " + Width + " " + Height + "'>");
            sb.Append("<defs>");
            sb.Append("<linearGradient id='bg' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='" + BgTop + "'/><stop offset='1' stop-color='" + BgBottom + "'/></linearGradient>");
            foreach (string c in Vivid.Concat(new[] { Dim }).Distinct())
            {
                sb.Append("<linearGradient id='g" + c.TrimStart('#') + "' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='" + Lighten(c, 0.35) +
                          "'/><stop offset='1' stop-color='" + c + "'/></linearGradient>");
            }
            sb.Append("<radialGradient id='coin' cx='0.35' cy='0.3' r='0.8'><stop offset='0' stop-color='#4A5576'/><stop offset='1' stop-color='#1A2240'/></radialGradient>");
            sb.Append("</defs>");
            sb.Append("<rect width='" + Width + "' height='" + Height + "' fill='url(#bg)'/>");

            Header(sb, ctx, d, history, previous);
            Fingerprint(sb, ctx, d);
            Health(sb, d, previous);
            Distribution(sb, d);
            StatusDonut(sb, ctx, d, previous);
            KeyQuantities(sb, d);
            Pareto(sb, d);
            AuditRadar(sb, ctx, previous);
            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>Planche en image PNG (fenetre, classeur). scale 1 = 1600 x 1000 pixels.</summary>
        public static byte[] Png(ExportContext ctx, IReadOnlyList<BoqSnapshot> history = null, double scale = 1.0)
        {
            BoqPdfWriter.EnsureConfigured();
            string svg = Svg(ctx, history);
            return QuestPDF.Fluent.Document.Create(c => c.Page(p =>
            {
                p.Size(Width, Height);
                p.Margin(0);
                p.PageColor(BgBottom);
                p.Content().Svg(svg).FitArea();
            })).GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings
            {
                ImageFormat = QuestPDF.Infrastructure.ImageFormat.Png,
                RasterDpi = (int)Math.Round(72 * Math.Max(0.5, Math.Min(3.0, scale)))
            }).First();
        }

        // ------------------------------------------------------------------ bandeau
        private static void Header(StringBuilder sb, ExportContext ctx, BoqDashboard d, IReadOnlyList<BoqSnapshot> history, BoqSnapshot previous)
        {
            Panel(sb, 30, 30, 1080, 150, null);
            Text(sb, 66, 92, "DYNAMIQUE DU DEVIS", 26, TextColor, bold: true);
            Text(sb, 66, 126, Trunc(ctx.ProjectTitle, 34), 18, Muted);
            Text(sb, 66, 154, "Indice " + Or(ctx.Project?.Revision, "—") + "  ·  " + ctx.GeneratedAt.ToString("dd/MM/yyyy", Fr) + "  ·  " + d.Currency, 14, Muted);

            // 1. TTC : historique des revisions (+ l'etat actuel).
            var ttc = history.Take(5).Reverse().Select(h => (double)h.TotalIncludingVat).ToList();
            ttc.Add((double)d.TotalIncludingVat);
            if (ttc.Count == 1)
            {
                ttc = d.Lots.OrderBy(l => l.Code, StringComparer.Ordinal).Select(l => (double)l.Amount).ToList();
            }
            Mini(sb, 410, "TTC", Compact(d.TotalIncludingVat), ttc, Cyan,
                previous == null ? (null, "1re révision") : Delta((double)d.TotalIncludingVat, (double)previous.TotalIncludingVat, higherIsBetter: null));

            // 2. Prix renseignes : un baton par lot.
            var priced = d.Lots.Select(l => l.Lines == 0 ? 0.0 : 1.0 - (double)l.LinesWithoutPrice / l.Lines).ToList();
            double? prevCoverage = previous == null || previous.Lines.Count == 0 ? (double?)null : (double)previous.Lines.Count(l => l.Amount.HasValue) / previous.Lines.Count;
            Mini(sb, 580, "Prix", Pct(d.PriceCoverage), priced, Yellow,
                prevCoverage.HasValue ? DeltaPoints((double)d.PriceCoverage, prevCoverage.Value, higherIsBetter: true) : (null, d.PricedLineCount + "/" + d.LineCount + " lignes"));

            // 3. Part mesuree par nature de quantite.
            var measured = d.KeyQuantities.Select(k => (double)k.MeasuredShare).ToList();
            Mini(sb, 750, "Mesuré", d.ReliabilityIndex.HasValue ? Pct(d.ReliabilityIndex.Value) : "—", measured, Green,
                (null, d.KeyQuantities.Count + " natures"));

            // 4. Lignes sans prix par lot.
            var missing = d.Lots.Select(l => (double)l.LinesWithoutPrice).ToList();
            int? prevMissing = previous?.Lines.Count(l => !l.Amount.HasValue);
            Mini(sb, 920, "Sans prix", d.MissingPrices.Count.ToString(Fr), missing, Pink,
                prevMissing.HasValue ? DeltaCount(d.MissingPrices.Count, prevMissing.Value) : (null, "à chiffrer"));
        }

        private static void Mini(StringBuilder sb, double x, string label, string value, List<double> values, string color, (bool? up, string text) trend)
        {
            Text(sb, x, 96, label, 20, TextColor);
            if (trend.up.HasValue)
            {
                Triangle(sb, x + 6, 117, trend.up.Value);
                Text(sb, x + 18, 123, trend.text, 15, trend.up.Value ? Green : Pink);
            }
            else
            {
                Text(sb, x, 123, trend.text, 14, Muted);
            }
            Text(sb, x, 156, value, 15, Muted);
            values = values.Count == 0 ? new List<double> { 0 } : values.Skip(Math.Max(0, values.Count - 6)).ToList();
            double max = Math.Max(values.Max(), 1e-9);
            double bx = x + 88, bottom = 152, h = 78, w = 10, gap = 6;
            for (int i = 0; i < values.Count; i++)
            {
                double bh = Math.Max(4, values[i] / max * h);
                RoundBar(sb, bx + i * (w + gap), bottom - bh, w, bh, "url(#g" + color.TrimStart('#') + ")");
            }
        }

        // ------------------------------------------------------------------ empreinte et voyants
        private static void Fingerprint(StringBuilder sb, ExportContext ctx, BoqDashboard d)
        {
            Panel(sb, 30, 204, 270, 436, "VOYANTS");
            // Empreinte : une barre par ligne du BOQ (montant ou quantite relative), couleur du statut.
            var lines = ctx.Boq.AllLines.ToList();
            Rect(sb, 52, 266, 226, 160, Inner, 2);
            if (lines.Count > 0)
            {
                double max = Math.Max(1e-9, lines.Max(l => (double)(l.Amount ?? 0m)));
                bool byAmount = max > 1e-9;
                double qmax = Math.Max(1e-9, lines.Max(l => (double)l.Quantity));
                double bw = Math.Max(2, Math.Min(10, 210.0 / lines.Count - 1));
                double step = 210.0 / lines.Count;
                for (int i = 0; i < lines.Count; i++)
                {
                    double v = byAmount ? (double)(lines[i].Amount ?? 0m) / max : (double)lines[i].Quantity / qmax;
                    double bh = Math.Max(3, v * 130);
                    string c = lines[i].Amount.HasValue ? Palette.ForReliability(lines[i].Reliability) : Pink;
                    Rect(sb, 60 + i * step, 416 - bh, bw, bh, c, 0);
                }
                Text(sb, 165, 446, "une barre par ligne du BOQ" + (byAmount ? " (montant)" : " (quantité)"), 12, Muted, anchor: "middle");
            }
            int y = 486;
            foreach (DashboardSignal s in d.Signals.Take(5))
            {
                string c = s.Level == SignalLevel.Critique ? Pink : s.Level == SignalLevel.Attention ? Yellow : s.Level == SignalLevel.Info ? Cyan : Green;
                if (s.Level == SignalLevel.Critique || s.Level == SignalLevel.Attention)
                {
                    Rect(sb, 44, y - 18, 242, 26, s.Level == SignalLevel.Critique ? "#3A1F45" : "#2E2D3E", 2);
                }
                Text(sb, 54, y, Trunc(s.Title, 26), 15, TextColor);
                Triangle(sb, 268, y - 5, s.Level == SignalLevel.Ok || s.Level == SignalLevel.Info, c);
                y += 30;
            }
        }

        // ------------------------------------------------------------------ jauge radiale
        private static void Health(StringBuilder sb, BoqDashboard d, BoqSnapshot previous)
        {
            Panel(sb, 30, 664, 270, 306, "SANTÉ DU DEVIS");
            double cx = 165, cy = 830;
            int ticks = 64;
            int lit = (int)Math.Round(ticks * d.HealthScore / 100.0);
            for (int i = 0; i < ticks; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / ticks;
                // Longueur variable, deterministe : rythme visuel de la jauge.
                double r1 = 58 + (i % 3) * 3, r2 = 84 + ((i * 7) % 5) * 5;
                string c = i < lit ? Vivid[(i * 5 / ticks) % 4 == 0 ? 2 : (i * 5 / ticks) % 4 == 1 ? 1 : (i * 5 / ticks) % 4 == 2 ? 3 : 0] : Dim;
                if (i < lit)
                {
                    double t = (double)i / Math.Max(1, lit);
                    c = t < 0.25 ? Cyan : t < 0.5 ? Green : t < 0.75 ? Yellow : Orange;
                }
                sb.AppendFormat(Inv, "<line x1='{0:0.##}' y1='{1:0.##}' x2='{2:0.##}' y2='{3:0.##}' stroke='{4}' stroke-width='5' stroke-linecap='round'/>",
                    cx + r1 * Math.Cos(a), cy + r1 * Math.Sin(a), cx + r2 * Math.Cos(a), cy + r2 * Math.Sin(a), c);
            }
            Text(sb, cx, cy + 4, d.HealthScore.ToString(Fr), 40, TextColor, bold: true, anchor: "middle");
            Text(sb, cx, cy + 28, "/ 100", 15, Muted, anchor: "middle");
            Text(sb, cx, 950, "prix 50 · fiabilité 30 · audit 20", 13, Muted, anchor: "middle");
        }

        // ------------------------------------------------------------------ repartition par lot
        private static void Distribution(StringBuilder sb, BoqDashboard d)
        {
            Panel(sb, 324, 204, 786, 266, null);
            Rect(sb, 352, 226, 380, 196, Inner, 2);
            var lots = d.Lots.Take(9).ToList();
            double max = lots.Count == 0 ? 0 : (double)lots.Max(l => l.Amount);
            bool amounts = max > 0;
            if (!amounts) max = lots.Count == 0 ? 1 : lots.Max(l => l.Lines);
            double slot = lots.Count == 0 ? 1 : 360.0 / lots.Count;
            double bw = Math.Min(30, slot * 0.66);
            for (int i = 0; i < lots.Count; i++)
            {
                double v = amounts ? (double)lots[i].Amount : lots[i].Lines;
                double bh = Math.Max(6, v / Math.Max(max, 1e-9) * 172);
                double bx = 362 + i * slot + (slot - bw) / 2;
                string color = Palette.ForLot(lots[i].Code);
                RoundBar(sb, bx, 414 - bh, bw, bh, color, 1);
                if (bh >= 24)
                    Text(sb, bx + bw / 2, 410, lots[i].Code, 11, "#0E1730", bold: true, anchor: "middle");
                else
                    Text(sb, bx + bw / 2, 414 - bh - 6, lots[i].Code, 11, color, bold: true, anchor: "middle");
                Text(sb, bx + bw / 2, 442, amounts ? (lots[i].Share * 100m).ToString("0.#", Fr) : lots[i].Lines.ToString(Fr), 12, Muted, anchor: "middle");
            }
            if (!amounts)
            {
                Text(sb, 542, 300, "aucun montant : hauteur = nombre de lignes", 13, Muted, anchor: "middle");
            }

            Text(sb, 930, 254, "RÉPARTITION PAR LOT", 22, TitleColor, bold: true, anchor: "middle");
            double ty = 290;
            Text(sb, 776, ty, "Lot", 13, TextColor, bold: true);
            Text(sb, 952, ty, "HT", 13, TextColor, bold: true, anchor: "end");
            Text(sb, 1010, ty, "Part", 13, TextColor, bold: true, anchor: "end");
            Text(sb, 1086, ty, "Sans prix", 13, TextColor, bold: true, anchor: "end");
            LotShare top = d.Lots.OrderByDescending(l => l.Amount).FirstOrDefault();
            ty += 24;
            foreach (LotShare l in d.Lots.Take(6))
            {
                if (l == top && amounts) Rect(sb, 758, ty - 16, 340, 22, "#23396B", 0);
                Rect(sb, 762, ty - 10, 9, 9, Palette.ForLot(l.Code), 1);
                Text(sb, 776, ty, Trunc(l.Code + " " + l.Designation, 22), 12.5, TextColor);
                Text(sb, 952, ty, Compact(l.Amount), 12.5, Cyan, anchor: "end");
                Text(sb, 1010, ty, (l.Share * 100m).ToString("0.0", Fr), 12.5, Muted, anchor: "end");
                Text(sb, 1086, ty, l.LinesWithoutPrice.ToString(Fr), 12.5, l.LinesWithoutPrice > 0 ? Pink : Green, anchor: "end");
                ty += 24;
            }
            if (d.Lots.Count > 6) Text(sb, 776, ty, "+ " + (d.Lots.Count - 6) + " autre(s) lot(s)", 12, Muted);
        }

        // ------------------------------------------------------------------ anneau des statuts
        private static void StatusDonut(StringBuilder sb, ExportContext ctx, BoqDashboard d, BoqSnapshot previous)
        {
            Panel(sb, 1134, 30, 436, 440, "STATUT DU MONTANT");
            var lines = ctx.Boq.AllLines.ToList();
            var groups = new[]
            {
                ("Mesuré", Green, lines.Where(l => l.Amount.HasValue && l.Reliability == Reliability.Measured)),
                ("Estimé", Yellow, lines.Where(l => l.Amount.HasValue && l.Reliability == Reliability.Estimated)),
                ("Forfait", Purple, lines.Where(l => l.Amount.HasValue && l.Reliability == Reliability.Manual)),
                ("Sans prix", Pink, lines.Where(l => !l.Amount.HasValue))
            };
            bool byAmount = d.WorksTotal > 0m;
            var parts = groups.Select(g => (g.Item1, g.Item2, value: byAmount
                ? (g.Item1 == "Sans prix" ? 0.0 : (double)g.Item3.Sum(l => l.Amount ?? 0m))
                : g.Item3.Count())).ToList();
            if (byAmount && lines.Any(l => !l.Amount.HasValue))
            {
                // Les lignes sans prix n'ont pas de montant : on les montre par leur nombre, en legende.
            }
            double total = parts.Sum(p => p.value);
            double cx = 1352, cy = 214, r = 98;
            double circ = 2 * Math.PI * r;
            double start = -Math.PI / 2;
            int k = 0;
            foreach (var p in parts.Where(p => p.value > 0))
            {
                double frac = total <= 0 ? 0 : p.value / total;
                double a0 = start, a1 = start + frac * 2 * Math.PI;
                double width = 30 + (k % 2) * 12;           // segments alternes en relief, comme une planche de designer
                Arc(sb, cx, cy, r + (k % 2) * 6, a0 + 0.012, a1 - 0.012, width, p.Item2);
                double am = (a0 + a1) / 2;
                Text(sb, cx + (r + 46) * Math.Cos(am), cy + (r + 46) * Math.Sin(am) + 5, (frac * 100).ToString("0.0", Fr) + " %", 15, p.Item2, bold: true, anchor: "middle");
                start = a1;
                k++;
            }
            if (total <= 0)
            {
                Arc(sb, cx, cy, r, 0, 2 * Math.PI - 0.001, 30, Dim);
            }
            sb.AppendFormat(Inv, "<circle cx='{0}' cy='{1}' r='32' fill='url(#coin)' stroke='#5A6688' stroke-width='2'/>", cx, cy - 14);
            Text(sb, cx, cy - 3, d.Currency == "CDF" ? "FC" : "$", 30, "#C9D2E6", bold: true, anchor: "middle");
            Text(sb, cx, cy + 46, Compact(d.TotalIncludingVat), 19, TextColor, bold: true, anchor: "middle");
            Text(sb, 1352, 384, byAmount ? "part du montant des travaux" : "aucun montant : part des lignes", 12, Muted, anchor: "middle");
            double lx = 1160;
            foreach (var p in parts)
            {
                Rect(sb, lx, 396, 10, 10, p.Item2, 2);
                Text(sb, lx + 15, 405, p.Item1, 12, TextColor);
                lx += 100;
            }
            // Pied : HT, TVA, majorations, cout au m2, avec tendance vs revision precedente.
            var foot = new[]
            {
                ("HT", d.TotalExcludingVat, previous?.TotalExcludingVat),
                ("TVA", d.Vat, previous?.Vat),
                ("Majorations", d.MarkupsTotal, previous == null ? (decimal?)null : previous.TotalExcludingVat - previous.WorksTotal),
                ("Coût/m²", d.CostPerSquareMeter ?? 0m, (decimal?)null)
            };
            double fx = 1180;
            foreach (var f in foot)
            {
                Text(sb, fx, 430, f.Item1, 13, Muted, anchor: "middle");
                Text(sb, fx, 452, f.Item1 == "Coût/m²" && !d.CostPerSquareMeter.HasValue ? "—" : Compact(f.Item2), 15, TextColor, bold: true, anchor: "middle");
                fx += 112;
            }
        }

        // ------------------------------------------------------------------ quantites cles
        private static void KeyQuantities(StringBuilder sb, BoqDashboard d)
        {
            Panel(sb, 324, 494, 306, 476, "QUANTITÉS CLÉS");
            var items = d.KeyQuantities.Take(6).ToList();
            double y = 574;
            for (int i = 0; i < items.Count; i++)
            {
                KeyQuantity q = items[i];
                string c = Vivid[i % Vivid.Length];
                Text(sb, 346, y + 5, Trunc(q.Label, 11), 13, c, bold: true);
                double x0 = 438, x1 = 606;
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2}' y2='{1}' stroke='{3}' stroke-width='2'/>", x0, y, x1, Dim);
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{0}' y2='{2}' stroke='{3}' stroke-width='2'/><line x1='{4}' y1='{1}' x2='{4}' y2='{2}' stroke='{3}' stroke-width='2'/>",
                    x0, y - 6, y + 6, Dim, x1);
                double xm = x0 + (x1 - x0) * (double)q.MeasuredShare;
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2:0.##}' y2='{1}' stroke='{3}' stroke-width='3'/>", x0, y, xm, c);
                sb.AppendFormat(Inv, "<circle cx='{0:0.##}' cy='{1}' r='10' fill='none' stroke='{2}' stroke-width='3'/><circle cx='{0:0.##}' cy='{1}' r='5' fill='{2}'/>", xm, y, c);
                y += 50;
            }
            if (items.Count == 0) Text(sb, 477, 640, "aucune quantité clé", 14, Muted, anchor: "middle");
            sb.AppendFormat(Inv, "<circle cx='392' cy='880' r='7' fill='none' stroke='{0}' stroke-width='2'/>", Muted);
            Text(sb, 405, 885, "part mesurée (le reste est estimé)", 12, Muted);
            KeyQuantity main = items.FirstOrDefault(q => q.Kind == QuantityKinds.ConcreteVolume) ?? items.FirstOrDefault();
            if (d.RebarRatio.HasValue)
            {
                Text(sb, 477, 920, "Ratio acier / béton", 15, Blue, anchor: "middle");
                Text(sb, 477, 954, d.RebarRatio.Value.ToString("N0", Fr) + " kg/m³", 28, TextColor, bold: true, anchor: "middle");
            }
            else if (main != null)
            {
                Text(sb, 477, 920, main.Label, 15, Blue, anchor: "middle");
                Text(sb, 477, 954, main.Value.ToString("N1", Fr) + " " + main.Unit.Symbol(), 28, TextColor, bold: true, anchor: "middle");
            }
        }

        // ------------------------------------------------------------------ pareto en arcs
        private static void Pareto(StringBuilder sb, BoqDashboard d)
        {
            Panel(sb, 654, 494, 306, 476, "PARETO");
            var top = d.Pareto.Take(5).ToList();
            double cxL = 748, cy = 700;
            string[] letters = { "A", "B", "C", "D", "E" };
            if (top.Count == 0)
            {
                Text(sb, 807, 700, "aucun montant :", 15, Muted, anchor: "middle");
                Text(sb, 807, 722, "saisir les prix unitaires", 15, Muted, anchor: "middle");
            }
            for (int i = 0; i < top.Count; i++)
            {
                double r = 92 - i * 16;
                string c = Vivid[i];
                double lenTop = 20 + (double)top[i].Share * 220;
                double lenBottom = 20 + (double)top[i].CumulativeShare * 120;
                // Arc en « C » ouvert a droite : segment haut (part), demi-cercle, segment bas (cumul).
                sb.AppendFormat(Inv, "<path d='M {0:0.##} {1:0.##} L {2:0.##} {1:0.##} A {3:0.##} {3:0.##} 0 0 0 {2:0.##} {4:0.##} L {5:0.##} {4:0.##}' fill='none' stroke='{6}' stroke-width='7' stroke-linecap='round'/>",
                    cxL + lenTop, cy - r, cxL, r, cy + r, cxL + lenBottom, c);
                sb.AppendFormat(Inv, "<circle cx='{0:0.##}' cy='{1:0.##}' r='7' fill='{2}'/><circle cx='{3:0.##}' cy='{4:0.##}' r='7' fill='{2}'/>",
                    cxL + lenTop, cy - r, c, cxL + lenBottom, cy + r);
                Text(sb, cxL + lenTop + 14, cy - r + 5, letters[i], 13, c, bold: true);
            }
            double ly = 836;
            for (int i = 0; i < top.Count; i++)
            {
                double lx = 678 + (i % 3) * 92;
                if (i == 3) ly += 20;
                sb.AppendFormat(Inv, "<circle cx='{0}' cy='{1}' r='5' fill='{2}'/>", lx, ly - 4, Vivid[i]);
                Text(sb, lx + 10, ly, letters[i] + " " + Trunc(top[i].Code, 8), 12, Muted);
            }
            if (top.Count > 0)
            {
                Text(sb, 807, 902, top.Count + " premiers articles", 15, Blue, anchor: "middle");
                Text(sb, 807, 938, Pct(top.Last().CumulativeShare), 30, TextColor, bold: true, anchor: "middle");
                Text(sb, 807, 958, "du montant des travaux", 12, Muted, anchor: "middle");
            }
        }

        // ------------------------------------------------------------------ radar d'audit
        private static void AuditRadar(StringBuilder sb, ExportContext ctx, BoqSnapshot previous)
        {
            Panel(sb, 984, 494, 586, 476, "PROFIL D'AUDIT");
            var sections = new (AuditSection s, string label)[]
            {
                (AuditSection.Materiaux, "Matériaux"), (AuditSection.BetonSansClasse, "Béton"), (AuditSection.Armatures, "Armatures"),
                (AuditSection.Profils, "Profilés"), (AuditSection.PiecesEtMurs, "Pièces / murs"), (AuditSection.Doublons, "Doublons"),
                (AuditSection.NonClasses, "Non classés"), (AuditSection.Liens, "Liens"), (AuditSection.AutresAnomalies, "Autres")
            };
            var findings = ctx.Audit?.Findings ?? new List<AuditFinding>();
            double[] warn = sections.Select(x => (double)findings.Count(f => f.Section == x.s && f.Severity == IssueSeverity.Warning)).ToArray();
            double[] err = sections.Select(x => (double)findings.Count(f => f.Section == x.s && f.Severity == IssueSeverity.Error)).ToArray();
            double[] elems = sections.Select(x => (double)findings.Where(f => f.Section == x.s).Sum(f => f.ElementIds.Count)).ToArray();
            double cx = 1196, cy = 742, R = 150;
            int n = sections.Length;
            foreach (double f in new[] { 0.33, 0.66, 1.0 })
            {
                sb.AppendFormat(Inv, "<circle cx='{0}' cy='{1}' r='{2:0.##}' fill='none' stroke='{3}' stroke-width='1' stroke-dasharray='2 6'/>", cx, cy, R * f, Dim);
            }
            for (int i = 0; i < n; i++)
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2:0.##}' y2='{3:0.##}' stroke='{4}' stroke-width='1.2' stroke-dasharray='4 5'/>",
                    cx, cy, cx + (R + 18) * Math.Cos(a), cy + (R + 18) * Math.Sin(a), Dim);
                string anchor = Math.Abs(Math.Cos(a)) < 0.2 ? "middle" : Math.Cos(a) > 0 ? "start" : "end";
                Text(sb, cx + (R + 28) * Math.Cos(a), cy + (R + 28) * Math.Sin(a) + 4, sections[i].label, 12.5, Muted, anchor: anchor);
            }
            Blob(sb, cx, cy, R, Normalize(elems), Purple, 0.10);
            Blob(sb, cx, cy, R, Normalize(warn), Yellow, 0.18);
            Blob(sb, cx, cy, R, Normalize(err), Pink, 0.26);
            sb.AppendFormat(Inv, "<circle cx='{0}' cy='{1}' r='5' fill='{2}'/>", cx, cy, Green);

            int errors = ctx.Audit?.ErrorCount ?? 0, warnings = ctx.Audit?.WarningCount ?? 0;
            Text(sb, 1480, 620, "Erreurs", 18, Pink, anchor: "middle");
            Text(sb, 1480, 660, errors.ToString(Fr), 34, TextColor, bold: true, anchor: "middle");
            Text(sb, 1480, 740, "Avertissements", 18, Yellow, anchor: "middle");
            Text(sb, 1480, 780, warnings.ToString(Fr), 34, TextColor, bold: true, anchor: "middle");
            Text(sb, 1480, 850, "Éléments signalés", 15, Purple, anchor: "middle");
            Text(sb, 1480, 880, ((int)elems.Sum()).ToString("N0", Fr), 22, TextColor, bold: true, anchor: "middle");
            Text(sb, 1196, 950, "rose : erreurs · jaune : avertissements · violet : éléments", 12, Muted, anchor: "middle");
        }

        // ------------------------------------------------------------------ outils de dessin
        private static double[] Normalize(double[] v)
        {
            double max = v.Length == 0 ? 0 : v.Max();
            // Un rayon minimal garde la forme lisible meme sans constat.
            // Racine carree : une rubrique dominante n'ecrase pas les autres (forme lisible).
            return v.Select(x => max <= 0 ? 0.16 : 0.2 + 0.8 * Math.Sqrt(x / max)).ToArray();
        }

        /// <summary>Contour lisse ferme (Catmull-Rom converti en Bezier) sur les axes du radar.</summary>
        private static void Blob(StringBuilder sb, double cx, double cy, double R, double[] v, string color, double fillOpacity)
        {
            int n = v.Length;
            var pts = Enumerable.Range(0, n).Select(i =>
            {
                double a = -Math.PI / 2 + 2 * Math.PI * i / n;
                return (x: cx + R * v[i] * Math.Cos(a), y: cy + R * v[i] * Math.Sin(a));
            }).ToArray();
            var path = new StringBuilder();
            path.AppendFormat(Inv, "M {0:0.##} {1:0.##} ", pts[0].x, pts[0].y);
            for (int i = 0; i < n; i++)
            {
                var p0 = pts[(i - 1 + n) % n];
                var p1 = pts[i];
                var p2 = pts[(i + 1) % n];
                var p3 = pts[(i + 2) % n];
                path.AppendFormat(Inv, "C {0:0.##} {1:0.##} {2:0.##} {3:0.##} {4:0.##} {5:0.##} ",
                    p1.x + (p2.x - p0.x) / 6, p1.y + (p2.y - p0.y) / 6, p2.x - (p3.x - p1.x) / 6, p2.y - (p3.y - p1.y) / 6, p2.x, p2.y);
            }
            sb.AppendFormat(Inv, "<path d='{0}Z' fill='{1}' fill-opacity='{2:0.##}' stroke='{1}' stroke-width='4' stroke-linejoin='round'/>", path, color, fillOpacity);
        }

        private static void Arc(StringBuilder sb, double cx, double cy, double r, double a0, double a1, double width, string color)
        {
            if (a1 - a0 >= 2 * Math.PI - 0.01)
            {
                sb.AppendFormat(Inv, "<circle cx='{0}' cy='{1}' r='{2:0.##}' fill='none' stroke='{3}' stroke-width='{4:0.##}'/>", cx, cy, r, color, width);
                return;
            }
            int large = a1 - a0 > Math.PI ? 1 : 0;
            sb.AppendFormat(Inv, "<path d='M {0:0.##} {1:0.##} A {2:0.##} {2:0.##} 0 {3} 1 {4:0.##} {5:0.##}' fill='none' stroke='{6}' stroke-width='{7:0.##}'/>",
                cx + r * Math.Cos(a0), cy + r * Math.Sin(a0), r, large, cx + r * Math.Cos(a1), cy + r * Math.Sin(a1), color, width);
        }

        private static void Panel(StringBuilder sb, double x, double y, double w, double h, string title)
        {
            sb.AppendFormat(Inv, "<rect x='{0}' y='{1}' width='{2}' height='{3}' rx='4' fill='{4}' stroke='{5}' stroke-width='1.2'/>", x, y, w, h, PanelFill, PanelStroke);
            if (title != null)
            {
                Text(sb, x + w / 2, y + 50, title, 22, TitleColor, bold: true, anchor: "middle");
            }
        }

        private static void Rect(StringBuilder sb, double x, double y, double w, double h, string fill, double rx) =>
            sb.AppendFormat(Inv, "<rect x='{0:0.##}' y='{1:0.##}' width='{2:0.##}' height='{3:0.##}' rx='{4:0.##}' fill='{5}'/>", x, y, w, h, rx, fill);

        private static void RoundBar(StringBuilder sb, double x, double y, double w, double h, string fill, double rx = 4) =>
            Rect(sb, x, y, w, h, fill, Math.Min(rx, w / 2));

        private static void Triangle(StringBuilder sb, double x, double y, bool up, string color = null)
        {
            string c = color ?? (up ? Green : Pink);
            string d = up
                ? string.Format(Inv, "M {0:0.##} {1:0.##} L {2:0.##} {3:0.##} L {4:0.##} {3:0.##} Z", x, y - 6, x + 7, y + 6, x - 7)
                : string.Format(Inv, "M {0:0.##} {1:0.##} L {2:0.##} {3:0.##} L {4:0.##} {3:0.##} Z", x, y + 6, x + 7, y - 6, x - 7);
            sb.Append("<path d='" + d + "' fill='" + c + "'/>");
        }

        private static void Text(StringBuilder sb, double x, double y, string text, double size, string color, bool bold = false, string anchor = "start")
        {
            sb.AppendFormat(Inv, "<text x='{0:0.##}' y='{1:0.##}' font-family=\"{2}\" font-size='{3:0.##}' fill='{4}'{5} text-anchor='{6}'>{7}</text>",
                x, y, Font, size, color, bold ? " font-weight='bold'" : string.Empty, anchor, SecurityElement.Escape(text ?? string.Empty));
        }

        private static (bool?, string) Delta(double now, double before, bool? higherIsBetter)
        {
            if (before == 0) return (null, "nouvelle base");
            double p = (now - before) / before * 100;
            bool up = p >= 0;
            return (up, (up ? "+" : "") + p.ToString("0.0", Fr) + " %");
        }

        private static (bool?, string) DeltaPoints(double now, double before, bool higherIsBetter)
        {
            double pts = (now - before) * 100;
            return (pts >= 0, (pts >= 0 ? "+" : "") + pts.ToString("0", Fr) + " pts");
        }

        private static (bool?, string) DeltaCount(int now, int before)
        {
            int diff = now - before;
            // Moins de lignes sans prix = progres (fleche montante verte).
            return (diff <= 0, (diff > 0 ? "+" : "") + diff.ToString(Fr) + " ligne(s)");
        }

        private static string Compact(decimal v)
        {
            decimal a = Math.Abs(v);
            if (a >= 1_000_000m) return (v / 1_000_000m).ToString("0.##", Fr) + " M";
            if (a >= 10_000m) return (v / 1000m).ToString("0.#", Fr) + " k";
            return v.ToString("N0", Fr);
        }

        private static string Pct(decimal share) => (share * 100m).ToString("0.#", Fr) + " %";

        private static string Trunc(string s, int max) =>
            string.IsNullOrEmpty(s) ? string.Empty : (s.Length <= max ? s : s.Substring(0, max - 1).TrimEnd() + "…");

        private static string Or(string v, string f) => string.IsNullOrWhiteSpace(v) ? f : v;

        private static string Lighten(string hex, double amount)
        {
            int r = Convert.ToInt32(hex.Substring(1, 2), 16), g = Convert.ToInt32(hex.Substring(3, 2), 16), b = Convert.ToInt32(hex.Substring(5, 2), 16);
            int L(int c) => (int)Math.Round(c + (255 - c) * amount);
            return "#" + L(r).ToString("X2") + L(g).ToString("X2") + L(b).ToString("X2");
        }
    }
}
