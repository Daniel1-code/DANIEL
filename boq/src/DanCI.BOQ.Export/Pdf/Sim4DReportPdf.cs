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
    /// 0. page de garde (vue 3D finale en grand, chiffres cles) ; 1. synthese (courbe en S avec jalons,
    /// histogramme, repartition par lot, reglages, deroulement, points d'attention) ; 2. captures de la
    /// vue 3D avec frise chronologique ; 3. diagramme de Gantt ; 4. taches (mini-barres de planning) et
    /// chronologie des jalons. Rien n'est invente : tout vient du planning simule et de ce que Revit a affiche.
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
            var shots = r.Snapshots.Where(s => s?.Jpeg != null && s.Jpeg.Length > 0).ToList();

            return Document.Create(doc =>
            {
                CoverPage(doc, r, ctx, t, cur, shots);
                SynthesisPage(doc, r, ctx, t, cur);
                SnapshotPages(doc, r, ctx, t, shots);
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
                TasksPage(doc, r, ctx, t, cur);
            });
        }

        // ================================================================= 0. page de garde
        private static void CoverPage(IDocumentContainer doc, Sim4DReport r, ExportContext ctx, PdfTheme t, string cur, List<Sim4DSnapshot> shots)
        {
            WorkPlan plan = r.Plan;
            // Image heroique : la vue de face finale (derniere capture), sinon la vue 3D de la conception.
            byte[] hero = shots.LastOrDefault()?.Jpeg;
            string heroPath = hero == null ? ctx.ExistingDesignImages.FirstOrDefault(d => d.Is3D)?.Path : null;
            string heroCaption = hero != null ? shots.Last().Caption : heroPath != null ? "Vue 3D du modèle" : null;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(0);
                page.PageColor(t.Page);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(t.Text));
                page.Background().Svg(CoverBackground(t));
                page.Content().PaddingVertical(30).PaddingHorizontal(32).Row(row =>
                {
                    row.Spacing(26);
                    // --- visuel
                    row.RelativeItem(1.45f).Column(c =>
                    {
                        c.Item().Height(36).AlignBottom().Row(h =>
                        {
                            string logo = ctx.EffectiveLogo;
                            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
                            {
                                h.ConstantItem(80).Height(34).Image(logo).FitArea();
                                h.ConstantItem(10);
                            }
                            h.RelativeItem().AlignBottom().Text(Clean(ctx.Company.DisplayShort)).Bold().FontSize(13).FontColor(t.Accent);
                        });
                        c.Item().PaddingTop(14).Height(410).Border(1.4f).BorderColor(t.Accent).Background(t.Dark ? "#000000" : "#FFFFFF").Padding(3).Element(e =>
                        {
                            if (hero != null) e.AlignCenter().AlignMiddle().Image(hero).FitArea();
                            else if (heroPath != null) e.AlignCenter().AlignMiddle().Image(heroPath).FitArea();
                            else e.AlignCenter().AlignMiddle().Width(260).Svg(BuildingIcon(t)).FitArea();
                        });
                        if (heroCaption != null)
                            c.Item().PaddingTop(5).Text(Clean(heroCaption)).FontSize(8).Italic().FontColor(t.Muted);
                    });
                    // --- titre et chiffres
                    row.RelativeItem().PaddingTop(38).Column(c =>
                    {
                        c.Item().Text("RAPPORT DE").FontSize(11).LetterSpacing(0.25f).FontColor(t.Muted);
                        c.Item().Text("SIMULATION 4D").Bold().FontSize(30).FontColor(t.Accent).LetterSpacing(0.04f);
                        c.Item().PaddingTop(4).Width(70).Height(3).Background(t.Accent2);
                        c.Item().PaddingTop(14).Text(Clean(ctx.ProjectTitle).ToUpper(Fr)).Bold().FontSize(17);
                        string place = string.Join(" · ", new[] { ctx.Project?.Client, ctx.Project?.Location, string.IsNullOrWhiteSpace(ctx.Project?.Number) ? null : "n° " + ctx.Project.Number }
                            .Where(x => !string.IsNullOrWhiteSpace(x)));
                        if (place.Length > 0) c.Item().PaddingTop(3).Text(Clean(place)).FontSize(9.5f).FontColor(t.Muted);

                        c.Item().PaddingTop(16).Background(t.Panel).Border(0.8f).BorderColor(t.PanelBorder).Padding(9).Row(p =>
                        {
                            p.RelativeItem().Column(x =>
                            {
                                x.Item().Text("DÉBUT").FontSize(6.5f).LetterSpacing(0.1f).FontColor(t.Muted);
                                x.Item().Text(plan.StartDate.ToString("dd MMM yyyy", Fr)).Bold().FontSize(12);
                            });
                            p.ConstantItem(40).AlignCenter().AlignMiddle().Svg(Arrow(t)).FitArea();
                            p.RelativeItem().AlignRight().Column(x =>
                            {
                                x.Item().AlignRight().Text("RÉCEPTION").FontSize(6.5f).LetterSpacing(0.1f).FontColor(t.Muted);
                                x.Item().AlignRight().Text(plan.FinishDate.ToString("dd MMM yyyy", Fr)).Bold().FontSize(12).FontColor(t.Accent2);
                            });
                        });

                        c.Item().PaddingTop(10).Row(p =>
                        {
                            p.Spacing(8);
                            BigTile(p, t, plan.TotalDays.ToString(Fr), "jours ouvrés", t.Accent);
                            BigTile(p, t, plan.Tasks.Count.ToString(Fr), "tâches", t.Accent);
                        });
                        c.Item().PaddingTop(8).Row(p =>
                        {
                            p.Spacing(8);
                            BigTile(p, t, r.SimulatedElements.ToString("N0", Fr), "éléments construits", t.Accent);
                            BigTile(p, t, Short(plan.TotalAmount) + " " + cur, "montant planifié", t.Accent2);
                        });

                        c.Item().PaddingTop(14).Row(p =>
                        {
                            p.AutoItem().Background(r.Completed ? t.Ok : t.Warn).PaddingVertical(4).PaddingHorizontal(10)
                             .Text(r.Completed ? "SIMULATION COMPLÈTE" : "RAPPORT PARTIEL").Bold().FontSize(8).LetterSpacing(0.08f).FontColor(t.Dark ? "#06121F" : "#FFFFFF");
                            p.RelativeItem();
                        });
                        c.Item().PaddingTop(6).Text(r.Snapshots.Count + " capture(s) de la vue 3D · " + plan.Milestones.Count + " jalon(s) · " +
                                                    plan.Tasks.Count(x => x.IsCritical) + " tâche(s) critique(s)").FontSize(8).FontColor(t.Muted);
                        c.Item().ExtendVertical().AlignBottom().Text("Établi le " + r.GeneratedAt.ToString("dd MMMM yyyy à HH:mm", Fr) + " — " + Clean(ctx.Company.DisplayShort))
                         .FontSize(7.5f).FontColor(t.Muted);
                    });
                });
            });
        }

        private static void BigTile(RowDescriptor row, PdfTheme t, string value, string label, string color) =>
            row.RelativeItem().Background(t.Panel).BorderLeft(3).BorderColor(color).PaddingVertical(7).PaddingHorizontal(9).Column(c =>
            {
                c.Item().Text(Clean(value)).Bold().FontSize(17).FontColor(color);
                c.Item().Text(label).FontSize(7.5f).FontColor(t.Muted);
            });

        // ================================================================= 1. synthese
        private static void SynthesisPage(IDocumentContainer doc, Sim4DReport r, ExportContext ctx, PdfTheme t, string cur)
        {
            WorkPlan plan = r.Plan;
            doc.Page(page =>
            {
                Frame(page, t, ctx, r, "Synthèse", 1);
                page.Content().PaddingTop(7).Column(col =>
                {
                    col.Spacing(7);
                    col.Item().Row(row =>
                    {
                        row.Spacing(6);
                        Card(row, t, "DÉBUT DES TRAVAUX", plan.StartDate.ToString("dd/MM/yyyy", Fr), plan.StartDate.ToString("dddd", Fr), t.Accent);
                        Card(row, t, "RÉCEPTION", plan.FinishDate.ToString("dd/MM/yyyy", Fr), plan.FinishDate.ToString("dddd", Fr), t.Accent2);
                        Card(row, t, "DURÉE", plan.TotalDays + " j ouvrés", plan.CalendarDays + " j calendaires · " + plan.WorkingDaysPerWeek + " j/sem.", t.Accent);
                        Card(row, t, "TÂCHES", plan.Tasks.Count.ToString(Fr), plan.Tasks.Count(x => x.IsCritical) + " critique(s) · " + plan.Milestones.Count + " jalon(s)", t.Critical);
                        Card(row, t, "ÉLÉMENTS SIMULÉS", r.SimulatedElements.ToString("N0", Fr), plan.Tasks.Select(x => x.Lot).Distinct().Count() + " lot(s)", t.Ok);
                        Card(row, t, "MONTANT PLANIFIÉ", plan.TotalAmount.ToString("N0", Fr) + " " + cur, "réparti sur la durée des tâches", t.Accent2);
                    });
                    col.Item().Height(222).Row(row =>
                    {
                        row.Spacing(7);
                        row.RelativeItem(1.15f).Element(e => Panel(e, t).Column(c =>
                        {
                            Title(c, t, "COURBE EN S", "avancement cumulé du montant · jalons en losanges (détail page Tâches)");
                            c.Item().PaddingTop(3).Svg(SCurve(plan, t)).FitArea();
                        }));
                        row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                        {
                            Title(c, t, "HISTOGRAMME HEBDOMADAIRE", "montant par semaine · moyenne en pointillés");
                            c.Item().PaddingTop(3).Svg(Weekly(plan, t)).FitArea();
                        }));
                    });
                    col.Item().Row(row =>
                    {
                        row.Spacing(7);
                        row.RelativeItem(1.15f).Element(e => Panel(e, t).Column(c =>
                        {
                            Title(c, t, "RÉPARTITION PAR LOT", "part du montant planifié");
                            c.Item().PaddingTop(4).Svg(LotBars(plan, t, cur)).FitWidth();
                        }));
                        row.RelativeItem().Element(e => Panel(e, t).Column(c =>
                        {
                            Title(c, t, "SIMULATION", "réglages et déroulement");
                            Line(c, t, "Lecture", r.Completed ? "terminée — vue de face" : "interrompue (rapport partiel)", r.Completed ? t.Ok : t.Warn);
                            Line(c, t, "Images dessinées", r.Frames.ToString("N0", Fr));
                            Line(c, t, "Cadence moyenne", r.AverageFrameMs > 0 ? (1000.0 / r.AverageFrameMs).ToString("0.0", Fr) + " img/s · " + r.AverageFrameMs.ToString("0", Fr) + " ms" : "—");
                            Line(c, t, "Durée de lecture", r.PlayTime.TotalSeconds > 0 ? ((int)r.PlayTime.TotalMinutes) + " min " + r.PlayTime.Seconds.ToString("00", Inv) + " s" : "—");
                            Line(c, t, "Vidéo", string.IsNullOrEmpty(r.VideoPath) ? "non enregistrée" : Path.GetFileName(r.VideoPath));
                            foreach (var kv in r.Settings) Line(c, t, kv.Key, kv.Value);
                        }));
                        row.RelativeItem(0.85f).Element(e => Panel(e, t).Column(c =>
                        {
                            var w = Attention(r).ToList();
                            Title(c, t, "POINTS D'ATTENTION", w.Count == 0 ? "aucun" : w.Count + " point(s)", w.Count == 0 ? t.Ok : t.Accent2);
                            if (w.Count == 0) c.Item().PaddingTop(4).Text("Tous les éléments du planning ont été construits et affichés.").FontSize(8).FontColor(t.Ok);
                            foreach (string s in w.Take(8))
                            {
                                c.Item().PaddingTop(4).Row(x =>
                                {
                                    x.ConstantItem(4).Height(10).Background(t.Accent2);
                                    x.RelativeItem().PaddingLeft(5).Text(Clean(s)).FontSize(7.5f);
                                });
                            }
                            if (w.Count > 8) c.Item().PaddingTop(3).Text("… et " + (w.Count - 8) + " autre(s).").FontSize(7.5f).FontColor(t.Muted);
                        }));
                    });
                });
            });
        }

        // ================================================================= 2. captures
        private static void SnapshotPages(IDocumentContainer doc, Sim4DReport r, ExportContext ctx, PdfTheme t, List<Sim4DSnapshot> shots)
        {
            for (int i = 0; i < shots.Count; i += 2)
            {
                var pair = shots.Skip(i).Take(2).ToList();
                int first = i;
                doc.Page(page =>
                {
                    Frame(page, t, ctx, r, "Construction pas à pas", 2);
                    page.Content().PaddingTop(6).Column(col =>
                    {
                        col.Item().Height(34).Svg(Timeline(r, shots, first, pair.Count, t)).FitArea();
                        col.Item().PaddingTop(6).Row(row =>
                        {
                            row.Spacing(10);
                            for (int k = 0; k < pair.Count; k++)
                            {
                                Sim4DSnapshot s = pair[k];
                                int n = first + k + 1;
                                row.RelativeItem().Background(t.Panel).Border(0.8f).BorderColor(t.PanelBorder).Column(c =>
                                {
                                    c.Item().Padding(6).Row(h =>
                                    {
                                        h.ConstantItem(24).Height(24).Background(t.Accent).AlignCenter().AlignMiddle().Text(n.ToString(Inv)).Bold().FontSize(11).FontColor(t.Dark ? "#06121F" : "#FFFFFF");
                                        h.RelativeItem().PaddingLeft(8).AlignMiddle().Column(x =>
                                        {
                                            x.Item().Text(Clean(Head(s.Caption))).Bold().FontSize(10);
                                            string tail = Tail(s.Caption);
                                            if (tail != null) x.Item().Text(Clean(tail)).FontSize(7.5f).FontColor(t.Muted);
                                        });
                                        h.AutoItem().AlignMiddle().Background(t.Accent2).PaddingVertical(3).PaddingHorizontal(7)
                                         .Text((s.Progress * 100).ToString("0", Fr) + " %").Bold().FontSize(9).FontColor(t.Dark ? "#06121F" : "#FFFFFF");
                                    });
                                    c.Item().Height(318).Background(t.Dark ? "#000000" : "#FFFFFF").AlignCenter().AlignMiddle().Image(s.Jpeg).FitArea();
                                    c.Item().Height(4).Row(b =>
                                    {
                                        float done = (float)Math.Max(0.001, Math.Min(1, s.Progress));
                                        b.RelativeItem(done).Background(t.Accent);
                                        if (done < 1) b.RelativeItem(1 - done).Background(t.PanelBorder);
                                    });
                                });
                            }
                            if (pair.Count == 1) row.RelativeItem();
                        });
                    });
                });
            }
        }

        private static string Head(string caption)
        {
            int k = (caption ?? string.Empty).IndexOf(" — ", StringComparison.Ordinal);
            return k > 0 ? caption.Substring(0, k) : caption ?? string.Empty;
        }

        private static string Tail(string caption)
        {
            int k = (caption ?? string.Empty).IndexOf(" — ", StringComparison.Ordinal);
            return k > 0 ? caption.Substring(k + 3) : null;
        }

        // ================================================================= 4. taches et jalons
        private static void TasksPage(IDocumentContainer doc, Sim4DReport r, ExportContext ctx, PdfTheme t, string cur)
        {
            WorkPlan plan = r.Plan;
            doc.Page(page =>
            {
                Frame(page, t, ctx, r, "Tâches simulées et jalons", 4);
                page.Content().PaddingTop(7).Column(col =>
                {
                    if (plan.Milestones.Count > 0)
                    {
                        col.Item().Element(e => Panel(e, t).Column(c =>
                        {
                            Title(c, t, "CHRONOLOGIE DES JALONS", plan.StartDate.ToString("dd/MM/yyyy", Fr) + " → " + plan.FinishDate.ToString("dd/MM/yyyy", Fr));
                            c.Item().PaddingTop(2).Svg(Milestones(plan, t)).FitWidth();
                        }));
                        col.Item().Height(8);
                    }
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(6); c.ConstantColumn(30); c.RelativeColumn(2.6f); c.RelativeColumn(1f); c.ConstantColumn(50); c.ConstantColumn(50);
                            c.ConstantColumn(34); c.RelativeColumn(2.2f); c.ConstantColumn(34); c.ConstantColumn(70); c.ConstantColumn(50);
                        });
                        string head = t.Dark ? "#12304F" : t.Accent;
                        table.Header(h =>
                        {
                            foreach (string s in new[] { "", "Lot", "Tâche", "Niveau", "Début", "Fin", "Durée", "Planning", "Élém.", "Montant (" + cur + ")", "Marge" })
                                h.Cell().Background(head).PaddingVertical(4).PaddingHorizontal(3).Text(s).Bold().FontSize(7.5f).FontColor("#FFFFFF");
                        });
                        int z = 0;
                        foreach (PlanTask x in plan.Tasks.OrderBy(x => x.StartDay).ThenBy(x => x.Lot, StringComparer.Ordinal))
                        {
                            bool zebra = z++ % 2 == 1;
                            string bg = zebra ? (t.Dark ? "#0F1A2E" : "#F3F6FB") : t.Page;
                            string color = Hex(x.Color) ?? t.Accent;
                            IContainer C() => table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(t.PanelBorder).PaddingVertical(2.5f).PaddingHorizontal(3);
                            table.Cell().Background(color);
                            C().Text(x.Lot).Bold().FontSize(7.5f).FontColor(color);
                            C().Text(Clean(x.Name) + (x.IsStartImposed || x.IsFinishImposed ? "  · date imposée" : string.Empty)).FontSize(7.5f);
                            C().Text(Clean(x.Level ?? "—")).FontSize(7.5f).FontColor(t.Muted);
                            C().Text(x.Start.ToString("dd/MM/yy", Fr)).FontSize(7.5f);
                            C().Text(x.Finish.ToString("dd/MM/yy", Fr)).FontSize(7.5f);
                            C().AlignRight().Text(x.DurationDays + " j").FontSize(7.5f);
                            C().AlignMiddle().Height(8).Svg(MiniBar(plan, x, color, t)).FitArea();
                            C().AlignRight().Text(x.Elements.Count.ToString(Fr)).FontSize(7.5f);
                            C().AlignRight().Text(x.Amount.ToString("N0", Fr)).FontSize(7.5f);
                            if (x.IsCritical)
                                table.Cell().Background(bg).BorderBottom(0.25f).BorderColor(t.PanelBorder).PaddingVertical(2).PaddingHorizontal(3).AlignRight()
                                     .Background(t.Critical).PaddingHorizontal(4).Text("critique").Bold().FontSize(6.5f).FontColor("#FFFFFF");
                            else
                                C().AlignRight().Text(x.FloatDays + " j").FontSize(7.5f).FontColor(t.Muted);
                        }
                    });
                });
            });
        }

        // ================================================================= cadre commun
        private static void Frame(PageDescriptor page, PdfTheme t, ExportContext ctx, Sim4DReport r, string section, int number)
        {
            page.Size(PageSizes.A4.Landscape());
            page.Margin(11, Unit.Millimetre);
            page.PageColor(t.Page);
            page.DefaultTextStyle(x => x.FontSize(8.5f).FontColor(t.Text));
            page.Header().Column(h =>
            {
                h.Item().Row(row =>
                {
                    row.ConstantItem(30).Height(30).Background(t.Accent).AlignCenter().AlignMiddle()
                       .Text(number.ToString("00", Inv)).Bold().FontSize(12).FontColor(t.Dark ? "#06121F" : "#FFFFFF");
                    row.ConstantItem(10);
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text(section.ToUpper(Fr)).Bold().FontSize(13).FontColor(t.Accent).LetterSpacing(0.03f);
                        c.Item().Text("Rapport de simulation 4D · " + Clean(ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number)))
                         .FontSize(8.5f).FontColor(t.Muted);
                    });
                    string logo = ctx.EffectiveLogo;
                    if (!string.IsNullOrEmpty(logo) && File.Exists(logo)) row.ConstantItem(64).Height(28).AlignRight().Image(logo).FitArea();
                    else row.ConstantItem(160).AlignRight().AlignMiddle().Text(Clean(ctx.Company.DisplayShort)).Bold().FontColor(t.Accent2);
                });
                h.Item().PaddingTop(5).Row(l =>
                {
                    l.ConstantItem(60).Height(2).Background(t.Accent2);
                    l.RelativeItem().Height(2).Background(t.Accent);
                });
            });
            page.Footer().PaddingTop(4).Row(row =>
            {
                row.RelativeItem().Text("DanCI · simulation 4D du " + r.Plan.StartDate.ToString("dd/MM/yyyy", Fr) + " au " + r.Plan.FinishDate.ToString("dd/MM/yyyy", Fr) +
                                        " · durées estimées par les rendements, à valider par l'entreprise · établi le " + r.GeneratedAt.ToString("dd/MM/yyyy", Fr)).FontSize(6.8f).FontColor(t.Muted);
                row.ConstantItem(70).AlignRight().Text(x =>
                {
                    x.DefaultTextStyle(s => s.FontSize(7).FontColor(t.Muted));
                    x.CurrentPageNumber().Bold().FontColor(t.Accent);
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        }

        private static IContainer Panel(IContainer e, PdfTheme t) =>
            e.Background(t.Panel).Border(0.8f).BorderColor(t.PanelBorder).Padding(8);

        private static void Title(ColumnDescriptor c, PdfTheme t, string title, string sub, string color = null) =>
            c.Item().Row(r =>
            {
                r.ConstantItem(3).Height(11).Background(color ?? t.Accent);
                r.AutoItem().PaddingLeft(5).Text(title).Bold().FontSize(8.5f).FontColor(color ?? t.Accent).LetterSpacing(0.04f);
                r.RelativeItem().PaddingLeft(6).AlignBottom().Text(Clean(sub)).FontSize(7).FontColor(t.Muted);
            });

        private static void Card(RowDescriptor row, PdfTheme t, string label, string value, string sub, string color) =>
            row.RelativeItem().Background(t.Panel).Border(0.6f).BorderColor(t.PanelBorder).Column(c =>
            {
                c.Item().Height(2.5f).Background(color);
                c.Item().PaddingHorizontal(7).PaddingVertical(5).Column(x =>
                {
                    x.Item().Text(label).FontSize(6.3f).FontColor(t.Muted).LetterSpacing(0.08f);
                    x.Item().Text(Clean(value)).Bold().FontSize(12.5f).FontColor(color);
                    x.Item().Text(Clean(sub)).FontSize(6.8f).FontColor(t.Muted);
                });
            });

        private static void Line(ColumnDescriptor c, PdfTheme t, string label, string value, string valueColor = null) =>
            c.Item().PaddingTop(3).Row(r =>
            {
                r.ConstantItem(98).Text(Clean(label)).FontColor(t.Muted).FontSize(7.3f);
                r.RelativeItem().Text(Clean(value)).FontSize(7.3f).FontColor(valueColor ?? t.Text);
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

        private static string Short(decimal v) =>
            v >= 1_000_000m ? (v / 1_000_000m).ToString("0.##", Fr) + " M" : v >= 10_000m ? (v / 1000m).ToString("0", Fr) + " k" : v.ToString("N0", Fr);

        private static string Hex(string c) =>
            !string.IsNullOrWhiteSpace(c) && c.Length == 7 && c[0] == '#' && c.Skip(1).All(Uri.IsHexDigit) ? c : null;

        private static string Esc(string s) => (s ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("'", "&#39;")
            .Replace(" ", " ").Replace(" ", " ");

        // ================================================================= graphiques (SVG)
        private const double W = 560, H = 175, PadL = 36, PadB = 18, PadT = 8, PadR = 8;

        internal static string SCurve(WorkPlan plan, PdfTheme t)
        {
            var weeks = plan.WeeklyAmounts();
            var sb = Axes(t, weeks.Count, true);
            double pw = W - PadL - PadR, ph = H - PadT - PadB;
            sb.AppendFormat(Inv, "<defs><linearGradient id='sa' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{0}' stop-opacity='0.45'/><stop offset='1' stop-color='{0}' stop-opacity='0.02'/></linearGradient></defs>", t.Accent);
            // Jalons : lignes verticales en pointilles.
            int k = 0;
            foreach (PlanMilestone m in plan.Milestones.OrderBy(m => m.Day))
            {
                double x = PadL + pw * Math.Max(0, Math.Min(1, m.Day / (double)Math.Max(1, plan.TotalDays)));
                sb.AppendFormat(Inv, "<line x1='{0:0.#}' y1='{1}' x2='{0:0.#}' y2='{2:0.#}' stroke='{3}' stroke-width='0.8' stroke-dasharray='3 2'/>", x, PadT, PadT + ph, t.Accent2);
                sb.AppendFormat(Inv, "<polygon points='{0:0.#},{1:0.#} {2:0.#},{3:0.#} {0:0.#},{4:0.#} {5:0.#},{3:0.#}' fill='{6}'/>", x, PadT - 1, x + 3.5, PadT + 2.5, PadT + 6, x - 3.5, t.Accent2);
                k++;
            }
            if (weeks.Count > 0)
            {
                var pts = new StringBuilder();
                pts.AppendFormat(Inv, "{0:0.#},{1:0.#} ", PadL, PadT + ph);
                for (int i = 0; i < weeks.Count; i++)
                    pts.AppendFormat(Inv, "{0:0.#},{1:0.#} ", PadL + pw * (i + 1) / weeks.Count, PadT + ph * (1 - (double)weeks[i].CumulativeShare));
                string line = pts.ToString().Trim();
                sb.AppendFormat(Inv, "<polygon points='{0} {1:0.#},{2:0.#}' fill='url(#sa)'/>", line, PadL + pw, PadT + ph);
                sb.AppendFormat(Inv, "<polyline points='{0}' fill='none' stroke='{1}' stroke-width='2.4' stroke-linejoin='round'/>", line, t.Accent);
                sb.AppendFormat(Inv, "<circle cx='{0:0.#}' cy='{1:0.#}' r='3.2' fill='{2}'/>", PadL + pw, PadT + ph * (1 - (double)weeks.Last().CumulativeShare), t.Accent);
            }
            Dates(sb, plan, t);
            return sb.Append("</svg>").ToString();
        }

        internal static string Weekly(WorkPlan plan, PdfTheme t)
        {
            var weeks = plan.WeeklyAmounts();
            var sb = Axes(t, weeks.Count, false);
            if (weeks.Count > 0)
            {
                double pw = W - PadL - PadR, ph = H - PadT - PadB;
                double max = (double)Math.Max(1m, weeks.Max(w => w.Amount));
                double avg = (double)weeks.Average(w => w.Amount);
                double bw = pw / weeks.Count;
                sb.AppendFormat(Inv, "<defs><linearGradient id='wb' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='{0}'/><stop offset='1' stop-color='{0}' stop-opacity='0.45'/></linearGradient></defs>", t.Accent2);
                for (int i = 0; i < weeks.Count; i++)
                {
                    double h = ph * (double)weeks[i].Amount / max;
                    sb.AppendFormat(Inv, "<rect x='{0:0.##}' y='{1:0.##}' width='{2:0.##}' height='{3:0.##}' rx='1.2' fill='url(#wb)'/>",
                        PadL + i * bw + bw * 0.14, PadT + ph - h, Math.Max(0.8, bw * 0.72), h);
                }
                double ya = PadT + ph * (1 - avg / max);
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1:0.#}' x2='{2}' y2='{1:0.#}' stroke='{3}' stroke-width='0.9' stroke-dasharray='4 3'/>", PadL, ya, W - PadR, t.Accent);
                sb.AppendFormat(Inv, "<text x='{0}' y='{1:0.#}' font-size='7' fill='{2}' text-anchor='end'>moy. {3}</text>", W - PadR, ya - 3, t.Accent, Esc(Short((decimal)avg)));
                sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7.5' fill='{2}' text-anchor='end'>{3}</text>", PadL - 4, PadT + 7, t.Muted, Esc(Short((decimal)max)));
                sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7.5' fill='{2}' text-anchor='end'>0</text>", PadL - 4, PadT + ph + 3, t.Muted);
            }
            Dates(sb, plan, t);
            return sb.Append("</svg>").ToString();
        }

        private static StringBuilder Axes(PdfTheme t, int weeks, bool percent)
        {
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'>", W, H);
            double ph = H - PadT - PadB;
            for (int k = 0; k <= 4; k++)
            {
                double y = PadT + ph * k / 4;
                sb.AppendFormat(Inv, "<line x1='{0}' y1='{1:0.#}' x2='{2}' y2='{1:0.#}' stroke='{3}' stroke-width='{4}'/>", PadL, y, W - PadR, t.PanelBorder, k == 4 ? 1 : 0.5);
                if (percent)
                    sb.AppendFormat(Inv, "<text x='{0}' y='{1:0.#}' font-size='7.5' fill='{2}' text-anchor='end'>{3} %</text>", PadL - 4, y + 3, t.Muted, 100 - k * 25);
            }
            return sb;
        }

        private static void Dates(StringBuilder sb, WorkPlan plan, PdfTheme t)
        {
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7' fill='{2}'>{3}</text>", PadL, H - 4, t.Muted, plan.StartDate.ToString("dd/MM/yy", Fr));
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7' fill='{2}' text-anchor='middle'>{3} semaines</text>", PadL + (W - PadL - PadR) / 2, H - 4, t.Muted,
                Math.Max(1, plan.WeeklyAmounts().Count));
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7' fill='{2}' text-anchor='end'>{3}</text>", W - PadR, H - 4, t.Muted, plan.FinishDate.ToString("dd/MM/yy", Fr));
        }

        /// <summary>Barres horizontales : montant de chaque lot, a sa couleur (10 plus gros lots).</summary>
        internal static string LotBars(WorkPlan plan, PdfTheme t, string cur)
        {
            var lots = plan.Tasks.GroupBy(x => x.Lot)
                .Select(g => (Lot: g.Key, Name: g.First().LotDesignation, Color: Hex(g.First().Color) ?? t.Accent, Amount: g.Sum(x => x.Amount), Days: g.Sum(x => x.DurationDays)))
                .Where(x => x.Amount > 0).OrderByDescending(x => x.Amount).Take(10).ToList();
            decimal total = Math.Max(1m, plan.TotalAmount);
            decimal max = lots.Count > 0 ? Math.Max(1m, lots.Max(x => x.Amount)) : 1m;
            double rowH = 19, width = 430, labelW = 150, valueW = 96;
            double height = Math.Max(rowH, lots.Count * rowH);
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'>", width, height);
            for (int i = 0; i < lots.Count; i++)
            {
                var l = lots[i];
                double y = i * rowH;
                double bw = (width - labelW - valueW - 8) * (double)(l.Amount / max);
                sb.AppendFormat(Inv, "<rect x='0' y='{0:0.#}' width='3' height='12' fill='{1}'/>", y + 2, l.Color);
                sb.AppendFormat(Inv, "<text x='7' y='{0:0.#}' font-size='9.5' fill='{1}'>{2}</text>", y + 11, t.Text, Esc(Trunc("Lot " + l.Lot + " · " + (l.Name ?? string.Empty), 26)));
                sb.AppendFormat(Inv, "<rect x='{0}' y='{1:0.#}' width='{2:0.#}' height='12' fill='{3}' stroke='none' opacity='0.18'/>", labelW, y + 2, width - labelW - valueW - 8, t.Muted);
                sb.AppendFormat(Inv, "<rect x='{0}' y='{1:0.#}' width='{2:0.#}' height='12' rx='1.5' fill='{3}'/>", labelW, y + 2, Math.Max(1.5, bw), l.Color);
                sb.AppendFormat(Inv, "<text x='{0}' y='{1:0.#}' font-size='9.5' fill='{2}' text-anchor='end'>{3} {4}</text>", width - 36, y + 11, t.Text, Esc(l.Amount.ToString("N0", Fr)), Esc(cur));
                sb.AppendFormat(Inv, "<text x='{0}' y='{1:0.#}' font-size='9.5' font-weight='bold' fill='{2}' text-anchor='end'>{3} %</text>", width, y + 11, l.Color,
                    (l.Amount / total * 100m).ToString("0", Fr));
            }
            return sb.Append("</svg>").ToString();
        }

        /// <summary>Frise : debut, fin, toutes les captures ; celles de la page en surbrillance.</summary>
        private static string Timeline(Sim4DReport r, List<Sim4DSnapshot> shots, int first, int count, PdfTheme t)
        {
            double width = 800, height = 34, x0 = 64, x1 = width - 64, y = 15;
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'>", width, height);
            sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2}' y2='{1}' stroke='{3}' stroke-width='3' stroke-linecap='round'/>", x0, y, x1, t.PanelBorder);
            double reached = shots.Count == 0 ? 0 : shots.Skip(first).Take(count).Max(s => s.Progress);
            sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2:0.#}' y2='{1}' stroke='{3}' stroke-width='3' stroke-linecap='round'/>", x0, y, x0 + (x1 - x0) * Math.Min(1, reached), t.Accent);
            for (int i = 0; i < shots.Count; i++)
            {
                double x = x0 + (x1 - x0) * Math.Max(0, Math.Min(1, shots[i].Progress));
                bool here = i >= first && i < first + count;
                sb.AppendFormat(Inv, "<circle cx='{0:0.#}' cy='{1}' r='{2}' fill='{3}' stroke='{4}' stroke-width='1.2'/>", x, y, here ? 6 : 3.2,
                    here ? t.Accent2 : t.Accent, t.Page);
                if (here)
                    sb.AppendFormat(Inv, "<text x='{0:0.#}' y='{1}' font-size='7.5' font-weight='bold' fill='{2}' text-anchor='middle'>{3}</text>", x, y + 3, t.Dark ? "#06121F" : "#FFFFFF", i + 1);
            }
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='8' fill='{2}' text-anchor='end'>{3}</text>", x0 - 8, y + 3, t.Muted, r.Plan.StartDate.ToString("dd/MM/yy", Fr));
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='8' fill='{2}'>{3}</text>", x1 + 8, y + 3, t.Muted, r.Plan.FinishDate.ToString("dd/MM/yy", Fr));
            sb.AppendFormat(Inv, "<text x='{0}' y='{1}' font-size='7' fill='{2}' text-anchor='middle'>captures {3} à {4} sur {5}</text>", width / 2, height - 1, t.Muted, first + 1, first + count, shots.Count);
            return sb.Append("</svg>").ToString();
        }

        /// <summary>Mini-barre de planning d'une tache (position dans la duree totale).</summary>
        private static string MiniBar(WorkPlan plan, PlanTask x, string color, PdfTheme t)
        {
            double total = Math.Max(1, plan.TotalDays), width = 200, h = 8;
            double a = width * x.StartDay / total, w = Math.Max(2, width * Math.Max(1, x.DurationDays) / total);
            return string.Format(Inv,
                "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'><rect x='0' y='3.5' width='{0}' height='1' fill='{2}'/>" +
                "<rect x='{3:0.#}' y='0.5' width='{4:0.#}' height='7' rx='1.5' fill='{5}'{6}/></svg>",
                width, h, t.PanelBorder, a, Math.Min(w, width - a), color, x.IsCritical ? " stroke='" + t.Critical + "' stroke-width='1'" : string.Empty);
        }

        /// <summary>Chronologie des jalons : frise avec losanges, etiquettes alternees dessus / dessous.</summary>
        private static string Milestones(WorkPlan plan, PdfTheme t)
        {
            double width = 800, height = 78, x0 = 20, x1 = width - 20, y = 39;
            var sb = new StringBuilder();
            sb.AppendFormat(Inv, "<svg xmlns='http://www.w3.org/2000/svg' width='{0}' height='{1}' viewBox='0 0 {0} {1}'>", width, height);
            sb.AppendFormat(Inv, "<line x1='{0}' y1='{1}' x2='{2}' y2='{1}' stroke='{3}' stroke-width='2'/>", x0, y, x1, t.Accent);
            int k = 0;
            foreach (PlanMilestone m in plan.Milestones.OrderBy(m => m.Day))
            {
                double x = x0 + (x1 - x0) * Math.Max(0, Math.Min(1, m.Day / (double)Math.Max(1, plan.TotalDays)));
                bool up = k % 2 == 0;
                string anchor = x < x0 + 60 ? "start" : x > x1 - 60 ? "end" : "middle";
                sb.AppendFormat(Inv, "<line x1='{0:0.#}' y1='{1}' x2='{0:0.#}' y2='{2}' stroke='{3}' stroke-width='0.7'/>", x, y, up ? y - 14 : y + 14, t.Muted);
                sb.AppendFormat(Inv, "<polygon points='{0:0.#},{1} {2:0.#},{3} {0:0.#},{4} {5:0.#},{3}' fill='{6}' stroke='{7}' stroke-width='1'/>",
                    x, y - 6, x + 6, y, y + 6, x - 6, t.Accent2, t.Page);
                double ty = up ? y - 26 : y + 24;
                sb.AppendFormat(Inv, "<text x='{0:0.#}' y='{1}' font-size='7.5' font-weight='bold' fill='{2}' text-anchor='{3}'>{4}</text>", x, ty, t.Accent2, anchor, m.Date.ToString("dd/MM/yy", Fr));
                sb.AppendFormat(Inv, "<text x='{0:0.#}' y='{1}' font-size='7.5' fill='{2}' text-anchor='{3}'>{4}</text>", x, ty + 9, t.Text, anchor, Esc(Trunc(m.Name, 36)));
                k++;
            }
            return sb.Append("</svg>").ToString();
        }

        private static string Trunc(string s, int n) => string.IsNullOrEmpty(s) || s.Length <= n ? s ?? string.Empty : s.Substring(0, n - 1) + "…";

        // ================================================================= decors
        private static string CoverBackground(PdfTheme t)
        {
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='842' height='595' viewBox='0 0 842 595' preserveAspectRatio='none'>");
            if (t.Dark)
            {
                sb.Append("<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#0B1A33'/><stop offset='1' stop-color='#05080F'/></linearGradient></defs>");
                sb.Append("<rect width='842' height='595' fill='url(#g)'/>");
            }
            else
            {
                sb.AppendFormat(Inv, "<rect width='842' height='595' fill='{0}'/>", t.Page);
            }
            string grid = t.Dark ? "#12345A" : "#E3EAF3";
            for (int x = 0; x <= 842; x += 28) sb.AppendFormat(Inv, "<line x1='{0}' y1='0' x2='{0}' y2='595' stroke='{1}' stroke-width='0.4' stroke-opacity='0.6'/>", x, grid);
            for (int y = 0; y <= 595; y += 28) sb.AppendFormat(Inv, "<line x1='0' y1='{0}' x2='842' y2='{0}' stroke='{1}' stroke-width='0.4' stroke-opacity='0.6'/>", y, grid);
            for (int i = 0; i < 5; i++)
                sb.AppendFormat(Inv, "<circle cx='820' cy='40' r='{0}' fill='none' stroke='{1}' stroke-width='{2}' stroke-opacity='{3}'/>", 70 + i * 34, t.Accent, i == 1 ? 1.3 : 0.6, 0.5 - i * 0.08);
            sb.AppendFormat(Inv, "<path d='M 0 560 L 300 560 L 330 535 L 842 535' fill='none' stroke='{0}' stroke-width='1.4' stroke-opacity='0.8'/>", t.Accent2);
            sb.AppendFormat(Inv, "<rect x='0' y='0' width='842' height='5' fill='{0}'/><rect x='0' y='5' width='220' height='2' fill='{1}'/>", t.Accent, t.Accent2);
            return sb.Append("</svg>").ToString();
        }

        private static string Arrow(PdfTheme t) => string.Format(Inv,
            "<svg xmlns='http://www.w3.org/2000/svg' width='40' height='14' viewBox='0 0 40 14'><line x1='2' y1='7' x2='34' y2='7' stroke='{0}' stroke-width='1.6'/>" +
            "<polygon points='30,2 38,7 30,12' fill='{0}'/></svg>", t.Accent);

        /// <summary>Pictogramme d'immeuble (sans capture ni image de conception).</summary>
        private static string BuildingIcon(PdfTheme t)
        {
            var sb = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='260' height='220' viewBox='0 0 260 220'>");
            for (int f = 0; f < 5; f++)
                sb.AppendFormat(Inv, "<polygon points='40,{0} 160,{0} 220,{1} 100,{1}' fill='{2}' stroke='{3}' stroke-width='1'/><rect x='40' y='{0}' width='120' height='14' fill='{4}' stroke='{3}'/>",
                    180 - f * 32, 150 - f * 32, t.Dark ? "#1E3A5F" : "#DCE6F2", t.Accent, t.Dark ? "#0F1A2E" : "#C9D6E3");
            return sb.Append("</svg>").ToString();
        }
    }
}
