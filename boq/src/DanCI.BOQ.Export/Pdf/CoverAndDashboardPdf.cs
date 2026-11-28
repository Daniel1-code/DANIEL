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
                            PartyInfo co = ctx.Company;
                            c.Item().Text(t2 => t2.Span(Clean(co.DisplayShort)).Bold().FontSize(18).FontColor(t.Accent).LetterSpacing(0.12f));
                            string tagline = PartyInfo.JoinPublic("  ·  ", co.Name == co.DisplayShort ? null : co.Name, co.Activity, co.City);
                            c.Item().Text(Clean(tagline).ToUpper(Fr)).FontSize(7).FontColor(t.Muted).LetterSpacing(0.15f);
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

                    string image = ctx.EffectiveProjectImage;
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
                        Card(row.RelativeItem(), t, "BUREAU D'ÉTUDES", Or(p.Engineer, ctx.Company.DisplayShort));
                        Card(row.RelativeItem(), t, "N° PROJET", Or(p.Number, "—"));
                    });

                    // Fiche client : logo, coordonnees, contact.
                    PartyInfo client = p.ClientParty;
                    if (client != null && !client.IsEmpty)
                    {
                        col.Item().PaddingTop(10).Background(t.Panel).BorderLeft(2).BorderColor(t.Accent2).Padding(8).Row(row =>
                        {
                            string clientLogo = ctx.ClientLogo;
                            if (clientLogo != null)
                            {
                                row.ConstantItem(70).Height(36).AlignMiddle().Image(clientLogo).FitArea();
                                row.ConstantItem(10);
                            }
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("CLIENT").FontSize(6.5f).FontColor(t.Muted).LetterSpacing(0.15f);
                                c.Item().Text(Clean(Or(client.Name, client.ShortName))).Bold().FontSize(10);
                                if (!string.IsNullOrWhiteSpace(client.AddressLine)) c.Item().Text(Clean(client.AddressLine)).FontSize(8).FontColor(t.Muted);
                                string contact = PartyInfo.JoinPublic("  ·  ", PartyInfo.JoinPublic(", ", client.Contact, client.ContactRole), client.ContactLine);
                                if (!string.IsNullOrWhiteSpace(contact)) c.Item().Text(Clean(contact)).FontSize(8).FontColor(t.Muted);
                            });
                        });
                    }

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

                page.Footer().PaddingHorizontal(48).PaddingBottom(14).Column(foot =>
                {
                    // Coordonnees et mentions legales de notre entreprise.
                    PartyInfo co = ctx.Company;
                    string coords = PartyInfo.JoinPublic("  ·  ", co.Name, co.AddressLine, co.ContactLine, co.LegalLine);
                    if (!string.IsNullOrWhiteSpace(coords))
                    {
                        foot.Item().PaddingBottom(4).BorderTop(0.5f).BorderColor(t.PanelBorder).PaddingTop(4).Text(Clean(coords)).FontSize(7).FontColor(t.Text);
                    }
                    foot.Item().Row(row =>
                {
                    // Code QR de verification (empreinte du metre, montant, indice).
                    row.ConstantItem(46).Height(46).Image(VerificationCode.Png(VerificationCode.Payload(ctx), 4, t.Dark)).FitArea();
                    row.ConstantItem(8);
                    row.RelativeItem().AlignBottom().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + "  ·  maquette " + ctx.DocumentTitle +
                                                  "  ·  empreinte " + DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint)))
                        .FontSize(7).FontColor(t.Muted);
                    row.ConstantItem(140).AlignRight().Text("Établi par " + Clean(Or(p.Author, ctx.Company.DisplayShort))).FontSize(7).FontColor(t.Muted);
                });
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
            if (t.Dark)
            {
                // Planche dessinee (meme rendu que la fenetre et le classeur), pleine page A4 paysage.
                string board = DashboardBoard.Svg(ctx, ctx.RevisionHistory);
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(0);
                    page.PageColor("#0E1730");
                    // Meme degrade que la planche sur toute la page : pas de bande plus sombre en haut et en bas.
                    page.Background().Svg("<svg xmlns='http://www.w3.org/2000/svg' width='842' height='595' viewBox='0 0 842 595' preserveAspectRatio='none'>" +
                                          "<defs><linearGradient id='p' x1='0' y1='0' x2='0' y2='1'><stop offset='0' stop-color='#1C2B4D'/>" +
                                          "<stop offset='1' stop-color='#0E1730'/></linearGradient></defs><rect width='842' height='595' fill='url(#p)'/></svg>");
                    page.Content().AlignCenter().AlignMiddle().Svg(DashboardBoard.WithoutBackground(board)).FitArea();
                });
                return;
            }
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

        /// <summary>Echeancier financier : planning des lots (barres) et besoin TTC mois par mois.</summary>
        public static void SchedulePage(IDocumentContainer doc, ExportContext ctx)
        {
            DanCI.BOQ.Core.Planning.ScheduleResult s = ctx.ScheduleResult;
            if (s == null)
            {
                return;
            }
            const string blue = "#1F4E79";
            string cur = s.Currency;
            doc.Page(page =>
            {
                page.Size(PageSizes.A4.Landscape());
                page.Margin(12, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(7.5f));
                page.Header().PaddingBottom(6).Column(h =>
                {
                    h.Item().Text("ÉCHÉANCIER FINANCIER — " + s.TotalMonths + " MOIS").Bold().FontSize(12).FontColor(blue);
                    h.Item().Text(Clean(ctx.ProjectTitle)).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    foreach (string note in s.Notes)
                    {
                        col.Item().Background("#FDECEA").Padding(3).Text(Clean(note)).FontColor("#C00000").Italic();
                    }
                    // Planning des lots : une barre par lot, a la couleur du lot.
                    col.Item().Text("PLANNING DES LOTS").Bold().FontColor(blue);
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(28);
                            c.ConstantColumn(150);
                            c.ConstantColumn(70);
                            for (int m = 0; m < s.TotalMonths; m++) c.RelativeColumn();
                        });
                        t.Cell().Background(blue).Padding(2).Text("Lot").FontColor(Colors.White).Bold();
                        t.Cell().Background(blue).Padding(2).Text("Désignation").FontColor(Colors.White).Bold();
                        t.Cell().Background(blue).Padding(2).AlignRight().Text("HT").FontColor(Colors.White).Bold();
                        for (int m = 0; m < s.TotalMonths; m++)
                        {
                            t.Cell().Background(blue).Padding(1).AlignCenter().Text((m + 1).ToString(Fr)).FontColor(Colors.White).FontSize(6);
                        }
                        foreach (var lot in s.Lots)
                        {
                            string color = DanCI.BOQ.Core.Dashboard.Palette.ForLot(lot.Lot);
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(lot.Lot).Bold();
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(Clean(lot.Designation) + (lot.DefaultPeriod ? " *" : string.Empty)).FontSize(6.5f);
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(Money(lot.Total)).FontSize(6.5f);
                            for (int m = 0; m < s.TotalMonths; m++)
                            {
                                bool active = m + 1 >= lot.Start && m + 1 < lot.Start + lot.Duration;
                                t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(0.5f)
                                    .Element(e => active ? e.Height(8).Background(color) : e.Height(8));
                            }
                        }
                    });
                    if (s.Lots.Any(l => l.DefaultPeriod))
                    {
                        col.Item().Text("* période par défaut (toute la durée) : à saisir dans Paramètres du projet > Échéancier.").FontSize(6.5f).Italic();
                    }

                    // Besoin TTC par mois.
                    col.Item().PaddingTop(4).Text("BESOIN DE FINANCEMENT TTC (" + cur + ")").Bold().FontColor(blue);
                    decimal peak = s.PeakTtc;
                    col.Item().Table(t =>
                    {
                        t.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(70);
                            c.ConstantColumn(80);
                            c.ConstantColumn(80);
                            c.ConstantColumn(85);
                            c.ConstantColumn(45);
                            c.RelativeColumn();
                        });
                        foreach (string h in new[] { "Mois", "Travaux HT", "TTC du mois", "TTC cumulé", "% cumulé", "" })
                        {
                            t.Cell().Background(blue).Padding(2).Text(h).FontColor(Colors.White).Bold();
                        }
                        foreach (var m in s.Months)
                        {
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(Clean(m.Label));
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(Money(m.Works));
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(Money(m.Ttc)).Bold();
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(Money(m.CumulativeTtc));
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight().Text(Percent(m.CumulativeShare));
                            t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(3).PaddingHorizontal(4)
                                .Element(e => Bar(e, PdfTheme.Classic, peak > 0m ? (float)(m.Ttc / peak) : 0f, blue));
                        }
                    });
                });
                page.Footer().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — montants HT des lots répartis uniformément sur leur période ; majorations et TVA au prorata."))
                    .FontSize(6.5f).FontColor(Colors.Grey.Darken1);
            });
        }

        /// <summary>Liste des materiaux a approvisionner (page blanche, imprimable).</summary>
        public static void MaterialsPage(IDocumentContainer doc, ExportContext ctx)
        {
            DanCI.BOQ.Core.Materials.MaterialList m = ctx.Materials;
            if (m == null || (m.Lines.Count == 0 && m.Totals.Count == 0))
            {
                return;
            }
            const string blue = "#1F4E79";
            doc.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(14, Unit.Millimetre);
                page.DefaultTextStyle(x => x.FontSize(8));
                page.Header().PaddingBottom(6).Column(h =>
                {
                    h.Item().Text("LISTE DES MATÉRIAUX À APPROVISIONNER").Bold().FontSize(12).FontColor(blue);
                    h.Item().Text(Clean(ctx.ProjectTitle)).FontSize(9).FontColor(Colors.Grey.Darken1);
                });
                page.Content().Column(col =>
                {
                    col.Spacing(6);
                    foreach (string note in m.Notes)
                    {
                        col.Item().Background("#FDECEA").Padding(4).Text(Clean(note)).FontColor("#C00000").Italic();
                    }
                    void Table(string title, IEnumerable<DanCI.BOQ.Core.Materials.MaterialLine> lines, bool basis)
                    {
                        col.Item().PaddingTop(4).Text(title).Bold().FontColor(blue);
                        col.Item().Table(t =>
                        {
                            t.ColumnsDefinition(c =>
                            {
                                c.RelativeColumn(3);
                                c.ConstantColumn(60);
                                c.ConstantColumn(34);
                                if (basis) c.RelativeColumn(4);
                            });
                            foreach (string h in basis ? new[] { "Matériau", "Quantité", "Unité", "Calcul" } : new[] { "Matériau", "Quantité", "Unité" })
                            {
                                t.Cell().Background(blue).Padding(3).Text(h).FontColor(Colors.White).Bold();
                            }
                            foreach (var l in lines)
                            {
                                t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(Clean(l.Material) + (l.FromEstimate ? " (E)" : string.Empty));
                                t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).AlignRight()
                                    .Text(Money(l.Quantity, l.Quantity == Math.Floor(l.Quantity) ? 0 : 2)).Bold();
                                t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(l.Unit);
                                if (basis) t.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).Padding(2).Text(Clean(l.Basis)).FontSize(6.5f).FontColor(Colors.Grey.Darken2);
                            }
                        });
                    }
                    if (m.Totals.Count > 0) Table("TOTAUX À COMMANDER", m.Totals, basis: false);
                    foreach (var g in m.Lines.GroupBy(l => l.Group))
                    {
                        Table(g.Key.ToUpper(Fr), g, basis: true);
                    }
                });
                page.Footer().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — hypothèses : config\\materiaux.json")).FontSize(6.5f).FontColor(Colors.Grey.Darken1);
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
