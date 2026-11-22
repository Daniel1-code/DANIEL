using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>Page de garde et tableau de bord du classeur.</summary>
    public static partial class BoqWorkbookWriter
    {
        public const string DashboardSheetName = "Tableau de bord";

        private sealed class SheetTheme
        {
            public XLColor Page, Panel, Text, Muted, Accent, Accent2, Ok, Warn, Critical;
            public bool Dark;
        }

        private static SheetTheme ThemeFor(ExportContext ctx) => ctx.Cover?.Theme == CoverTheme.Classique
            ? new SheetTheme
            {
                Page = XLColor.White, Panel = XLColor.FromHtml("#F3F7FB"), Text = XLColor.FromHtml("#1B2633"), Muted = XLColor.FromHtml("#5B6B7D"),
                Accent = XLColor.FromHtml("#1F4E79"), Accent2 = XLColor.FromHtml("#E07B00"), Ok = XLColor.FromHtml("#2E7D32"),
                Warn = XLColor.FromHtml("#B7791F"), Critical = XLColor.FromHtml("#C00000"), Dark = false
            }
            : new SheetTheme
            {
                Page = XLColor.FromHtml("#070D1A"), Panel = XLColor.FromHtml("#0F1A2E"), Text = XLColor.FromHtml("#E6F1FF"), Muted = XLColor.FromHtml("#8AA4C8"),
                Accent = XLColor.FromHtml("#00E5FF"), Accent2 = XLColor.FromHtml("#FF8A00"), Ok = XLColor.FromHtml("#21E6A1"),
                Warn = XLColor.FromHtml("#FFC233"), Critical = XLColor.FromHtml("#FF4D6D"), Dark = true
            };

        // ---------------------------------------------------------------- 1. Page de garde
        private static void Cover(IXLWorksheet ws, ExportContext ctx)
        {
            SheetTheme t = ThemeFor(ctx);
            ws.ShowGridLines = false;
            ws.Column(1).Width = 4;
            ws.Column(2).Width = 30;
            ws.Column(3).Width = 70;
            ws.Column(4).Width = 4;
            if (t.Dark)
            {
                // Fond nuit sur la zone de la page de garde ; les cartes recoivent ensuite leur couleur.
                ws.Range(1, 1, 160, 4).Style.Fill.SetBackgroundColor(t.Page);
            }
            int r = 2;

            string imported = ctx.Cover?.ImportedPage;
            if (!string.IsNullOrWhiteSpace(imported) && File.Exists(imported))
            {
                // Page de garde realisee a part : en tete, sur la largeur imprimable d'une page A4.
                var picture = ws.AddPicture(imported).MoveTo(ws.Cell(r, 2));
                picture.ScaleWidth(Math.Min(1.0, 680.0 / Math.Max(1, picture.OriginalWidth)));
                double heightPx = picture.OriginalHeight * Math.Min(1.0, 680.0 / Math.Max(1, picture.OriginalWidth));
                r += (int)Math.Ceiling(heightPx / 20.0) + 2;
                ws.Row(r - 1).AddHorizontalPageBreak();
            }

            string logo = ctx.EffectiveLogo;
            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
            {
                ws.AddPicture(logo).MoveTo(ws.Cell(r, 2)).WithSize(180, 90);
                r += 6;
            }
            ws.Cell(r, 2).Value = "DanCI — Dan Consult Intelligente";
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(t.Accent);
            r += 1;
            ws.Cell(r, 2).Value = "BUREAU D'ÉTUDES · KINSHASA, RDC";
            ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            r += 3;
            ws.Range(r, 2, r, 3).Merge().Value = "BORDEREAU DES QUANTITÉS & DEVIS ESTIMATIF";
            ws.Cell(r, 2).Style.Font.SetFontSize(11).Font.SetFontColor(t.Muted);
            r += 1;
            ws.Range(r, 2, r, 3).Merge().Value = (ctx.ProjectTitle ?? string.Empty).ToUpper(CultureInfo.GetCultureInfo("fr-FR"));
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(22).Font.SetFontColor(t.Text);
            ws.Row(r).Height = 32;
            r += 1;
            ProjectInfo p = ctx.Project ?? new ProjectInfo();
            string subtitle = string.IsNullOrWhiteSpace(ctx.Cover?.Subtitle) ? p.Purpose : ctx.Cover.Subtitle;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                ws.Range(r, 2, r, 3).Merge().Value = subtitle;
                ws.Cell(r, 2).Style.Font.SetFontSize(12).Font.SetFontColor(t.Accent);
                r += 1;
            }
            r += 1;
            string image = ctx.Cover?.ProjectImage;
            if (!string.IsNullOrWhiteSpace(image) && File.Exists(image))
            {
                var pic = ws.AddPicture(image).MoveTo(ws.Cell(r, 2));
                double scale = Math.Min(560.0 / Math.Max(1, pic.OriginalWidth), 300.0 / Math.Max(1, pic.OriginalHeight));
                pic.Scale(Math.Min(1.0, scale));
                r += (int)Math.Ceiling(pic.OriginalHeight * Math.Min(1.0, scale) / 20.0) + 1;
            }

            var rows = new List<(string, string)>
            {
                ("Projet", ctx.ProjectTitle),
                ("Objet", p.Purpose),
                ("Numéro de projet", p.Number),
                ("Référence du devis", p.QuoteReference),
                ("Maître d'ouvrage", p.Client),
                ("Maître d'œuvre", p.Architect),
                ("Bureau d'études", string.IsNullOrWhiteSpace(p.Engineer) ? ProductInfo.Author : p.Engineer),
                ("Entreprise", p.Contractor),
                ("Lieu", p.Location),
                ("Date", string.IsNullOrWhiteSpace(p.IssueDate) ? ctx.GeneratedAt.ToString("d MMMM yyyy", Fr) : p.IssueDate),
                ("Indice de révision", string.IsNullOrWhiteSpace(p.Revision) ? "(non renseigné)" : p.Revision),
                ("Établi par", string.IsNullOrWhiteSpace(p.Author) ? ProductInfo.Author : p.Author),
                ("Vérifié par", p.CheckedBy),
                ("Statut", p.Status),
                ("Validité de l'offre", string.IsNullOrWhiteSpace(p.ValidityDays) ? null : p.ValidityDays + " jours"),
                ("Délai d'exécution", p.ExecutionTime),
                ("Conditions de paiement", p.PaymentTerms),
                ("Devise", ctx.Boq.Currency.ToString()),
                ("Montant total TTC", ctx.Boq.TotalIncludingVat.ToString("N2", Fr) + " " + ctx.Boq.Currency + (ctx.Boq.IsComplete ? string.Empty : " (INCOMPLET : minorant)")),
                ("Maquette", ctx.DocumentTitle),
                ("Portée du métré", ctx.ScopeDescription),
                ("Généré le", ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr) + " — " + ProductInfo.Name + " " + ProductInfo.ApplicationVersion +
                              " (moteur " + ProductInfo.QuantityEngineVersion + ")"),
                ("Empreinte du métré", DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint) +
                                       " (SHA-256 des quantités : deux analyses du même modèle donnent la même empreinte)")
            };
            foreach ((string label, string value) in rows)
            {
                // Les champs facultatifs vides ne sont pas imprimes (pas de lignes « — » inutiles).
                if (string.IsNullOrWhiteSpace(value) && label != "Maître d'ouvrage" && label != "Lieu" && label != "Numéro de projet")
                {
                    continue;
                }
                ws.Cell(r, 2).Value = label.ToUpper(Fr);
                ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted)
                    .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
                ws.Cell(r, 3).Value = value ?? string.Empty;
                ws.Cell(r, 3).Style.Font.SetBold().Font.SetFontColor(label == "Montant total TTC" ? t.Accent : t.Text).Alignment.SetWrapText();
                ws.Range(r, 2, r, 3).Style.Fill.SetBackgroundColor(t.Panel);
                ws.Row(r).Height = (value ?? string.Empty).Length > 75 ? 30 : 18;
                r++;
            }
            r += 1;
            if (!string.IsNullOrWhiteSpace(p.Notes))
            {
                ws.Range(r, 2, r + 1, 3).Merge().Value = p.Notes;
                ws.Range(r, 2, r + 1, 3).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontColor(t.Text);
                r += 3;
            }
            ws.Range(r, 2, r + 2, 3).Merge().Value =
                "Les quantités sont issues de la maquette numérique ; chaque ligne est traçable jusqu'aux éléments " +
                "(feuille « Détail du métré »). Les quantités estimées (ratios, hypothèses) sont signalées et justifiées " +
                "(feuilles « Audit » et « Hypothèses »). Document à vérifier par l'ingénieur ou le métreur responsable avant signature.";
            ws.Range(r, 2, r + 2, 3).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top).Font.SetFontColor(t.Muted).Font.SetFontSize(8);
            r += 3;
            ws.PageSetup.PrintAreas.Add(1, 1, r + 1, 4);
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        // ---------------------------------------------------------------- 2. Tableau de bord
        private static void DashboardSheet(IXLWorksheet ws, ExportContext ctx, BoqSheetMap map)
        {
            BoqDashboard d = ctx.Dashboard;
            SheetTheme t = ThemeFor(ctx);
            string cur = d.Currency;
            ws.ShowGridLines = false;
            int[] widths = { 3, 16, 16, 16, 16, 16, 16, 16, 16, 3 };
            for (int i = 0; i < widths.Length; i++) ws.Column(i + 1).Width = widths[i];

            ws.Range(1, 2, 1, 9).Merge().Value = "TABLEAU DE BORD DE PILOTAGE";
            ws.Cell(1, 2).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(t.Accent);
            ws.Row(1).Height = 28;
            ws.Range(2, 2, 2, 7).Merge().Value = ctx.ProjectTitle + " — indice " + (string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "—" : ctx.Project.Revision) +
                                                  " — " + ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr);
            ws.Cell(2, 2).Style.Font.SetFontColor(t.Muted);
            ws.Range(1, 8, 1, 9).Merge();
            ws.Cell(2, 8).Value = "Note de santé";
            ws.Cell(2, 8).Style.Font.SetFontColor(t.Muted);
            ws.Cell(2, 9).Value = d.HealthScore;
            ws.Cell(2, 9).Style.NumberFormat.Format = "0\" / 100\"";
            ws.Cell(2, 9).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(d.HealthScore >= 85 ? t.Ok : d.HealthScore >= 60 ? t.Warn : t.Critical);

            // Tuiles : les totaux sont des FORMULES vers la feuille BOQ (toujours egaux au devis).
            int r = 4;
            Tile(ws, t, r, 2, "TOTAL TRAVAUX HT", "'BOQ'!" + map.WorksTotal, MoneyFormat + "\" " + cur + "\"", null);
            Tile(ws, t, r, 4, "TOTAL HT", "'BOQ'!" + map.TotalExcludingVat, MoneyFormat + "\" " + cur + "\"", null);
            Tile(ws, t, r, 6, "TVA " + d.VatPercent.ToString("0.##", Fr) + " %", "'BOQ'!" + map.Vat, MoneyFormat + "\" " + cur + "\"", null);
            Tile(ws, t, r, 8, "TOTAL TTC", "'BOQ'!" + map.TotalIncludingVat, MoneyFormat + "\" " + cur + "\"", t.Accent);
            r += 3;
            TileValue(ws, t, r, 2, "PRIX RENSEIGNÉS", d.PriceCoverage, "0.0%", d.PriceCoverage >= 1m ? t.Ok : t.Critical, d.PricedLineCount + " / " + d.LineCount + " lignes");
            if (d.ReliabilityIndex.HasValue)
                TileValue(ws, t, r, 4, "FIABILITÉ", d.ReliabilityIndex.Value, "0.0%", d.ReliabilityIndex >= 0.8m ? t.Ok : t.Warn, "part mesurée du montant");
            else
                TileText(ws, t, r, 4, "FIABILITÉ", "—", t.Muted, "aucun montant");
            if (d.CostPerSquareMeter.HasValue && d.ReferenceArea.HasValue)
            {
                Tile(ws, t, r, 6, "COÛT TTC AU M²", "'BOQ'!" + map.TotalIncludingVat + "/" + d.ReferenceArea.Value.ToString(CultureInfo.InvariantCulture),
                    MoneyFormat + "\" " + cur + "/m²\"", null, d.ReferenceArea.Value.ToString("N2", Fr) + " m² " + (d.ReferenceAreaLabel ?? string.Empty));
            }
            else
            {
                TileText(ws, t, r, 6, "COÛT TTC AU M²", "—", t.Muted, "surface de référence à renseigner");
            }
            TileText(ws, t, r, 8, "AUDIT", d.AuditErrors + " erreur(s) · " + d.AuditWarnings + " avert.", d.AuditErrors > 0 ? t.Critical : d.AuditWarnings > 0 ? t.Warn : t.Ok,
                d.ElementCount + " éléments · " + d.ExcludedCount + " exclus");
            r += 4;

            // Repartition par lot : montants en formules, barres de donnees.
            Heading(ws, t, r, "RÉPARTITION PAR LOT");
            r++;
            HeaderRow(ws, t, r, "Lot", "Désignation", null, null, "Montant HT", "Part", "Lignes", "Sans prix");
            r++;
            int first = r;
            foreach (LotShare lot in d.Lots)
            {
                BodyRow(ws, t, r);
                ws.Cell(r, 2).Value = lot.Code;
                ws.Range(r, 3, r, 5).Merge().Value = lot.Designation;
                ws.Cell(r, 6).FormulaA1 = "'BOQ'!" + map.LotSubtotals[lot.Code];
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 7).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,F" + r + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(r, 7).Style.NumberFormat.Format = "0.0%";
                ws.Cell(r, 8).Value = lot.Lines;
                ws.Cell(r, 9).Value = lot.LinesWithoutPrice;
                if (lot.LinesWithoutPrice > 0) ws.Cell(r, 9).Style.Font.SetFontColor(t.Critical).Font.SetBold();
                r++;
            }
            if (r > first)
            {
                ws.Range(first, 6, r - 1, 6).AddConditionalFormat().DataBar(t.Accent).LowestValue().HighestValue();
            }
            r += 1;

            Heading(ws, t, r, "VOYANTS");
            r++;
            foreach (DashboardSignal s in d.Signals)
            {
                BodyRow(ws, t, r);
                ws.Cell(r, 2).Value = s.Level.ToString().ToUpperInvariant();
                ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(s.Level == SignalLevel.Critique ? t.Critical : s.Level == SignalLevel.Attention ? t.Warn : s.Level == SignalLevel.Info ? t.Accent : t.Ok);
                ws.Range(r, 3, r, 5).Merge().Value = s.Title + (string.IsNullOrWhiteSpace(s.Detail) ? string.Empty : " — " + s.Detail);
                ws.Range(r, 6, r, 9).Merge().Value = s.Action ?? string.Empty;
                ws.Range(r, 3, r, 9).Style.Alignment.SetWrapText();
                ws.Row(r).Height = 28;
                r++;
            }
            r += 1;

            Heading(ws, t, r, "PARETO — LES ARTICLES QUI FONT LE DEVIS");
            r++;
            HeaderRow(ws, t, r, "Rang", "Code", "Désignation", null, null, "Montant HT", "Part", "Part cumulée");
            r++;
            int paretoFirst = r;
            foreach (ParetoLine l in d.Pareto)
            {
                BodyRow(ws, t, r);
                ws.Cell(r, 2).Value = l.Rank;
                ws.Cell(r, 3).Value = l.Code;
                ws.Range(r, 4, r, 6).Merge().Value = l.Designation + (l.Reliability == Reliability.Estimated ? " (E)" : string.Empty);
                ws.Cell(r, 7).FormulaA1 = map.LineAmounts.TryGetValue(l.Code, out string a) ? "'BOQ'!" + a : l.Amount.ToString(CultureInfo.InvariantCulture);
                ws.Cell(r, 7).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 8).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,G" + r + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(r, 8).Style.NumberFormat.Format = "0.0%";
                ws.Cell(r, 9).FormulaA1 = "SUM(H$" + paretoFirst + ":H" + r + ")";
                ws.Cell(r, 9).Style.NumberFormat.Format = "0.0%";
                r++;
            }
            if (d.Pareto.Count == 0)
            {
                BodyRow(ws, t, r);
                ws.Range(r, 2, r, 9).Merge().Value = "Aucun montant : saisir les prix unitaires (Paramètres du projet > Prix unitaires).";
                r++;
            }
            r += 1;

            Heading(ws, t, r, "QUANTITÉS CLÉS (NETTES, EN PLACE)");
            r++;
            HeaderRow(ws, t, r, "Ouvrage", null, "Quantité", "Unité", "Part mesurée");
            r++;
            foreach (KeyQuantity k in d.KeyQuantities)
            {
                BodyRow(ws, t, r);
                ws.Range(r, 2, r, 3).Merge().Value = k.Label;
                ws.Cell(r, 4).Value = k.Value;
                ws.Cell(r, 4).Style.NumberFormat.Format = k.Unit == BoqUnit.Kilogram ? "#,##0" : "#,##0.00";
                ws.Cell(r, 5).Value = k.Unit.Symbol();
                ws.Cell(r, 6).Value = k.MeasuredShare;
                ws.Cell(r, 6).Style.NumberFormat.Format = "0%";
                r++;
            }
            if (d.RebarRatio.HasValue)
            {
                BodyRow(ws, t, r);
                ws.Range(r, 2, r, 3).Merge().Value = "Ratio armatures / béton";
                ws.Cell(r, 4).Value = d.RebarRatio.Value;
                ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0";
                ws.Cell(r, 5).Value = "kg/m³";
                r++;
            }
            r += 1;

            if (d.MissingPrices.Count > 0)
            {
                Heading(ws, t, r, "PRIX À SAISIR (" + d.MissingPrices.Count + ")");
                r++;
                HeaderRow(ws, t, r, "Code", "Désignation", null, null, null, "Quantité", "Unité");
                r++;
                foreach (MissingPrice m in d.MissingPrices)
                {
                    BodyRow(ws, t, r);
                    ws.Cell(r, 2).Value = m.Code;
                    ws.Cell(r, 2).Style.Font.SetFontColor(t.Critical);
                    ws.Range(r, 3, r, 6).Merge().Value = m.Designation;
                    ws.Cell(r, 7).Value = m.Quantity;
                    ws.Cell(r, 7).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(r, 8).Value = m.Unit.Symbol();
                    r++;
                }
                r += 1;
            }
            ws.Range(r, 2, r + 1, 9).Merge().Value =
                "Indicateurs calculés uniquement à partir du BOQ, de l'audit et du métré de cette analyse ; les totaux sont des formules vers la feuille BOQ. " +
                "La note de santé (prix 50, fiabilité 30, audit 20) aide au pilotage : elle ne remplace pas la vérification du métreur.";
            ws.Range(r, 2, r + 1, 9).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            r += 2;
            if (t.Dark)
            {
                // Fond nuit sur toute la zone imprimee ; les cellules deja colorees (tuiles, lignes) gardent leur couleur.
                foreach (IXLCell c in ws.Range(1, 1, r + 1, 10).Cells())
                {
                    if (c.Style.Fill.BackgroundColor == XLColor.NoColor || c.Style.Fill.PatternType == XLFillPatternValues.None)
                    {
                        c.Style.Fill.SetBackgroundColor(t.Page);
                    }
                }
            }
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        private static void Heading(IXLWorksheet ws, SheetTheme t, int r, string text)
        {
            ws.Range(r, 2, r, 9).Merge().Value = text;
            ws.Range(r, 2, r, 9).Style.Font.SetBold().Font.SetFontColor(t.Accent).Border.SetBottomBorder(XLBorderStyleValues.Thin)
                .Border.SetBottomBorderColor(t.Accent);
            ws.Row(r).Height = 20;
        }

        private static void HeaderRow(IXLWorksheet ws, SheetTheme t, int r, params string[] labels)
        {
            for (int i = 0; i < labels.Length; i++)
            {
                if (labels[i] != null) ws.Cell(r, i + 2).Value = labels[i];
            }
            ws.Range(r, 2, r, 9).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted).Font.SetBold().Fill.SetBackgroundColor(t.Panel);
        }

        private static void BodyRow(IXLWorksheet ws, SheetTheme t, int r)
        {
            ws.Range(r, 2, r, 9).Style.Fill.SetBackgroundColor(t.Panel).Border.SetBottomBorder(XLBorderStyleValues.Hair)
                .Border.SetBottomBorderColor(t.Dark ? XLColor.FromHtml("#1E3A5F") : XLColor.FromHtml("#C9D6E3"))
                .Font.SetFontColor(t.Text);
        }

        private static void TileFrame(IXLWorksheet ws, SheetTheme t, int r, int c, string label, XLColor color)
        {
            IXLRange tile = ws.Range(r, c, r + 2, c + 1);
            tile.Style.Fill.SetBackgroundColor(t.Panel).Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(color ?? t.Accent);
            ws.Range(r, c, r, c + 1).Merge().Value = label;
            ws.Cell(r, c).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r + 1, c, r + 1, c + 1).Merge();
            ws.Cell(r + 1, c).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(color ?? t.Text);
            ws.Range(r + 2, c, r + 2, c + 1).Merge();
            ws.Cell(r + 2, c).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Row(r + 1).Height = 24;
        }

        private static void Tile(IXLWorksheet ws, SheetTheme t, int r, int c, string label, string formula, string format, XLColor color, string sub = null)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).FormulaA1 = formula;
            ws.Cell(r + 1, c).Style.NumberFormat.Format = format;
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }

        private static void TileValue(IXLWorksheet ws, SheetTheme t, int r, int c, string label, decimal value, string format, XLColor color, string sub)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).Value = value;
            ws.Cell(r + 1, c).Style.NumberFormat.Format = format;
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }

        private static void TileText(IXLWorksheet ws, SheetTheme t, int r, int c, string label, string value, XLColor color, string sub)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).Value = value;
            ws.Cell(r + 1, c).Style.Font.SetFontSize(11);
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }
    }
}
