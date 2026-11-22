using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static DanCI.BOQ.Export.Pdf.PdfTheme;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>Page de garde (generee ou importee) et page « Tableau de bord » du PDF.</summary>
    internal static class CoverAndDashboardPdf
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static void Cover(IDocumentContainer doc, ExportContext ctx)
        {
            string imported = ctx.Cover?.ImportedPage;
            if (!string.IsNullOrWhiteSpace(imported) && File.Exists(imported))
            {
                // Page de garde realisee a part : affichee telle quelle, pleine page.
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4);
                    page.Margin(0);
                    page.Content().AlignCenter().AlignMiddle().Image(imported).FitArea();
                });
                return;
            }
            PdfTheme t = For(ctx.Cover);
            ProjectInfo p = ctx.Project ?? new ProjectInfo();
            string cur = ctx.Boq.Currency.ToString();

            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(0);
                page.PageColor(t.Page);
                page.DefaultTextStyle(s => s.FontColor(t.Text).FontSize(9));
                if (t.Dark)
                {
                    page.Background().Svg(FuturisticBackground());
                }

                page.Content().PaddingHorizontal(48).PaddingTop(40).Column(col =>
                {
                    col.Spacing(0);
                    col.Item().Row(row =>
                    {
                        string logo = ctx.EffectiveLogo;
                        if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
                        {
                            row.ConstantItem(90).Height(42).Image(logo).FitArea();
                            row.ConstantItem(12);
                        }
                        row.RelativeItem().AlignMiddle().Column(c =>
                        {
                            c.Item().Text(t2 => t2.Span("DanCI").Bold().FontSize(18).FontColor(t.Accent).LetterSpacing(0.12f));
                            c.Item().Text("DAN CONSULT INTELLIGENTE  ·  BUREAU D'ÉTUDES  ·  KINSHASA").FontSize(7).FontColor(t.Muted).LetterSpacing(0.15f);
                        });
                        row.ConstantItem(130).AlignRight().AlignMiddle().Column(c =>
                        {
                            c.Item().AlignRight().Text(Clean(Or(p.QuoteReference, "Réf. —"))).FontSize(8).FontColor(t.Muted);
                            c.Item().AlignRight().Text("INDICE " + Clean(Or(p.Revision, "—"))).Bold().FontSize(10).FontColor(t.Accent2);
                        });
                    });

                    col.Item().PaddingTop(70).PaddingLeft(10).Column(c =>
                    {
                        c.Item().Text("BORDEREAU DES QUANTITÉS").FontSize(11).FontColor(t.Muted).LetterSpacing(0.3f);
                        c.Item().Text("& DEVIS ESTIMATIF").FontSize(11).FontColor(t.Muted).LetterSpacing(0.3f);
                        c.Item().PaddingTop(10).Text(Clean(ctx.ProjectTitle).ToUpper(Fr)).Bold().FontSize(28).FontColor(t.Text).LineHeight(1.05f);
                        string subtitle = Or(ctx.Cover?.Subtitle, p.Purpose);
                        if (!string.IsNullOrWhiteSpace(subtitle))
                        {
                            c.Item().PaddingTop(6).Text(Clean(subtitle)).FontSize(12).FontColor(t.Accent);
                        }
                    });

                    string image = ctx.Cover?.ProjectImage;
                    bool hasImage = !string.IsNullOrWhiteSpace(image) && File.Exists(image);
                    col.Item().PaddingTop(hasImage ? 22 : 60).Height(hasImage ? 220 : 0).Element(e =>
                    {
                        if (hasImage)
                        {
                            e.Border(1.2f).BorderColor(t.Accent).Padding(3).AlignCenter().AlignMiddle().Image(image).FitArea();
                        }
                    });

                    // Cartes d'identite du projet.
                    col.Item().PaddingTop(24).Row(row =>
                    {
                        row.Spacing(10);
                        Card(row.RelativeItem(), t, "MAÎTRE D'OUVRAGE", Or(p.Client, "—"));
                        Card(row.RelativeItem(), t, "LIEU", Or(p.Location, "—"));
                        Card(row.RelativeItem(), t, "DATE", Or(p.IssueDate, ctx.GeneratedAt.ToString("dd/MM/yyyy", Fr)));
                    });
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.Spacing(10);
                        Card(row.RelativeItem(), t, "MAÎTRE D'ŒUVRE", Or(p.Architect, "—"));
                        Card(row.RelativeItem(), t, "BUREAU D'ÉTUDES", Or(p.Engineer, ProductInfo.Author));
                        Card(row.RelativeItem(), t, "N° PROJET", Or(p.Number, "—"));
                    });

                    // Montant : le chiffre qui compte, en grand.
                    col.Item().PaddingTop(18).Border(1).BorderColor(t.Accent).Background(t.Panel).Padding(12).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("MONTANT TOTAL TTC").FontSize(8).FontColor(t.Muted).LetterSpacing(0.2f);
                            c.Item().Text(Money(ctx.Boq.TotalIncludingVat) + " " + cur).Bold().FontSize(22).FontColor(t.Accent);
                            if (ctx.Boq.TotalIncludingVatSecondary.HasValue && ctx.Boq.SecondaryCurrency.HasValue)
                            {
                                c.Item().Text("soit " + Money(ctx.Boq.TotalIncludingVatSecondary.Value) + " " + ctx.Boq.SecondaryCurrency).FontSize(9).FontColor(t.Muted);
                            }
                        });
                        row.ConstantItem(150).AlignRight().AlignMiddle().Column(c =>
                        {
                            c.Item().AlignRight().Text("HT " + Money(ctx.Boq.TotalExcludingVat) + " " + cur).FontSize(9);
                            c.Item().AlignRight().Text("TVA " + ctx.Boq.VatPercent.ToString("0.##", Fr) + " %  " + Money(ctx.Boq.Vat)).FontSize(9).FontColor(t.Muted);
                            if (!ctx.Boq.IsComplete)
                            {
                                c.Item().AlignRight().Text("TOTAL INCOMPLET (minorant)").Bold().FontSize(8).FontColor(t.Critical);
                            }
                        });
                    });
                });

                page.Footer().PaddingHorizontal(48).PaddingBottom(14).Row(row =>
                {
                    row.RelativeItem().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + "  ·  maquette " + ctx.DocumentTitle +
                                                  "  ·  empreinte " + DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint)))
                        .FontSize(7).FontColor(t.Muted);
                    row.ConstantItem(140).AlignRight().Text("Établi par " + Clean(Or(p.Author, ProductInfo.Author))).FontSize(7).FontColor(t.Muted);
                });
            });
        }

        public static void Dashboard(IDocumentContainer doc, ExportContext ctx)
        {
            BoqDashboard d = ctx.Dashboard;
            if (d == null)
            {
                return;
            }
            PdfTheme t = For(ctx.Cover);
            string cur = d.Currency;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(12, Unit.Millimetre);
                page.PageColor(t.Page);
                page.DefaultTextStyle(s => s.FontColor(t.Text).FontSize(8));

                page.Header().PaddingBottom(6).Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("TABLEAU DE BORD DE PILOTAGE").Bold().FontSize(14).FontColor(t.Accent).LetterSpacing(0.1f);
                        c.Item().Text(Clean(ctx.ProjectTitle)).FontSize(9).FontColor(t.Muted);
                    });
                    row.ConstantItem(120).AlignRight().Column(c =>
                    {
                        c.Item().AlignRight().Text("NOTE DE SANTÉ").FontSize(7).FontColor(t.Muted);
                        c.Item().AlignRight().Text(d.HealthScore + " / 100").Bold().FontSize(18).FontColor(LevelColor(t, ScoreLevel(d.HealthScore)));
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(8);
                    col.Item().Row(row =>
                    {
                        row.Spacing(6);
                        Kpi(row.RelativeItem(), t, "TOTAL TRAVAUX HT", Money(d.WorksTotal) + " " + cur, null, t.Dark ? Palette.Blue : null, accent: Palette.Blue);
                        Kpi(row.RelativeItem(), t, "TOTAL TTC", Money(d.TotalIncludingVat) + " " + cur,
                            d.TotalIncludingVatSecondary.HasValue ? "soit " + Money(d.TotalIncludingVatSecondary.Value) + " " + d.SecondaryCurrency : null,
                            t.Dark ? (d.IsComplete ? Palette.Green : t.Warn) : t.Accent, accent: Palette.Green);
                        Kpi(row.RelativeItem(), t, "COÛT AU M²", d.CostPerSquareMeter.HasValue ? Money(d.CostPerSquareMeter.Value) + " " + cur + "/m²" : "—",
                            d.ReferenceArea.HasValue ? "TTC / " + Money(d.ReferenceArea.Value) + " m² " + Clean(d.ReferenceAreaLabel) : "surface à renseigner",
                            t.Dark ? Palette.Violet : null, accent: Palette.Violet);
                    });
                    col.Item().Row(row =>
                    {
                        row.Spacing(6);
                        Kpi(row.RelativeItem(), t, "PRIX RENSEIGNÉS", Percent(d.PriceCoverage), d.PricedLineCount + " / " + d.LineCount + " lignes",
                            d.PriceCoverage >= 1m ? t.Ok : t.Critical, d.PriceCoverage, Palette.Amber);
                        Kpi(row.RelativeItem(), t, "FIABILITÉ", d.ReliabilityIndex.HasValue ? Percent(d.ReliabilityIndex.Value) : "—", "part mesurée du montant",
                            d.ReliabilityIndex >= 0.8m ? t.Ok : t.Warn, d.ReliabilityIndex ?? 0m, Palette.Teal);
                        Kpi(row.RelativeItem(), t, "AUDIT", d.AuditErrors + " err. · " + d.AuditWarnings + " av.",
                            d.ElementCount + " éléments · " + d.ExcludedCount + " exclus", d.AuditErrors > 0 ? t.Critical : (d.AuditWarnings > 0 ? t.Warn : t.Ok), accent: Palette.Magenta);
                        Kpi(row.RelativeItem(), t, "RATIO ACIER", d.RebarRatio.HasValue ? Money(d.RebarRatio.Value, 0) + " kg/m³" : "—", "armatures / béton",
                            t.Dark ? Palette.Orange : null, accent: Palette.Orange);
                    });

                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem(3).Element(e => Panel(e, t, "RÉPARTITION PAR LOT", c =>
                        {
                            decimal max = d.Lots.Count == 0 ? 0m : d.Lots.Max(l => l.Share);
                            foreach (LotShare lot in d.Lots)
                            {
                                c.Item().PaddingVertical(1.5f).Row(r =>
                                {
                                    r.ConstantItem(26).Text(lot.Code).Bold().FontColor(t.Dark ? Palette.ForLot(lot.Code) : t.Muted);
                                    r.RelativeItem(4).Text(Clean(lot.Designation) + (lot.IsComplete ? string.Empty : " *")).FontSize(7.5f);
                                    r.RelativeItem(3).AlignMiddle().Element(b => Bar(b, t, max > 0m ? (float)(lot.Share / max) : 0f, t.Dark ? Palette.ForLot(lot.Code) : (lot.IsComplete ? t.Accent : t.Warn)));
                                    r.ConstantItem(36).AlignRight().Text(Percent(lot.Share)).FontSize(7.5f);
                                });
                            }
                            if (d.Lots.Any(l => !l.IsComplete))
                            {
                                c.Item().PaddingTop(3).Text("* lot avec des lignes sans prix").FontSize(6.5f).FontColor(t.Warn);
                            }
                        }));
                        row.RelativeItem(2).Element(e => Panel(e, t, "VOYANTS", c =>
                        {
                            foreach (DashboardSignal s in d.Signals.Take(8))
                            {
                                c.Item().PaddingVertical(2).Row(r =>
                                {
                                    r.ConstantItem(8).PaddingTop(2).Height(6).Width(6).Background(LevelColor(t, s.Level));
                                    r.RelativeItem().Column(x =>
                                    {
                                        x.Item().Text(Clean(s.Title)).Bold().FontSize(7.5f);
                                        if (!string.IsNullOrWhiteSpace(s.Action))
                                        {
                                            x.Item().Text(Clean(s.Action)).FontSize(6.5f).FontColor(t.Muted);
                                        }
                                    });
                                });
                            }
                        }));
                    });

                    col.Item().Row(row =>
                    {
                        row.Spacing(8);
                        row.RelativeItem(3).Element(e => Panel(e, t, "PARETO — LES ARTICLES QUI FONT LE DEVIS", c =>
                        {
                            if (d.Pareto.Count == 0)
                            {
                                c.Item().Text("Aucun montant : saisir les prix unitaires.").FontColor(t.Muted);
                            }
                            foreach (ParetoLine l in d.Pareto)
                            {
                                c.Item().PaddingVertical(1.2f).Row(r =>
                                {
                                    r.ConstantItem(14).Text(l.Rank.ToString(Fr)).FontColor(t.Accent2).Bold();
                                    r.ConstantItem(34).Text(Clean(l.Code)).FontColor(t.Muted).FontSize(7);
                                    r.RelativeItem().Text(Clean(l.Designation) + (l.Reliability == Reliability.Estimated ? " (E)" : string.Empty)).FontSize(7);
                                    r.ConstantItem(62).AlignRight().Text(Money(l.Amount)).FontSize(7);
                                    r.ConstantItem(36).AlignRight().Text(Percent(l.CumulativeShare)).FontSize(7).FontColor(t.Accent);
                                });
                            }
                            if (d.Pareto.Count > 0)
                            {
                                c.Item().PaddingTop(2).Text("Dernière colonne : part cumulée du total des travaux.").FontSize(6.5f).FontColor(t.Muted);
                            }
                        }));
                        row.RelativeItem(2).Element(e => Panel(e, t, "QUANTITÉS CLÉS (NETTES)", c =>
                        {
                            if (d.KeyQuantities.Count == 0)
                            {
                                c.Item().Text("—").FontColor(t.Muted);
                            }
                            foreach (KeyQuantity k in d.KeyQuantities)
                            {
                                c.Item().PaddingVertical(1.2f).Row(r =>
                                {
                                    r.RelativeItem().Text(Clean(k.Label)).FontSize(7.5f);
                                    r.ConstantItem(70).AlignRight().Text(Money(k.Value, k.Unit == BoqUnit.Kilogram ? 0 : 2) + " " + k.Unit.Symbol()).FontSize(7.5f).Bold();
                                    r.ConstantItem(40).AlignRight().Text(Percent(k.MeasuredShare)).FontSize(6.5f).FontColor(k.MeasuredShare >= 1m ? t.Ok : t.Warn);
                                });
                            }
                            c.Item().PaddingTop(2).Text("Dernière colonne : part mesurée (le reste est estimé).").FontSize(6.5f).FontColor(t.Muted);
                        }));
                    });

                    if (d.MissingPrices.Count > 0)
                    {
                        col.Item().Element(e => Panel(e, t, "PRIX À SAISIR (" + d.MissingPrices.Count + ")", c =>
                        {
                            foreach (MissingPrice m in d.MissingPrices.Take(12))
                            {
                                c.Item().Row(r =>
                                {
                                    r.ConstantItem(50).Text(Clean(m.Code)).FontColor(t.Critical);
                                    r.RelativeItem().Text(Clean(m.Designation)).FontSize(7);
                                    r.ConstantItem(80).AlignRight().Text(Money(m.Quantity) + " " + m.Unit.Symbol()).FontSize(7);
                                });
                            }
                            if (d.MissingPrices.Count > 12)
                            {
                                c.Item().Text("… et " + (d.MissingPrices.Count - 12) + " autre(s) : voir le classeur.").FontSize(7).FontColor(t.Muted);
                            }
                        }));
                    }
                    col.Item().Text("Indicateurs calculés uniquement à partir du BOQ, de l'audit et du métré de cette analyse. La note de santé " +
                                    "(prix 50, fiabilité 30, audit 20) aide au pilotage ; elle ne remplace pas la vérification du métreur.")
                        .FontSize(6.5f).FontColor(t.Muted).Italic();
                });

                page.Footer().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — " + ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr)))
                    .FontSize(6.5f).FontColor(t.Muted);
            });
        }

        private static SignalLevel ScoreLevel(int score) => score >= 85 ? SignalLevel.Ok : score >= 60 ? SignalLevel.Attention : SignalLevel.Critique;

        private static string LevelColor(PdfTheme t, SignalLevel level)
        {
            switch (level)
            {
                case SignalLevel.Critique: return t.Critical;
                case SignalLevel.Attention: return t.Warn;
                case SignalLevel.Info: return t.Accent;
                default: return t.Ok;
            }
        }

        private static void Card(IContainer c, PdfTheme t, string label, string value) =>
            c.BorderLeft(2).BorderColor(t.Accent).Background(t.Panel).PaddingVertical(6).PaddingHorizontal(8).Column(x =>
            {
                x.Item().Text(label).FontSize(6.5f).FontColor(t.Muted).LetterSpacing(0.15f);
                x.Item().Text(Clean(value)).Bold().FontSize(9);
            });

        private static void Kpi(IContainer c, PdfTheme t, string label, string value, string sub, string color = null, decimal? gauge = null, string accent = null) =>
            c.Background(t.Panel).BorderTop(2.5f).BorderColor(t.Dark && accent != null ? accent : t.PanelBorder).Padding(7).Column(x =>
            {
                x.Item().Text(label).FontSize(6.5f).FontColor(t.Muted).LetterSpacing(0.15f);
                x.Item().Text(Clean(value)).Bold().FontSize(12).FontColor(color ?? t.Text);
                if (gauge.HasValue)
                {
                    x.Item().PaddingTop(3).Element(b => Bar(b, t, (float)gauge.Value, color ?? t.Accent));
                }
                if (!string.IsNullOrWhiteSpace(sub))
                {
                    x.Item().PaddingTop(2).Text(Clean(sub)).FontSize(6.5f).FontColor(t.Muted);
                }
            });

        /// <summary>Couleur du titre de chaque panneau (theme nuit) : meme code couleur que la fenetre.</summary>
        private static string TitleColor(string title) =>
            title.StartsWith("VOYANTS", StringComparison.Ordinal) ? Palette.Amber :
            title.StartsWith("PARETO", StringComparison.Ordinal) ? Palette.Orange :
            title.StartsWith("QUANTIT", StringComparison.Ordinal) ? Palette.Green :
            title.StartsWith("PRIX", StringComparison.Ordinal) ? Palette.Red : Palette.Cyan;

        private static void Panel(IContainer c, PdfTheme t, string title, Action<ColumnDescriptor> body) =>
            c.Border(0.8f).BorderColor(t.PanelBorder).Background(t.Panel).Padding(8).Column(x =>
            {
                x.Item().PaddingBottom(4).Text(title).Bold().FontSize(7.5f).FontColor(t.Dark ? TitleColor(title) : t.Accent).LetterSpacing(0.12f);
                body(x);
            });

        private static void Bar(IContainer container, PdfTheme t, float share, string color)
        {
            share = Math.Max(0f, Math.Min(1f, share));
            container.Height(5).Background(t.PanelBorder).Row(row =>
            {
                if (share > 0.0005f)
                {
                    row.RelativeItem(share).Background(color);
                }
                if (share < 0.9995f)
                {
                    row.RelativeItem(1f - share);
                }
            });
        }

        private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
