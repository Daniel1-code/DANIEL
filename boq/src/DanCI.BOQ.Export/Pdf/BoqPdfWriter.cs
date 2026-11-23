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
            // d'affaires annuel est inferieur a 1 000 000 USD. Eligibilite confirmee par DanCI le
            // 22/11/2026 (README) ; a revoir si le seuil est depasse ou en cas de diffusion.
            QuestPDF.Settings.License = LicenseType.Community;
            // Pas d'exception sur un glyphe absent de la police : le texte est assaini avant.
            QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;
        }

        /// <summary>Declare la licence et les reglages QuestPDF (appel sans effet, declenche le constructeur statique).</summary>
        public static void EnsureConfigured()
        {
        }

        public static void Write(ExportContext ctx, string path)
        {
            if (ctx?.Boq == null) throw new ArgumentException("Aucun BOQ à exporter.", nameof(ctx));
            byte[] pdf = Generate(ctx);
            File.WriteAllBytes(path, pdf);
        }

        public static byte[] Generate(ExportContext ctx) => Compose(ctx).GeneratePdf();

        /// <summary>
        /// Pages du PDF en images PNG, pour l'apercu avant export : MEME document que le fichier
        /// (meme composition), seule la sortie change.
        /// </summary>
        public static System.Collections.Generic.IReadOnlyList<byte[]> GeneratePageImages(ExportContext ctx, int dpi = 110)
        {
            if (ctx?.Boq == null) throw new ArgumentException("Aucun BOQ à prévisualiser.", nameof(ctx));
            return Compose(ctx).GenerateImages(new ImageGenerationSettings
            {
                ImageFormat = ImageFormat.Png,
                RasterDpi = Math.Max(48, Math.Min(200, dpi))
            }).ToList();
        }

        public static IDocument Compose(ExportContext ctx)
        {
            BoqResult boq = ctx.Boq;
            EstimateSettings settings = ctx.Configuration?.Settings ?? new EstimateSettings();
            string cur = boq.Currency.ToString();

            return Document.Create(container =>
            {
                CoverAndDashboardPdf.Cover(container, ctx);
                CoverAndDashboardPdf.Dashboard(container, ctx);
                container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(14, Unit.Millimetre);
                page.DefaultTextStyle(t => t.FontSize(8.5f));

                page.Header().Column(h =>
                {
                    h.Item().Row(row =>
                    {
                        if (!string.IsNullOrEmpty(ctx.EffectiveLogo) && File.Exists(ctx.EffectiveLogo))
                        {
                            row.ConstantItem(70).Height(35).Image(ctx.EffectiveLogo).FitArea();
                            row.ConstantItem(8);
                        }
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("BORDEREAU DES QUANTITÉS ET DEVIS ESTIMATIF").Bold().FontSize(12).FontColor(Blue);
                            c.Item().Text(Clean(ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number))).FontSize(10);
                        });
                        row.ConstantItem(150).AlignRight().Column(c =>
                        {
                            c.Item().AlignRight().Text(Clean(PartyInfo.JoinPublic(" — ", ctx.Company.DisplayShort, ctx.Company.Name == ctx.Company.DisplayShort ? null : ctx.Company.Name))).Bold().FontColor(Blue);
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

                    int unpriced = boq.AllLines.Count(l => !l.Amount.HasValue);
                    if (!boq.IsComplete)
                    {
                        col.Item().Background("#FDECEA").BorderLeft(3).BorderColor("#C00000").PaddingVertical(4).PaddingHorizontal(6).Text(
                            unpriced > 0
                                ? "ATTENTION : " + unpriced + " ligne(s) sans prix unitaire — les totaux sont des MINORANTS du coût réel."
                                : "ATTENTION : devis incomplet (voir l'audit) — les totaux sont des MINORANTS du coût réel.").Bold().FontColor("#C00000");
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

                        if (settings.IsByLevel)
                        {
                            // BOQ par NIVEAU : chapitre = niveau (cote en m), puis ses sous-lots et leurs articles.
                            var blocks = DanCI.BOQ.Core.Engine.LevelBreakdown.Build(boq, ctx.LevelOrder, null, null, null,
                                DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(ctx.LevelOrder, ctx.Configuration?.Extraction));
                            int chapter = 0;
                            foreach (var b in blocks)
                            {
                                chapter++;
                                string label = DanCI.BOQ.Core.Engine.LevelBreakdown.Label(b.IsWithoutLevel ? null : b.Level, ctx.LevelElevations);
                                string elevation = !b.IsWithoutLevel && ctx.LevelElevations != null && ctx.LevelElevations.ContainsKey(b.Level)
                                    ? label.Substring(label.LastIndexOf('(') + 1).TrimEnd(')') : null;
                                LevelBand(table, 6, "NIVEAU " + chapter + "  ·  " + (b.IsWithoutLevel ? label : b.Level).ToUpperInvariant(), elevation);
                                foreach (var tg in b.Lots.GroupBy(l => l.Trade ?? Trades.Of(l.Code)))
                                {
                                TradeBand(table, 6, tg.Key);
                                foreach (var lot in tg)
                                {
                                    LotBand(table, 6, lot.Code, chapter + "." + lot.Code + "   Lot " + lot.Code + " — " + lot.Designation);
                                    int k = 0;
                                    foreach (var a in lot.Articles)
                                    {
                                        int decimals = settings.DecimalsFor(a.Unit);
                                        bool z = k++ % 2 == 1;
                                        Cell(table, z).Text(Clean(a.Code)).FontColor(Colors.Grey.Darken2);
                                        Cell(table, z).Text(Clean(a.Line.DesignationWithDerived + (a.Reliability == Reliability.Estimated ? " (E)" : string.Empty)));
                                        Cell(table, z).AlignCenter().Text(a.Unit.Symbol());
                                        Cell(table, z).AlignRight().Text(Number(a.Quantity, decimals));
                                        Cell(table, z).AlignRight().Text(a.UnitPrice.HasValue ? Number(a.UnitPrice.Value, settings.AmountDecimals) : "—");
                                        Cell(table, z).AlignRight().Text(a.Amount.HasValue ? Number(a.Amount.Value, settings.AmountDecimals) : "—").SemiBold();
                                    }
                                    Subtotal(table, 6, "Sous-total lot " + lot.Code + (lot.IsComplete ? string.Empty : " (incomplet)"), Number(lot.Total, settings.AmountDecimals), false);
                                }
                                Subtotal(table, 6, "Sous-total " + tg.Key + " — " + (b.IsWithoutLevel ? "sans niveau" : b.Level), Number(tg.Sum(l => l.Total), settings.AmountDecimals), true);
                                }
                                table.Cell().ColumnSpan(5).Background("#0B1F3A").PaddingVertical(4).PaddingHorizontal(6).AlignRight()
                                    .Text(Clean("TOTAL NIVEAU " + chapter + " — " + label + (b.IsComplete ? string.Empty : " (incomplet)"))).Bold().FontColor(Colors.White);
                                table.Cell().Background("#0B1F3A").PaddingVertical(4).PaddingHorizontal(3).AlignRight()
                                    .Text(Number(b.Total, settings.AmountDecimals)).Bold().FontColor("#7CF2FF");
                            }
                        }
                        else
                        foreach (BoqLotResult lot in boq.Lots)
                        {
                            LotBand(table, 6, lot.Code, "LOT " + lot.Code + " — " + lot.Designation);
                            int k = 0;
                            foreach (BoqLine line in lot.Lines)
                            {
                                int decimals = settings.DecimalsFor(line.Unit);
                                bool z = k++ % 2 == 1;
                                Cell(table, z).Text(Clean(line.Code)).FontColor(Colors.Grey.Darken2);
                                Cell(table, z).Text(Clean(line.DesignationWithDerived + (line.Reliability == Reliability.Estimated ? " (E)" : string.Empty)));
                                Cell(table, z).AlignCenter().Text(line.Unit.Symbol());
                                Cell(table, z).AlignRight().Text(Number(line.Quantity, decimals));
                                Cell(table, z).AlignRight().Text(line.UnitPrice.HasValue ? Number(line.UnitPrice.Value, settings.AmountDecimals) : "—");
                                Cell(table, z).AlignRight().Text(line.Amount.HasValue ? Number(line.Amount.Value, settings.AmountDecimals) : "—").SemiBold();
                            }
                            Subtotal(table, 6, "Sous-total LOT " + lot.Code + (lot.IsComplete ? string.Empty : " (incomplet)"), Number(lot.Total, settings.AmountDecimals), true);
                        }
                    });

                    // Bordereau des prix unitaires (dynamique : articles et variantes du modele, prix en lettres).
                    var bpu = DanCI.BOQ.Core.Pricing.UnitPriceSchedule.Build(boq, ctx.Configuration?.Prices);
                    if (bpu.Count > 0)
                    {
                        col.Item().PageBreak();
                        col.Item().Text("BORDEREAU DES PRIX UNITAIRES").Bold().FontSize(11).FontColor(Blue);
                        col.Item().PaddingBottom(4).Text(Clean("Prix hors taxes en " + cur + ", établis pour les ouvrages présents dans le modèle ; sous chaque article, ses variantes " +
                            "(famille, type, matériau) et le prix qui s'y applique. « À CHIFFRER » : aucun prix (rien n'est inventé).")).FontSize(7).Italic().FontColor(Colors.Grey.Darken2);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(42);
                                c.RelativeColumn(4);
                                c.ConstantColumn(30);
                                c.ConstantColumn(62);
                                c.RelativeColumn(3);
                            });
                            table.Header(h =>
                            {
                                foreach (string t in new[] { "N°", "Désignation", "Unité", "PU (" + cur + ")", "Prix unitaire en toutes lettres" })
                                {
                                    h.Cell().Background(Blue).Padding(3).Text(t).FontColor(Colors.White).Bold();
                                }
                            });
                            string lotCode = null;
                            int bpuIndex = 0;
                            foreach (var row in bpu)
                            {
                                if (row.LotCode != lotCode)
                                {
                                    lotCode = row.LotCode;
                                    LotBand(table, 5, row.LotCode, "LOT " + row.LotCode + " — " + row.LotDesignation);
                                    bpuIndex = 0;
                                }
                                bool z = !row.IsVariant && bpuIndex++ % 2 == 1;
                                if (row.IsVariant)
                                {
                                    Cell(table).Text(string.Empty);
                                    Cell(table).Text(Clean(row.Designation)).FontSize(7).FontColor(row.HasOwnPrice ? Blue : Colors.Grey.Darken1);
                                    Cell(table).AlignCenter().Text(row.Unit.Symbol()).FontSize(7);
                                    Cell(table).AlignRight().Text(row.UnitPrice.HasValue ? Number(row.UnitPrice.Value, settings.AmountDecimals) : "à chiffrer").FontSize(7);
                                    Cell(table).Text(Clean(row.UnitPriceInWords)).FontSize(7).Italic();
                                }
                                else
                                {
                                    Cell(table, z).Text(Clean(row.Code)).Bold();
                                    Cell(table, z).Text(Clean(row.Designation)).Bold();
                                    Cell(table, z).AlignCenter().Text(row.Unit.Symbol());
                                    if (row.UnitPrice.HasValue) Cell(table, z).AlignRight().Text(Number(row.UnitPrice.Value, settings.AmountDecimals)).Bold();
                                    else Cell(table, z).AlignRight().Text("À CHIFFRER").Bold().FontColor("#C00000");
                                    Cell(table, z).Text(Clean(row.UnitPriceInWords)).Italic().FontColor("#3A4A63");
                                }
                            }
                        });
                    }

                    col.Item().PageBreak();
                    if (settings.IsByLevel)
                    {
                        // Recapitulatif par niveau, avant celui par lot.
                        var blocks = DanCI.BOQ.Core.Engine.LevelBreakdown.Build(boq, ctx.LevelOrder, null, null, null,
                            DanCI.BOQ.Core.Engine.LevelBreakdown.GroundLevel(ctx.LevelOrder, ctx.Configuration?.Extraction));
                        col.Item().Text("RÉCAPITULATIF PAR NIVEAU").Bold().FontSize(11).FontColor(Blue);
                        col.Item().Table(table =>
                        {
                            table.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(60);
                                c.RelativeColumn(3);
                                c.ConstantColumn(80);
                                c.ConstantColumn(40);
                                c.RelativeColumn(2);
                            });
                            foreach (string h in new[] { "Niveau", "Désignation (cote)", "Montant HT", "Part", "Répartition" })
                            {
                                table.Cell().Background(Blue).Padding(3).Text(h).FontColor(Colors.White).Bold();
                            }
                            int n = 0;
                            decimal all = blocks.Sum(x => x.Total);
                            foreach (var b in blocks)
                            {
                                decimal share = all > 0 ? b.Total / all : 0m;
                                Cell(table).Text("Niveau " + (++n));
                                Cell(table).Text(Clean(DanCI.BOQ.Core.Engine.LevelBreakdown.Label(b.IsWithoutLevel ? null : b.Level, ctx.LevelElevations) +
                                                       " — lots " + string.Join(", ", b.Lots.Select(l => l.Code))));
                                Cell(table).AlignRight().Text(Number(b.Total, settings.AmountDecimals));
                                Cell(table).AlignRight().Text((share * 100m).ToString("0.0", Fr) + " %");
                                Cell(table).AlignMiddle().Element(e => Bar(e, (float)share));
                            }
                        });
                        col.Item().PaddingTop(8);
                    }
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

                    col.Item().PaddingTop(4).Border(0.5f).BorderColor(Blue).Background(LightBlue).Padding(6)
                        .Text(Clean(AmountInWords.Sentence(boq.TotalIncludingVat, boq.Currency)) +
                              (boq.IsComplete ? string.Empty : " (montant INCOMPLET : lignes sans prix)")).Bold().FontColor(Blue);

                    col.Item().Text(text =>
                    {
                        text.Span("(E) : quantité estimée en tout ou partie (ratio, hypothèse), justifiée dans le classeur (feuilles Audit et Hypothèses). ").Italic();
                        text.Span(boq.PricedQuantity == PricedQuantity.Nette
                            ? "Quantités nettes en place ; les pertes sont comprises dans les prix unitaires. "
                            : "Quantités brutes (pertes comprises) ; les prix unitaires sont hors pertes. ").Italic();
                        text.Span(boq.ReliabilityIndex.HasValue
                            ? "Indice de fiabilité : " + (boq.ReliabilityIndex.Value * 100m).ToString("0.0", Fr) + " % du montant issu de quantités mesurées."
                            : "Indice de fiabilité : non calculable (aucun montant).").Italic();
                    });

                    ProjectInfo terms = ctx.Project ?? new ProjectInfo();
                    if (!string.IsNullOrWhiteSpace(terms.ValidityDays) || !string.IsNullOrWhiteSpace(terms.ExecutionTime) ||
                        !string.IsNullOrWhiteSpace(terms.PaymentTerms) || !string.IsNullOrWhiteSpace(terms.Notes) || !string.IsNullOrWhiteSpace(ctx.Company.Bank))
                    {
                        col.Item().PaddingTop(6).Column(c =>
                        {
                            c.Item().Text("CONDITIONS DE L'OFFRE").Bold().FontColor(Blue);
                            if (!string.IsNullOrWhiteSpace(terms.ValidityDays)) c.Item().Text("Validité de l'offre : " + Clean(terms.ValidityDays) + " jours");
                            if (!string.IsNullOrWhiteSpace(terms.ExecutionTime)) c.Item().Text("Délai d'exécution : " + Clean(terms.ExecutionTime));
                            if (!string.IsNullOrWhiteSpace(terms.PaymentTerms)) c.Item().Text("Conditions de paiement : " + Clean(terms.PaymentTerms));
                            if (!string.IsNullOrWhiteSpace(ctx.Company.Bank)) c.Item().Text("Paiement : " + Clean(ctx.Company.Bank));
                            if (!string.IsNullOrWhiteSpace(terms.Notes)) c.Item().Text(Clean(terms.Notes)).Italic();
                        });
                    }

                    // Code QR de verification a cote des signatures.
                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.ConstantItem(70).Height(70).Image(VerificationCode.Png(VerificationCode.Payload(ctx), 5)).FitArea();
                        row.ConstantItem(8);
                        row.RelativeItem().AlignMiddle().Text(Clean(
                            "Code de vérification : il contient l'émetteur, le projet, l'indice, la date, le montant TTC et l'empreinte complète du métré (" +
                            DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint) + "…). Une nouvelle analyse du même modèle doit donner la même empreinte ; " +
                            "tout écart signale un devis modifié ou un modèle changé.")).FontSize(7).FontColor(Colors.Grey.Darken2);
                    });

                    col.Item().PaddingTop(16).Row(row =>
                    {
                        row.Spacing(10);
                        foreach (string who in new[] { "Établi par" + (string.IsNullOrWhiteSpace(ctx.Project?.Author) ? " (" + Clean(ctx.Company.DisplayShort) + ")" : " : " + Clean(ctx.Project.Author)), "Vérifié par" + (string.IsNullOrWhiteSpace(ctx.Project?.CheckedBy) ? string.Empty : " : " + Clean(ctx.Project.CheckedBy)), "Lu et approuvé — Maître d'ouvrage" })
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
            });
                CoverAndDashboardPdf.SchedulePage(container, ctx);
                GanttPages(container, ctx);
                CoverAndDashboardPdf.MaterialsPage(container, ctx);
            });
        }

        /// <summary>Planning des travaux : diagramme de Gantt, une ou plusieurs pages A4 paysage.</summary>
        private static void GanttPages(IDocumentContainer doc, ExportContext ctx)
        {
            DanCI.BOQ.Core.Planning.WorkPlan plan = ctx.WorkPlan;
            if (plan == null || plan.Tasks.Count == 0) return;
            foreach (string svg in GanttChart.Pages(plan, Clean(ctx.ProjectTitle)))
            {
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(0);
                    // Meme procede que la planche du tableau de bord : SVG a la taille de la page.
                    page.Background().Svg(svg);
                    page.Content().Text(string.Empty);
                });
            }
        }

        private static IContainer Cell(TableDescriptor table) =>
            table.Cell().BorderBottom(0.25f).BorderColor(Colors.Grey.Lighten2).PaddingVertical(2).PaddingHorizontal(3);

        /// <summary>Cellule d'une ligne d'article : une ligne sur deux legerement teintee (lecture facile).</summary>
        private static IContainer Cell(TableDescriptor table, bool zebra) =>
            (zebra ? table.Cell().Background("#F3F6FB") : table.Cell()).BorderBottom(0.25f).BorderColor("#DCE3EE").PaddingVertical(2.5f).PaddingHorizontal(3);

        /// <summary>Bandeau de niveau : fond nuit, filet cyan a gauche, cote dans une etiquette.</summary>
        private static void LevelBand(TableDescriptor table, int columns, string title, string elevation)
        {
            table.Cell().ColumnSpan((uint)columns).PaddingTop(6).Background("#0B1F3A").Row(row =>
            {
                row.ConstantItem(4).Background("#00B8D4");
                row.RelativeItem().PaddingVertical(5).PaddingHorizontal(7).Text(Clean(title)).Bold().FontSize(9.5f).FontColor(Colors.White).LetterSpacing(0.03f);
                if (!string.IsNullOrEmpty(elevation))
                {
                    row.AutoItem().AlignMiddle().PaddingRight(7).Background("#00B8D4").PaddingVertical(1.5f).PaddingHorizontal(6)
                        .Text(Clean(elevation)).Bold().FontSize(8).FontColor("#0B1F3A");
                }
            });
        }

        /// <summary>Bandeau de corps d'etat (GROS ŒUVRE / SECOND ŒUVRE) : filet bleu a gauche.</summary>
        private static void TradeBand(TableDescriptor table, int columns, string trade)
        {
            table.Cell().ColumnSpan((uint)columns).Background("#E3EAF5").Row(row =>
            {
                row.ConstantItem(3).Background(Blue);
                row.RelativeItem().PaddingVertical(3).PaddingHorizontal(6).Text(Clean(trade.ToUpperInvariant())).Bold().FontSize(8.5f).FontColor(Blue).LetterSpacing(0.06f);
            });
        }

        /// <summary>En-tete de lot : pastille de la couleur du lot (la meme que dans la fenetre et le Gantt).</summary>
        private static void LotBand(TableDescriptor table, int columns, string lotCode, string text)
        {
            table.Cell().ColumnSpan((uint)columns).BorderBottom(0.5f).BorderColor(DanCI.BOQ.Core.Dashboard.Palette.ForLot(lotCode)).PaddingTop(3).Row(row =>
            {
                row.ConstantItem(9).AlignMiddle().Height(9).Background(DanCI.BOQ.Core.Dashboard.Palette.ForLot(lotCode));
                row.RelativeItem().PaddingVertical(2.5f).PaddingHorizontal(5).Text(Clean(text)).Bold().FontColor(Blue);
            });
        }

        /// <summary>Ligne de sous-total : libelle a droite, montant encadre.</summary>
        private static void Subtotal(TableDescriptor table, int columns, string label, string amount, bool strong)
        {
            var l = table.Cell().ColumnSpan((uint)(columns - 1)).PaddingVertical(2.5f).PaddingRight(6).AlignRight().Text(Clean(label));
            var a = table.Cell().Background(strong ? "#DDEBF7" : "#F0F4FA").BorderTop(strong ? 0.75f : 0.4f).BorderColor(Blue)
                .PaddingVertical(2.5f).PaddingHorizontal(3).AlignRight().Text(amount);
            if (strong) { l.Bold(); a.Bold(); } else { l.Italic().FontColor(Colors.Grey.Darken2); a.SemiBold(); }
        }

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
