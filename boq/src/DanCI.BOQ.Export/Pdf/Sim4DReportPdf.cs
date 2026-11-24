using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DanCI.BOQ.Core.Planning;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static DanCI.BOQ.Export.Pdf.PdfTheme;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// Rapport de simulation 4D (PDF, A4 paysage, theme de la page de garde) :
    /// 1. synthese (dates, duree, taches, montant, reglages, deroulement, points d'attention) avec la
    ///    courbe en S et l'histogramme hebdomadaire ; 2. captures de la vue 3D pendant la simulation ;
    /// 3. diagramme de Gantt ; 4. tableau des taches et jalons. Rien n'est invente : tout vient du
    /// planning simule et de ce que Revit a reellement affiche.
    /// </summary>
    public static class Sim4DReportPdf
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        static Sim4DReportPdf()
        {
            QuestPDF.Settings.License = LicenseType.Community;
        }

        public static void Write(Sim4DReport report, string path) => File.WriteAllBytes(path, Generate(report));

        public static byte[] Generate(Sim4DReport report) => Compose(report).GeneratePdf();

        public static IReadOnlyList<byte[]> PageImages(Sim4DReport report, int dpi = 60) =>
            Compose(report).GenerateImages(new ImageGenerationSettings { RasterDpi = dpi, ImageFormat = ImageFormat.Png }).ToList();

        public static IDocument Compose(Sim4DReport r)
        {
            if (r?.Plan == null) throw new ArgumentException("Aucun planning simulé.", nameof(r));
            ExportContext ctx = r.Context ?? new ExportContext();
            PdfTheme t = For(ctx.Cover);
            WorkPlan plan = r.Plan;
            string cur = ctx.Boq?.Currency.ToString() ?? string.Empty;

            return Document.Create(doc =>
            {
                // ---- 1. Synthese
                doc.Page(page =>
                {
                    Frame(page, t, ctx, r, "Synthèse");
                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Spacing(8);
                        col.Item().Row(row =>
                        {
                            row.Spacing(8);
                            Card(row, t, "DÉBUT DES TRAVAUX", plan.StartDate.ToString("dd/MM/yyyy", Fr), plan.StartDate.ToString("dddd", Fr));
                            Card(row, t, "RÉCEPTION", plan.FinishDate.ToString("dd/MM/yyyy", Fr), plan.FinishDate.ToString("dddd", Fr));
                            Card(row, t, "DURÉE", plan.TotalDays + " j ouvrés", plan.CalendarDays + " j calendaires · " + plan.WorkingDaysPerWeek + " j/sem.");
                            Card(row, t, "TÂCHES", plan.Tasks.Count.ToString(Fr), plan.Tasks.Count(x => x.IsCritical) + " critique(s) · " + plan.Milestones.Count + " jalon(s)");
                            Card(row, t, "ÉLÉMENTS SIMULÉS", r.SimulatedElements.ToString("N0", Fr), plan.Tasks.Select(x => x.Lot).Distinct().Count() + " lot(s)");
                            Card(row, t, "MONTANT PLANIFIÉ", plan.TotalAmount.ToString("N0", Fr) + " " + cur, "réparti sur la durée des tâches");
                        });
                        col.Item().Height(205).Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                            {
                                c.Item().Text("COURBE EN S — avancement cumulé (montant)").Bold().FontSize(8.5f).FontColor(t.Accent);
                                c.Item().PaddingTop(4).Svg(SCurve(plan, t)).FitArea();
                            }));
                            row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                            {
                                c.Item().Text("HISTOGRAMME HEBDOMADAIRE — montant par semaine").Bold().FontSize(8.5f).FontColor(t.Accent);
                                c.Item().PaddingTop(4).Svg(Weekly(plan, t)).FitArea();
                            }));
                        });
                        col.Item().Row(row =>
                        {
                            row.Spacing(8);
                            row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                            {
                                c.Item().Text("RÉGLAGES DE LA SIMULATION").Bold().FontSize(8.5f).FontColor(t.Accent);
                                foreach (var kv in r.Settings) Line(c, t, kv.Key, kv.Value);
                            }));
                            row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                            {
                                c.Item().Text("DÉROULEMENT").Bold().FontSize(8.5f).FontColor(t.Accent);
                                Line(c, t, "Lecture", r.Completed ? "terminée — bâtiment complet, vue de face" : "interrompue avant la fin du chantier");
                                Line(c, t, "Images dessinées par Revit", r.Frames.ToString("N0", Fr));
                                Line(c, t, "Cadence moyenne", r.AverageFrameMs > 0 ? (1000.0 / r.AverageFrameMs).ToString("0.0", Fr) + " image(s)/s (" + r.AverageFrameMs.ToString("0", Fr) + " ms par image)" : "—");
                                Line(c, t, "Durée de lecture", r.PlayTime.TotalSeconds > 0 ? ((int)r.PlayTime.TotalMinutes) + " min " + r.PlayTime.Seconds.ToString("00", Inv) + " s" : "—");
                                Line(c, t, "Captures de la vue 3D", r.Snapshots.Count.ToString(Fr));
                                Line(c, t, "Vidéo", string.IsNullOrEmpty(r.VideoPath) ? "non enregistrée" : Path.GetFileName(r.VideoPath));
                            }));
                            row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                            {
                                c.Item().Text("POINTS D'ATTENTION").Bold().FontSize(8.5f).FontColor(t.Accent2);
                                var w = Attention(r).ToList();
                                if (w.Count == 0) c.Item().PaddingTop(3).Text("Aucun : tous les éléments du planning ont été construits et affichés.").FontColor(t.Ok);
                                foreach (string s in w.Take(9)) c.Item().PaddingTop(3).Text("• " + Clean(s)).FontSize(7.5f);
                                if (w.Count > 9) c.Item().PaddingTop(3).Text("… et " + (w.Count - 9) + " autre(s).").FontSize(7.5f).FontColor(t.Muted);
                            }));
                        });
                    });
                });

                // ---- 2. Captures de la vue 3D (2 par page)
                var shots = r.Snapshots.Where(s => s?.Jpeg != null && s.Jpeg.Length > 0).ToList();
                for (int i = 0; i < shots.Count; i += 2)
                {
                    var pair = shots.Skip(i).Take(2).ToList();
                    int first = i;
                    doc.Page(page =>
                    {
                        Frame(page, t, ctx, r, "Construction pas à pas — captures de la vue 3D");
                        page.Content().PaddingTop(8).Row(row =>
                        {
                            row.Spacing(10);
                            for (int k = 0; k < pair.Count; k++)
                            {
                                Sim4DSnapshot s = pair[k];
                                int n = first + k + 1;
                                row.RelativeItem().Column(c =>
                                {
                                    c.Item().Row(h =>
                                    {
                                        h.ConstantItem(26).Height(18).Background(t.Accent).AlignCenter().AlignMiddle().Text(n.ToString(Inv)).Bold().FontColor(t.Dark ? "#06121F" : "#FFFFFF");
                                        h.RelativeItem().PaddingLeft(6).AlignMiddle().Text(Clean(s.Caption)).Bold().FontSize(9.5f);
                                        h.ConstantItem(60).AlignRight().AlignMiddle().Text((s.Progress * 100).ToString("0", Fr) + " %").Bold().FontColor(t.Accent2);
                                    });
                                    c.Item().PaddingTop(5).Height(370).Border(1).BorderColor(t.PanelBorder).Background("#000000").AlignCenter().AlignMiddle().Image(s.Jpeg).FitArea();
                                });
                            }
                            if (pair.Count == 1) row.RelativeItem();
                        });
                    });
                }

                // ---- 3. Gantt
                foreach (string svg in GanttChart.Pages(plan, Clean(ctx.ProjectTitle)))
                {
                    doc.Page(page =>
                    {
                        page.Size(PageSizes.A4.Landscape());
                        page.Margin(0);
                        page.Background().Svg(svg);
                        page.Content().Text(string.Empty);
                    });
                }

                // ---- 4. Taches et jalons
                doc.Page(page =>
                {
                    Frame(page, t, ctx, r, "Tâches simulées");
                    page.Content().PaddingTop(8).Column(col =>
                    {
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(34); c.RelativeColumn(3); c.RelativeColumn(1.3f); c.ConstantColumn(58); c.ConstantColumn(58);
                                c.ConstantColumn(40); c.ConstantColumn(46); c.ConstantColumn(80); c.ConstantColumn(56);
                            });
                            table.Header(h =>
                            {
                                foreach (string s in new[] { "Lot", "Tâche", "Niveau", "Début", "Fin", "Durée", "Élém.", "Montant (" + cur + ")", "Marge" })
                                    h.Cell().Background(t.Dark ? "#12304F" : t.Accent).Padding(3).Text(s).Bold().FontSize(7.5f).FontColor("#FFFFFF");
                            });
                            int z = 0;
                            foreach (PlanTask x in plan.Tasks.OrderBy(x => x.StartDay).ThenBy(x => x.Lot, StringComparer.Ordinal))
                            {
                                bool zebra = z++ % 2 == 1;
                                string bg = zebra ? (t.Dark ? "#0F1A2E" : "#F3F6FB") : t.Page;
                                IContainer C() => table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(t.PanelBorder).PaddingVertical(2).PaddingHorizontal(3);
                                C().Text(x.Lot).FontSize(7.5f).FontColor(x.Color ?? t.Text);
                                C().Text(Clean(x.Name) + (x.IsStartImposed || x.IsFinishImposed ? "  (date imposée)" : string.Empty)).FontSize(7.5f);
                                C().Text(Clean(x.Level ?? "—")).FontSize(7.5f);
                                C().Text(x.Start.ToString("dd/MM/yy", Fr)).FontSize(7.5f);
                                C().Text(x.Finish.ToString("dd/MM/yy", Fr)).FontSize(7.5f);
                                C().AlignRight().Text(x.DurationDays + " j").FontSize(7.5f);
                                C().AlignRight().Text(x.Elements.Count.ToString(Fr)).FontSize(7.5f);
                                C().AlignRight().Text(x.Amount.ToString("N0", Fr)).FontSize(7.5f);
                                C().AlignRight().Text(x.IsCritical ? "critique" : x.FloatDays + " j").FontSize(7.5f).FontColor(x.IsCritical ? t.Critical : t.Muted);
                            }
                        });
                        if (plan.Milestones.Count > 0)
                        {
                            col.Item().PaddingTop(10).Text("JALONS").Bold().FontColor(t.Accent);
                            foreach (PlanMilestone m in plan.Milestones.OrderBy(m => m.Day))
                                col.Item().PaddingTop(2).Text("• " + m.Date.ToString("dd/MM/yyyy", Fr) + " — " + Clean(m.Name) + (string.IsNullOrEmpty(m.Level) ? string.Empty : " (" + Clean(m.Level) + ")")).FontSize(8);
                        }
                    });
                });
            });
        }

        // ---------------------------------------------------------------- elements de page
        private static void Frame(PageDescriptor page, PdfTheme t, ExportContext ctx, Sim4DReport r, string section)
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(11, Unit.Millimetre);
            page.PageColor(t.Page);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(t.Text));
            page.Header().Column(h =>
            {
                h.Item().Row(row =>
                {
                    string logo = ctx.EffectiveLogo;
                    if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
                    {
                        row.ConstantItem(64).Height(30).Image(logo).FitArea();
                        row.ConstantItem(10);
                    }
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("RAPPORT DE SIMULATION 4D — " + section.ToUpper(Fr)).Bold().FontSize(12.5f).FontColor(t.Accent).LetterSpacing(0.03f);
                        c.Item().Text(Clean(ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number))).FontSize(9.5f).FontColor(t.Muted);
                    });
                    row.ConstantItem(190).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text(Clean(ctx.Company.DisplayShort)).Bold().FontColor(t.Accent2);
                        c.Item().AlignRight().Text("Établi le " + r.GeneratedAt.ToString("dd/MM/yyyy à HH:mm", Fr)).FontSize(7.5f).FontColor(t.Muted);
                    });
                });
                h.Item().PaddingTop(4).LineHorizontal(1).LineColor(t.Accent);
            });
            page.Footer().Row(row =>
            {
                row.RelativeItem().Text("Simulation 4D DanCI — planning " + r.Plan.StartDate.ToString("dd/MM/yyyy", Fr) + " → " + r.Plan.FinishDate.ToString("dd/MM/yyyy", Fr) +
                                        " ; durées estimées par les rendements (à valider par l'entreprise).").FontSize(7).FontColor(t.Muted);
                row.ConstantItem(80).AlignRight().Text(x =>
                {
                    x.DefaultTextStyle(s => s.FontSize(7).FontColor(t.Muted));
                    x.Span("page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }

        private static IContainer Panel(IContainer e, PdfTheme t) =>
            e.Background(t.Panel).Border(0.8f).BorderColor(t.PanelBorder).Padding(7);

        private static void Card(RowDescriptor row, PdfTheme t, string label, string value, string sub) =>
            row.RelativeItem().Background(t.Panel).BorderLeft(3).BorderColor(t.Accent).Padding(6).Column(c =>
            {
                c.Item().Text(label).FontSize(6.5f).FontColor(t.Muted).LetterSpacing(0.06f);
                c.Item().Text(Clean(value)).Bold().FontSize(12);
                c.Item().Text(Clean(sub)).FontSize(7).FontColor(t.Muted);
            });

        private static void Line(ColumnDescriptor c, PdfTheme t, string label, string value) =>
            c.Item().PaddingTop(3).Row(r =>
            {
                r.ConstantItem(120).Text(Clean(label)).FontColor(t.Muted).FontSize(7.5f);
                r.RelativeItem().Text(Clean(value)).FontSize(7.5f);
            });

        /// <summary>Points d'attention : ceux de la simulation, puis les conflits de dates imposees et les taches sans element.</summary>
        public static IEnumerable<string> Attention(Sim4DReport r)
        {
            foreach (string w in r.Warnings ?? new List<string>()) yield return w;
            foreach (PlanTask x in r.Plan.Tasks.Where(x => !string.IsNullOrWhiteSpace(x.Conflict)))
                yield return "Tâche « " + x.Name + " » : " + x.Conflict;
            int empty = r.Plan.Tasks.Count(x => x.Elements.Count == 0 && x.Lot != "01");
            if (empty > 0) yield return empty + " tâche(s) sans élément Revit (rien à construire en 3D, seulement au planning).";
        }

        // ---------------------------------------------------------------- graphiques (SVG)
        private const double W = 520, H = 170, PadL = 34, PadB = 20, PadT = 6, PadR = 8;

        internal static string SCurve(WorkPlan plan, PdfTheme t)
        {
            var weeks = plan.WeeklyAmounts();
            var sb = Axes(t, weeks.Count, "%");
            if (weeks.Count > 0)
            {
                double pw = W - PadL - PadR, ph = H - PadT - PadB;
                var pts = new StringBuilder();
                pts.AppendFormat(Inv, "{0:0.#},{1:0.#} ", PadL, PadT + ph);
                for (int i = 0; i < weeks.Count; i++)
                {
                    double x = PadL + pw * (i + 1) / weeks.Count;
                    double y = PadT + ph * (1 - (double)weeks[i].CumulativeShare);
                    pts.AppendFormat(Inv, "{0:0.#},{1:0.#} ", x, y);
                }
                sb.AppendFormat(Inv, "<polyline points='{0}' fill='none' stroke='{1}' stroke-width='2.2'/>", pts.ToString().Trim(), t.Accent);
            }
            return sb.Append("</svg>").ToString();
        }

        internal static string Weekly(WorkPlan plan, PdfTheme t)
        {
            var weeks = plan.WeeklyAmounts();
            var sb = Axes(t, weeks.Count, null);
            if (weeks.Count > 0)
            {
                double pw = W - PadL - PadR, ph = H - PadT - PadB;
                double max = (double)Math.Max(1m, weeks.Max(w => w.Amount));
                double bw = pw / weeks.Count;
                for (int i = 0; i < weeks.Count; i++)
                {
                    double h = ph * (double)weeks[i].Amount / max;
                    sb.AppendFormat(Inv, "<rect x='{0:0.##}' y='{1:0.##}' width='{2:0.##}' height='{3:0.##}' fill='{4}'/>",
                        PadL + i * bw + bw * 0.12, PadT + ph - h, Math.Max(0.6, bw * 0.76), h, t.Accent2);
                }
                sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='8' fill='{2}' text-anchor='end'>{3}</text>", PadL - 3, PadT + 7, t.Muted,
                    Esc(((decimal)max).ToString("N0", Fr)));
            }
            return sb.Append("</svg>").ToString();
        }

        private static StringBuilder Axes(PdfTheme t, int weeks, string unit)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'>", W, H);
            double ph = H - PadT - PadB;
            for (int k = 0; k <= 4; k++)
            {
                double y = PadT + ph * k / 4;
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1:0.#}' x2='{2}' y2='{1:0.#}' stroke='{3}' stroke-width='0.5'/>", PadL, y, W - PadR, t.PanelBorder);
                if (unit != null)
                    sb.AppendFormat(Inv, "<text x='{0}' y='{1:0.#}' font-size='8' fill='{2}' text-anchor='end'>{3} %</text>", PadL - 3, y + 3, t.Muted, 100 - k * 25);
            }
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='8' fill='{2}'>semaine 1</text>", PadL, H - 5, t.Muted);
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='8' fill='{2}' text-anchor='end'>semaine {3}</text>", W - PadR, H - 5, t.Muted, Math.Max(1, weeks));
            return sb;
        }

        private static string Esc(string s) => (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace(" ", " ");
    }
}
