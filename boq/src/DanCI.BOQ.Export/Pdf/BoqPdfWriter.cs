using System;
using System.Globalization;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// PDF du devis, pret a signer : en-tete projet, BOQ par lot avec sous-totaux, recapitulatif
    /// avec graphique de repartition, pied de devis, indice de fiabilite, cases de signature.
    /// Les lignes estimees sont marquees (E), comme dans le classeur.
    /// </summary>
    public static class BoqPdfWriter
    {
        private const string Blue = "#1F4E79";
        private const string LightBlue = "#DDEBF7";
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        static BoqPdfWriter()
        {
            // Licence Community de QuestPDF : gratuite pour une entreprise dont le chiffre
            // d'affaires annuel est inferieur a 1 000 000 USD (voir README : a confirmer par DanCI).
            QuestPDF.Settings.License = LicenseType.Community;
            // Pas d'exception sur un glyphe absent de la police : le texte est assaini avant.
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }

        public static void Write(ExportContext ctx, string path)
        {
            if (ctx?.Boq == null) throw new ArgumentException("Aucun BOQ à exporter.", nameof(ctx));
            byte[] pdf = Generate(ctx);
            File.WriteAllBytes(path, pdf);
        }

        public static byte[] Generate(ExportContext ctx)
        {
            BoqResult boq = ctx.Boq;
            EstimateSettings settings = ctx.Configuration?.Settings ?? new EstimateSettings();
            string cur = boq.Currency.ToString();

            return Document.Create(container => container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(14, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(8.5f));

                page.Header().Column(h =>
                {
                    h.Item().Row(row =>
                    {
                        if (!string.IsNullOrEmpty(ctx.LogoPath) && File.Exists(ctx.LogoPath))
                        {
                            row.ConstantItem(70).Height(35).Image(ctx.LogoPath).FitArea();
                            row.ConstantItem(8);
                        }
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BORDEREAU DES QUANTITÉS ET DEVIS ESTIMATIF").Bold().FontSize(12).FontColor(Blue);
                            c.Item().Text(Clean(ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number))).FontSize(10);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text("DanCI — Dan Consult Intelligente").Bold().FontColor(Blue);
                            c.Item().AlignRight().Text("Indice : " + Clean(Or(ctx.Project?.Revision, "—")));
                            c.Item().AlignRight().Text(Clean(Or(ctx.Project?.IssueDate, ctx.GeneratedAt.ToString("dd/MM/yyyy", Fr))));
                        });
                    });
                    h.Item().PaddingTop(4).LineHorizontal(0.75f).LineColor(Blue);
                });

                page.Content().PaddingTop(6).Column(col =>
                {
                    col.Spacing(6);
                    col.Item().Text(text =>
                    {
                        text.Span("Maître d'ouvrage : ").Bold();
                        text.Span(Clean(Or(ctx.Project?.Client, "—")));
                        text.Span("     Lieu : ").Bold();
                        text.Span(Clean(Or(ctx.Project?.Location, "—")));
                        text.Span("     Montants en ").Bold();
                        text.Span(cur);
                    });

                    if (!boq.IsComplete)
                    {
                        col.Item().Background("#FDECEA").Padding(4).Text(
                            "ATTENTION : " + boq.AllLines.Count(l => !l.Amount.HasValue) +
                            " ligne(s) sans prix unitaire — les totaux sont des MINORANTS du coût réel.").Bold().FontColor("#C00000");
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(48);
                            c.RelativeColumn();
                            c.ConstantColumn(28);
                            c.ConstantColumn(58);
                            c.ConstantColumn(58);
                            c.ConstantColumn(68);
                        });
                        table.Header(header =>
                        {
                            foreach (string h in new[] { "N°", "Désignation", "Unité", "Quantité", "PU (" + cur + ")", "Montant (" + cur + ")" })
                            {
                                header.Cell().Background(Blue).Padding(3).Text(h).FontColor(Colors.White).Bold();
                            }
                        });

                        foreach (BoqLotResult lot in boq.Lots)
                        {
                            table.Cell().ColumnSpan(6).Background(LightBlue).Padding(3)
                                .Text(Clean("LOT " + lot.Code + " — " + lot.Designation)).Bold().FontColor(Blue);
                            foreach (BoqLine line in lot.Lines)
                            {
                                int decimals = settings.DecimalsFor(line.Unit);
                                Cell(table).Text(Clean(line.Code));
                                Cell(table).Text(Clean(line.Designation + (line.Reliability == Reliability.Estimated ? " (E)" : string.Empty)));
                                Cell(table).AlignCenter().Text(line.Unit.Symbol());
                                Cell(table).AlignRight().Text(Number(line.Quantity, decimals));
                                Cell(table).AlignRight().Text(line.UnitPrice.HasValue ? Number(line.UnitPrice.Value, settings.AmountDecimals) : "—");
                                Cell(table).AlignRight().Text(line.Amount.HasValue ? Number(line.Amount.Value, settings.AmountDecimals) : "—");
                            }
                            table.Cell().ColumnSpan(5).BorderTop(0.5f).Padding(3).AlignRight()
                                .Text(Clean("Sous-total LOT " + lot.Code + (lot.IsComplete ? string.Empty : " (incomplet)"))).Bold();
                            table.Cell().BorderTop(0.5f).Padding(3).AlignRight().Text(Number(lot.Total, settings.AmountDecimals)).Bold();
                        }
                    });

                    col.Item().PageBreak();
                    col.Item().Text("RÉCAPITULATIF PAR LOT").Bold().FontSize(11).FontColor(Blue);
                    decimal works = boq.WorksTotal;
                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.ConstantColumn(40);
                            c.RelativeColumn(3);
                            c.ConstantColumn(80);
                            c.ConstantColumn(40);
                            c.RelativeColumn(2);
                        });
                        foreach (string h in new[] { "Lot", "Désignation", "Montant HT", "Part", "Répartition" })
                        {
                            table.Cell().Background(Blue).Padding(3).Text(h).FontColor(Colors.White).Bold();
                        }
                        foreach (BoqLotResult lot in boq.Lots)
                        {
                            decimal share = works > 0 ? lot.Total / works : 0m;
                            Cell(table).Text(lot.Code);
                            Cell(table).Text(Clean(lot.Designation + (lot.IsComplete ? string.Empty : " (incomplet)")));
                            Cell(table).AlignRight().Text(Number(lot.Total, settings.AmountDecimals));
                            Cell(table).AlignRight().Text((share * 100m).ToString("0.0", Fr) + " %");
                            Cell(table).AlignMiddle().Element(e => Bar(e, (float)share));
                        }
                    });

                    col.Item().PaddingTop(4).AlignRight().Width(300).Table(table =>
                    {
                        table.ColumnsDefinition(c =>
                        {
                            c.RelativeColumn();
                            c.ConstantColumn(95);
                        });
                        void Line(string label, decimal amount, bool strong = false)
                        {
                            var l = table.Cell().Padding(2).AlignRight().Text(Clean(label));
                            var a = table.Cell().Padding(2).AlignRight().Text(Number(amount, settings.AmountDecimals) + " " + cur);
                            if (strong)
                            {
                                l.Bold();
                                a.Bold();
                            }
                        }
                        Line("Total des travaux HT", boq.WorksTotal, true);
                        foreach (MarkupLine m in boq.Markups)
                        {
                            Line(m.Designation + " (" + m.Percent.ToString("0.##", Fr) + " %)", m.Amount);
                        }
                        Line("Total HT", boq.TotalExcludingVat, true);
                        Line("TVA " + boq.VatPercent.ToString("0.##", Fr) + " %", boq.Vat);
                        Line("TOTAL TTC", boq.TotalIncludingVat, true);
                        if (boq.TotalIncludingVatSecondary.HasValue && boq.SecondaryCurrency.HasValue)
                        {
                            table.Cell().ColumnSpan(2).Padding(2).AlignRight().Text(
                                "soit " + Number(boq.TotalIncludingVatSecondary.Value, settings.AmountDecimals) + " " + boq.SecondaryCurrency +
                                " (taux du " + (settings.ExchangeRate?.Date?.ToString("dd/MM/yyyy", Fr) ?? "—") + ")");
                        }
                    });

                    col.Item().Text(text =>
                    {
                        text.Span("(E) : quantité estimée en tout ou partie (ratio, hypothèse), justifiée dans le classeur (feuilles Audit et Hypothèses). ").Italic();
                        text.Span(boq.ReliabilityIndex.HasValue
                            ? "Indice de fiabilité : " + (boq.ReliabilityIndex.Value * 100m).ToString("0.0", Fr) + " % du montant issu de quantités mesurées."
                            : "Indice de fiabilité : non calculable (aucun montant).").Italic();
                    });

                    col.Item().PaddingTop(16).Row(row =>
                    {
                        row.Spacing(10);
                        foreach (string who in new[] { "Établi par (DanCI)", "Vérifié par", "Lu et approuvé — Maître d'ouvrage" })
                        {
                            row.RelativeItem().Border(0.5f).Height(70).Padding(4).Column(c =>
                            {
                                c.Item().Text(who).Bold();
                                c.Item().Text("Nom, date et signature :").FontSize(7).FontColor(Colors.Grey.Darken1);
                            });
                        }
                    });
                });

                page.Footer().Row(row =>
                {
                    row.RelativeItem().Text(Clean(ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — généré le " +
                                                  ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr) + " — empreinte du métré " +
                                                  DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint))).FontSize(7).FontColor(Colors.Grey.Darken1);
                    row.ConstantItem(80).AlignRight().Text(t =>
                    {
                        t.DefaultTextStyle(s => s.FontSize(7));
                        t.Span("Page ");
                        t.CurrentPageNumber();
                        t.Span(" / ");
                        t.TotalPages();
                    });
                });
            })).GeneratePdf();
        }

        private static IContainer Cell(TableDescriptor table) =>
            table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(3);

        private static void Bar(IContainer container, float share)
        {
            share = Math.Max(0f, Math.Min(1f, share));
            if (share <= 0.0005f)
            {
                container.Height(7);
                return;
            }
            container.Height(7).Row(row =>
            {
                row.RelativeItem(share).Background(Blue);
                if (share < 0.9995f)
                {
                    row.RelativeItem(1f - share);
                }
            });
        }

        private static string Number(decimal value, int decimals) =>
            Clean(value.ToString("N" + Math.Max(0, decimals), Fr));

        /// <summary>Espaces insecables (separateur de milliers francais) remplaces : la police du PDF ne les a pas toutes.</summary>
        private static string Clean(string s) =>
            (s ?? string.Empty).Replace(' ', ' ').Replace(' ', ' ');

        private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value;
    }
}
