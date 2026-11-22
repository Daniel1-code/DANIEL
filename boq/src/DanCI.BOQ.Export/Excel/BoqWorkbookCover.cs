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
        // Meme modele que le PDF : marque, titre, sous-titre, image, cartes d'identite, montant TTC.
        private static void Cover(IXLWorksheet ws, ExportContext ctx)
        {
            SheetTheme t = ThemeFor(ctx);
            ws.ShowGridLines = false;
            ws.Column(1).Width = 3;
            for (int c = 2; c <= 7; c++) ws.Column(c).Width = 17;
            ws.Column(8).Width = 3;
            if (t.Dark)
            {
                ws.Range(1, 1, 120, 8).Style.Fill.SetBackgroundColor(t.Page);
            }
            ProjectInfo p = ctx.Project ?? new ProjectInfo();
            int r = 2;

            string imported = ctx.Cover?.ImportedPage;
            if (!string.IsNullOrWhiteSpace(imported) && File.Exists(imported))
            {
                // Page de garde realisee a part : en tete, sur la largeur d'une page A4.
                var picture = ws.AddPicture(imported).MoveTo(ws.Cell(r, 2));
                double scale = Math.Min(1.0, 690.0 / Math.Max(1, picture.OriginalWidth));
                picture.Scale(scale);
                r += (int)Math.Ceiling(picture.OriginalHeight * scale / 20.0) + 2;
                ws.Row(r - 1).AddHorizontalPageBreak();
            }

            string logo = ctx.EffectiveLogo;
            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
            {
                ws.AddPicture(logo).MoveTo(ws.Cell(r, 2)).WithSize(150, 70);
                r += 4;
            }
            ws.Cell(r, 2).Value = "DanCI";
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(t.Accent);
            ws.Range(r, 6, r, 7).Merge().Value = string.IsNullOrWhiteSpace(p.QuoteReference) ? "Réf. —" : p.QuoteReference;
            ws.Cell(r, 6).Style.Font.SetFontColor(t.Muted).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(r).Height = 28;
            r++;
            ws.Range(r, 2, r, 5).Merge().Value = "DAN CONSULT INTELLIGENTE  ·  BUREAU D'ÉTUDES  ·  KINSHASA";
            ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r, 6, r, 7).Merge().Value = "INDICE " + (string.IsNullOrWhiteSpace(p.Revision) ? "—" : p.Revision);
            ws.Cell(r, 6).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(t.Accent2).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            r += 4;

            ws.Range(r, 2, r, 7).Merge().Value = "BORDEREAU DES QUANTITÉS  &  DEVIS ESTIMATIF";
            ws.Cell(r, 2).Style.Font.SetFontSize(11).Font.SetFontColor(t.Muted)
                .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
            r++;
            ws.Range(r, 2, r, 7).Merge().Value = (ctx.ProjectTitle ?? string.Empty).ToUpper(Fr);
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(26).Font.SetFontColor(t.Text)
                .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent)
                .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(r).Height = (ctx.ProjectTitle ?? string.Empty).Length > 34 ? 70 : 38;
            r++;
            string subtitle = string.IsNullOrWhiteSpace(ctx.Cover?.Subtitle) ? p.Purpose : ctx.Cover.Subtitle;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                ws.Range(r, 2, r, 7).Merge().Value = subtitle;
                ws.Cell(r, 2).Style.Font.SetFontSize(13).Font.SetFontColor(t.Accent)
                    .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
                ws.Row(r).Height = 20;
                r++;
            }
            r += 2;

            string image = ctx.Cover?.ProjectImage;
            if (!string.IsNullOrWhiteSpace(image) && File.Exists(image))
            {
                var pic = ws.AddPicture(image).MoveTo(ws.Cell(r, 2), 2, 2);
                double scale = Math.Min(1.0, Math.Min(680.0 / Math.Max(1, pic.OriginalWidth), 300.0 / Math.Max(1, pic.OriginalHeight)));
                pic.Scale(scale);
                int imageRows = (int)Math.Ceiling(pic.OriginalHeight * scale / 20.0) + 1;
                ws.Range(r, 2, r + imageRows - 1, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(t.Accent);
                r += imageRows + 1;
            }

            // Cartes d'identite : 3 par ligne, comme dans le PDF.
            void Card(int row, int col, string label, string value)
            {
                IXLRange l = ws.Range(row, col, row, col + 1).Merge();
                l.Value = label;
                l.Style.Font.SetFontSize(7).Font.SetFontColor(t.Muted).Fill.SetBackgroundColor(t.Panel);
                IXLRange v = ws.Range(row + 1, col, row + 1, col + 1).Merge();
                v.Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
                v.Style.Font.SetBold().Font.SetFontColor(t.Text).Fill.SetBackgroundColor(t.Panel).Alignment.SetWrapText()
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(row, col, row + 1, col).Style.Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
                ws.Row(row + 1).Height = Math.Max(ws.Row(row + 1).Height, (value ?? string.Empty).Length > 30 ? 30 : 18);
            }
            Card(r, 2, "MAÎTRE D'OUVRAGE", p.Client);
            Card(r, 4, "LIEU", p.Location);
            Card(r, 6, "DATE", string.IsNullOrWhiteSpace(p.IssueDate) ? ctx.GeneratedAt.ToString("dd/MM/yyyy", Fr) : p.IssueDate);
            r += 3;
            Card(r, 2, "MAÎTRE D'ŒUVRE", p.Architect);
            Card(r, 4, "BUREAU D'ÉTUDES", string.IsNullOrWhiteSpace(p.Engineer) ? ProductInfo.Author : p.Engineer);
            Card(r, 6, "N° PROJET", p.Number);
            r += 3;

            // Montant total TTC.
            IXLRange box = ws.Range(r, 2, r + 2, 7);
            box.Style.Fill.SetBackgroundColor(t.Panel).Border.SetOutsideBorder(XLBorderStyleValues.Medium).Border.SetOutsideBorderColor(t.Accent);
            ws.Range(r, 2, r, 4).Merge().Value = "MONTANT TOTAL TTC";
            ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r + 1, 2, r + 1, 4).Merge().Value = ctx.Boq.TotalIncludingVat.ToString("N2", Fr) + " " + ctx.Boq.Currency;
            ws.Cell(r + 1, 2).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(t.Accent);
            ws.Row(r + 1).Height = 30;
            if (ctx.Boq.TotalIncludingVatSecondary.HasValue && ctx.Boq.SecondaryCurrency.HasValue)
            {
                ws.Range(r + 2, 2, r + 2, 4).Merge().Value = "soit " + ctx.Boq.TotalIncludingVatSecondary.Value.ToString("N2", Fr) + " " + ctx.Boq.SecondaryCurrency;
                ws.Cell(r + 2, 2).Style.Font.SetFontSize(9).Font.SetFontColor(t.Muted);
            }
            ws.Range(r, 5, r, 7).Merge().Value = "HT " + ctx.Boq.TotalExcludingVat.ToString("N2", Fr) + " " + ctx.Boq.Currency;
            ws.Range(r + 1, 5, r + 1, 7).Merge().Value = "TVA " + ctx.Boq.VatPercent.ToString("0.##", Fr) + " % : " + ctx.Boq.Vat.ToString("N2", Fr);
            ws.Range(r, 5, r + 1, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right).Font.SetFontColor(t.Text);
            if (!ctx.Boq.IsComplete)
            {
                ws.Range(r + 2, 5, r + 2, 7).Merge().Value = "TOTAL INCOMPLET (minorant)";
                ws.Cell(r + 2, 5).Style.Font.SetBold().Font.SetFontColor(t.Critical).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
            r += 4;

            // Informations complementaires (champs renseignes seulement).
            var rows = new List<(string, string)>
            {
                ("Objet", p.Purpose),
                ("Entreprise", p.Contractor),
                ("Établi par", string.IsNullOrWhiteSpace(p.Author) ? ProductInfo.Author : p.Author),
                ("Vérifié par", p.CheckedBy),
                ("Statut", p.Status),
                ("Validité de l'offre", string.IsNullOrWhiteSpace(p.ValidityDays) ? null : p.ValidityDays + " jours"),
                ("Délai d'exécution", p.ExecutionTime),
                ("Conditions de paiement", p.PaymentTerms),
                ("Maquette", ctx.DocumentTitle),
                ("Portée du métré", ctx.ScopeDescription),
                ("Généré le", ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr) + " — " + ProductInfo.Name + " " + ProductInfo.ApplicationVersion +
                              " (moteur " + ProductInfo.QuantityEngineVersion + ")"),
                ("Empreinte du métré", DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint) +
                                       " (SHA-256 des quantités : deux analyses du même modèle donnent la même empreinte)")
            };
            foreach ((string label, string value) in rows.Where(x => !string.IsNullOrWhiteSpace(x.Item2)))
            {
                ws.Range(r, 2, r, 3).Merge().Value = label.ToUpper(Fr);
                ws.Cell(r, 2).Style.Font.SetFontSize(7).Font.SetFontColor(t.Muted).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(r, 4, r, 7).Merge().Value = value;
                ws.Cell(r, 4).Style.Font.SetFontColor(t.Text).Font.SetFontSize(9).Alignment.SetWrapText();
                ws.Row(r).Height = value.Length > 70 ? 26 : 14;
                r++;
            }
            r++;
            if (!string.IsNullOrWhiteSpace(p.Notes))
            {
                ws.Range(r, 2, r + 1, 7).Merge().Value = p.Notes;
                ws.Range(r, 2, r + 1, 7).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontColor(t.Text);
                r += 3;
            }
            ws.Range(r, 2, r + 2, 7).Merge().Value =
                "Les quantités sont issues de la maquette numérique ; chaque ligne est traçable jusqu'aux éléments " +
                "(feuille « Détail du métré »). Les quantités estimées (ratios, hypothèses) sont signalées et justifiées " +
                "(feuilles « Audit » et « Hypothèses »). Document à vérifier par l'ingénieur ou le métreur responsable avant signature.";
            ws.Range(r, 2, r + 2, 7).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top).Font.SetFontColor(t.Muted).Font.SetFontSize(8);
            r += 3;
            ws.PageSetup.PrintAreas.Add(1, 1, r + 1, 8);
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        // ---------------------------------------------------------------- 2. Tableau de bord
        // Meme modele que la fenetre et le PDF : 7 tuiles, lots | voyants, Pareto | quantites cles.
        private const int DashFirstCol = 2;   // B
        private const int DashLastCol = 15;   // O
        private const int DashMidCol = 9;     // I : fin du panneau gauche

        private static void DashboardSheet(IXLWorksheet ws, ExportContext ctx, BoqSheetMap map)
        {
            BoqDashboard d = ctx.Dashboard;
            SheetTheme t = ThemeFor(ctx);
            string cur = d.Currency;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            for (int c = DashFirstCol; c <= DashLastCol; c++) ws.Column(c).Width = 11;
            ws.Column(DashLastCol + 1).Width = 2;
            if (t.Dark)
            {
                ws.Range(1, 1, 200, DashLastCol + 1).Style.Fill.SetBackgroundColor(t.Page);
            }

            ws.Range(1, 2, 1, 11).Merge().Value = "TABLEAU DE BORD DE PILOTAGE";
            ws.Cell(1, 2).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(t.Accent);
            ws.Row(1).Height = 30;
            ws.Range(2, 2, 2, 11).Merge().Value = ctx.ProjectTitle + " — indice " + (string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "—" : ctx.Project.Revision) +
                                                   " — " + ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr);
            ws.Cell(2, 2).Style.Font.SetFontColor(t.Muted);
            ws.Range(1, 13, 1, 15).Merge().Value = "Note de santé";
            ws.Cell(1, 13).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Bottom);
            ws.Range(2, 13, 2, 15).Merge().Value = d.HealthScore;
            ws.Cell(2, 13).Style.NumberFormat.Format = "0\" / 100\"";
            ws.Cell(2, 13).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(d.HealthScore >= 85 ? t.Ok : d.HealthScore >= 60 ? t.Warn : t.Critical)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(2).Height = 28;

            // 7 tuiles de 2 colonnes (B a O), comme la fenetre. Totaux = FORMULES vers la feuille BOQ.
            int r = 4;
            string money = MoneyFormat + "\" " + cur + "\"";
            Tile(ws, t, r, 2, "TOTAL TRAVAUX HT", "'BOQ'!" + map.WorksTotal, money, null, "majorations " + d.MarkupsTotal.ToString("N2", Fr));
            Tile(ws, t, r, 4, "TOTAL TTC", "'BOQ'!" + map.TotalIncludingVat, money, d.IsComplete ? t.Accent : t.Warn,
                d.TotalIncludingVatSecondary.HasValue ? "soit " + d.TotalIncludingVatSecondary.Value.ToString("N2", Fr) + " " + d.SecondaryCurrency
                    : "TVA " + d.VatPercent.ToString("0.##", Fr) + " % : " + d.Vat.ToString("N2", Fr));
            if (d.CostPerSquareMeter.HasValue && d.ReferenceArea.HasValue)
            {
                Tile(ws, t, r, 6, "COÛT TTC AU M²", "'BOQ'!" + map.TotalIncludingVat + "/" + d.ReferenceArea.Value.ToString(CultureInfo.InvariantCulture),
                    MoneyFormat, null, d.ReferenceArea.Value.ToString("N2", Fr) + " m² " + (d.ReferenceAreaLabel ?? string.Empty));
            }
            else
            {
                TileText(ws, t, r, 6, "COÛT TTC AU M²", "—", t.Muted, "surface de référence à saisir");
            }
            TileValue(ws, t, r, 8, "PRIX RENSEIGNÉS", d.PriceCoverage, "0.0%", d.PriceCoverage >= 1m ? t.Ok : t.Critical, d.PricedLineCount + " / " + d.LineCount + " lignes");
            if (d.ReliabilityIndex.HasValue)
                TileValue(ws, t, r, 10, "FIABILITÉ", d.ReliabilityIndex.Value, "0.0%", d.ReliabilityIndex >= 0.8m ? t.Ok : t.Warn, "part mesurée du montant");
            else
                TileText(ws, t, r, 10, "FIABILITÉ", "—", t.Muted, "aucun montant");
            TileText(ws, t, r, 12, "AUDIT", d.AuditErrors + " err. · " + d.AuditWarnings + " av.", d.AuditErrors > 0 ? t.Critical : d.AuditWarnings > 0 ? t.Warn : t.Ok,
                d.ElementCount + " éléments · " + d.ExcludedCount + " exclus");
            TileText(ws, t, r, 14, "RATIO ACIER", d.RebarRatio.HasValue ? d.RebarRatio.Value.ToString("N0", Fr) + " kg/m³" : "—", null, "armatures / béton (nets)");
            r += 4;

            // Panneaux cote a cote : Repartition par lot | Voyants.
            int left = r, right = r;
            PanelTitle(ws, t, left, DashFirstCol, DashMidCol, "RÉPARTITION PAR LOT");
            left++;
            decimal max = d.Lots.Count == 0 ? 0m : d.Lots.Max(l => l.Share);
            int lotFirst = left;
            foreach (LotShare lot in d.Lots)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Cell(left, 2).Value = lot.Code;
                ws.Cell(left, 2).Style.Font.SetFontColor(t.Muted);
                ws.Range(left, 3, left, 5).Merge().Value = lot.Designation + (lot.IsComplete ? string.Empty : "  (" + lot.LinesWithoutPrice + " sans prix)");
                // Barre : caracteres pleins proportionnels a la part (formule vivante sur la part).
                ws.Range(left, 6, left, 7).Merge();
                ws.Cell(left, 6).FormulaA1 = "REPT(\"█\",ROUND(IF(MAX(I$" + lotFirst + ":I$" + (lotFirst + d.Lots.Count - 1) + ")=0,0,I" + left +
                                                              "/MAX(I$" + lotFirst + ":I$" + (lotFirst + d.Lots.Count - 1) + "))*14,0))";
                ws.Cell(left, 6).Style.Font.SetFontColor(lot.IsComplete ? t.Accent : t.Warn).Font.SetFontSize(8);
                ws.Cell(left, 8).FormulaA1 = "'BOQ'!" + map.LotSubtotals[lot.Code];
                ws.Cell(left, 8).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(left, 9).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,H" + left + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(left, 9).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).Style.Font.SetFontColor(t.Accent);
                left++;
            }

            PanelTitle(ws, t, right, DashMidCol + 2, DashLastCol, "VOYANTS");
            right++;
            foreach (DashboardSignal s in d.Signals)
            {
                XLColor color = s.Level == SignalLevel.Critique ? t.Critical : s.Level == SignalLevel.Attention ? t.Warn : s.Level == SignalLevel.Info ? t.Accent : t.Ok;
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Cell(right, DashMidCol + 2).Value = "●";
                ws.Cell(right, DashMidCol + 2).Style.Font.SetFontColor(color).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                IXLRange text = ws.Range(right, DashMidCol + 3, right, DashLastCol).Merge();
                text.Value = s.Title;
                text.Style.Font.SetBold().Font.SetFontColor(t.Text);
                right++;
                string more = string.Join("  —  ", new[] { s.Detail, s.Action }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (more.Length > 0)
                {
                    PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                    IXLRange m = ws.Range(right, DashMidCol + 3, right, DashLastCol).Merge();
                    m.Value = more;
                    m.Style.Font.SetFontSize(8).Font.SetFontColor(color).Alignment.SetWrapText();
                    ws.Row(right).Height = more.Length > 75 ? 24 : 13;
                    right++;
                }
            }
            r = Math.Max(left, right) + 1;

            // Panneaux cote a cote : Pareto | Quantites cles.
            left = r;
            right = r;
            PanelTitle(ws, t, left, DashFirstCol, DashMidCol, "PARETO — LES ARTICLES QUI FONT LE DEVIS");
            left++;
            int paretoFirst = left;
            foreach (ParetoLine l in d.Pareto)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Cell(left, 2).Value = l.Rank;
                ws.Cell(left, 2).Style.Font.SetBold().Font.SetFontColor(t.Accent2).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                ws.Cell(left, 3).Value = l.Code;
                ws.Cell(left, 3).Style.Font.SetFontColor(t.Muted);
                ws.Range(left, 4, left, 6).Merge().Value = l.Designation + (l.Reliability == Reliability.Estimated ? " (E)" : string.Empty);
                ws.Cell(left, 7).FormulaA1 = map.LineAmounts.TryGetValue(l.Code, out string a) ? "'BOQ'!" + a : l.Amount.ToString(CultureInfo.InvariantCulture);
                ws.Cell(left, 7).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(left, 8).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,G" + left + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(left, 8).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).FormulaA1 = "SUM(H$" + paretoFirst + ":H" + left + ")";
                ws.Cell(left, 9).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).Style.Font.SetFontColor(t.Accent2);
                left++;
            }
            if (d.Pareto.Count == 0)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Range(left, 2, left, DashMidCol).Merge().Value = "Aucun montant pour l'instant : saisir les prix unitaires (Paramètres du projet > Prix unitaires).";
                ws.Cell(left, 2).Style.Font.SetFontColor(t.Warn);
                left++;
            }
            else
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Range(left, 2, left, DashMidCol).Merge().Value = "Colonnes : montant HT, part, part cumulée du total des travaux.";
                ws.Cell(left, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
                left++;
            }

            PanelTitle(ws, t, right, DashMidCol + 2, DashLastCol, "QUANTITÉS CLÉS (NETTES)");
            right++;
            foreach (KeyQuantity k in d.KeyQuantities)
            {
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Range(right, DashMidCol + 2, right, DashMidCol + 3).Merge().Value = k.Label;
                ws.Range(right, DashMidCol + 4, right, DashMidCol + 5).Merge().Value = k.Value;
                ws.Cell(right, DashMidCol + 4).Style.NumberFormat.Format = (k.Unit == BoqUnit.Kilogram ? "#,##0" : "#,##0.00") + "\" " + k.Unit.Symbol() + "\"";
                ws.Cell(right, DashMidCol + 4).Style.Font.SetBold();
                ws.Cell(right, DashMidCol + 6).Value = k.MeasuredShare;
                ws.Cell(right, DashMidCol + 6).Style.NumberFormat.Format = "0.0%\" mesuré\"";
                ws.Cell(right, DashMidCol + 6).Style.Font.SetFontSize(8).Font.SetFontColor(k.MeasuredShare >= 1m ? t.Ok : t.Warn);
                right++;
            }
            if (d.RebarRatio.HasValue)
            {
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Range(right, DashMidCol + 2, right, DashMidCol + 3).Merge().Value = "Ratio armatures / béton";
                ws.Range(right, DashMidCol + 4, right, DashMidCol + 5).Merge().Value = d.RebarRatio.Value;
                ws.Cell(right, DashMidCol + 4).Style.NumberFormat.Format = "#,##0\" kg/m³\"";
                right++;
            }
            r = Math.Max(left, right) + 1;

            if (d.MissingPrices.Count > 0)
            {
                PanelTitle(ws, t, r, DashFirstCol, DashLastCol, "PRIX À SAISIR (" + d.MissingPrices.Count + ")");
                ws.Cell(r, DashFirstCol).Style.Font.SetFontColor(t.Critical);
                r++;
                foreach (MissingPrice m in d.MissingPrices)
                {
                    PanelRow(ws, t, r, DashFirstCol, DashLastCol);
                    ws.Cell(r, 2).Value = m.Code;
                    ws.Cell(r, 2).Style.Font.SetFontColor(t.Critical);
                    ws.Range(r, 3, r, 11).Merge().Value = m.Designation;
                    ws.Range(r, 12, r, 13).Merge().Value = m.Quantity;
                    ws.Cell(r, 12).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(r, 14).Value = m.Unit.Symbol();
                    r++;
                }
                r++;
            }
            ws.Range(r, 2, r + 1, DashLastCol).Merge().Value =
                "Indicateurs calculés uniquement à partir du BOQ, de l'audit et du métré de cette analyse ; les totaux sont des formules vers la feuille BOQ. " +
                "La note de santé (prix 50, fiabilité 30, audit 20) aide au pilotage : elle ne remplace pas la vérification du métreur.";
            ws.Range(r, 2, r + 1, DashLastCol).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.PageSetup.PrintAreas.Add(1, 1, r + 2, DashLastCol + 1);
            SetupPrint(ws, ctx, XLPageOrientation.Landscape);
        }

        private static void PanelTitle(IXLWorksheet ws, SheetTheme t, int r, int c1, int c2, string text)
        {
            IXLRange title = ws.Range(r, c1, r, c2);
            title.Style.Fill.SetBackgroundColor(t.Panel).Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(t.Dark ? XLColor.FromHtml("#1E3A5F") : t.Accent);
            ws.Range(r, c1, r, c2).Merge().Value = text;
            ws.Cell(r, c1).Style.Font.SetBold().Font.SetFontColor(t.Accent);
            ws.Row(r).Height = Math.Max(ws.Row(r).Height, 20);
        }

        private static void PanelRow(IXLWorksheet ws, SheetTheme t, int r, int c1, int c2)
        {
            ws.Range(r, c1, r, c2).Style.Fill.SetBackgroundColor(t.Panel).Font.SetFontColor(t.Text);
        }

        private static void TileFrame(IXLWorksheet ws, SheetTheme t, int r, int c, string label, XLColor color)
        {
            IXLRange tile = ws.Range(r, c, r + 2, c + 1);
            tile.Style.Fill.SetBackgroundColor(t.Panel).Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(color ?? t.Accent);
            ws.Range(r, c, r, c + 1).Merge().Value = label;
            ws.Cell(r, c).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r + 1, c, r + 1, c + 1).Merge();
            ws.Cell(r + 1, c).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(color ?? t.Text).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
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
