using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>
    /// Classeur BOQ professionnel (ClosedXML, sans Office) :
    ///  1. Page de garde ; 2. BOQ (formules vivantes) ; 3. Recapitulatif par lot ;
    ///  4. Detail du metre ; 5. Audit ; 6. Hypotheses ; 7. Exclus.
    /// Les montants du BOQ sont des FORMULES : =SI(PU="";"";ARRONDI(Quantite x PU;2)), sous-totaux
    /// par SOMME, pied de devis calcule. Quantite et PU sont ecrits deja arrondis, comme dans le
    /// moteur : Excel retombe au centime sur les montants du moteur.
    /// </summary>
    public static class BoqWorkbookWriter
    {
        public const string MoneyFormat = "#,##0.00";
        private static readonly XLColor Blue = XLColor.FromHtml("#1F4E79");
        private static readonly XLColor LightBlue = XLColor.FromHtml("#DDEBF7");
        private static readonly XLColor Orange = XLColor.FromHtml("#FCE4D6");
        private static readonly XLColor Red = XLColor.FromHtml("#C00000");
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Adresses cles de la feuille BOQ (pour le recapitulatif et les tests).</summary>
        public sealed class BoqSheetMap
        {
            public Dictionary<string, string> LotSubtotals { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public Dictionary<string, string> LineAmounts { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
            public string WorksTotal { get; set; }
            public List<string> Markups { get; } = new List<string>();
            public string TotalExcludingVat { get; set; }
            public string Vat { get; set; }
            public string TotalIncludingVat { get; set; }
            public string TotalSecondary { get; set; }
        }

        public static BoqSheetMap Write(ExportContext ctx, string path)
        {
            using (var wb = new XLWorkbook())
            {
                BoqSheetMap map = Build(wb, ctx);
                wb.SaveAs(path);
                return map;
            }
        }

        public static BoqSheetMap Build(XLWorkbook wb, ExportContext ctx)
        {
            if (ctx?.Boq == null) throw new ArgumentException("Aucun BOQ à exporter.", nameof(ctx));
            wb.Author = ctx.Project?.Author ?? ProductInfo.Author;
            Cover(wb.AddWorksheet("Page de garde"), ctx);
            BoqSheetMap map = Boq(wb.AddWorksheet("BOQ"), ctx);
            Recap(wb.AddWorksheet("Récapitulatif"), ctx, map);
            Detail(wb.AddWorksheet("Détail du métré"), ctx);
            Audit(wb.AddWorksheet("Audit"), ctx);
            Hypotheses(wb.AddWorksheet("Hypothèses"), ctx);
            Excluded(wb.AddWorksheet("Exclus"), ctx);
            wb.Worksheet("BOQ").SetTabActive();
            return map;
        }

        // ---------------------------------------------------------------- 1. Page de garde
        private static void Cover(IXLWorksheet ws, ExportContext ctx)
        {
            ws.Column(1).Width = 4;
            ws.Column(2).Width = 30;
            ws.Column(3).Width = 70;
            int r = 2;
            if (!string.IsNullOrEmpty(ctx.LogoPath) && File.Exists(ctx.LogoPath))
            {
                ws.AddPicture(ctx.LogoPath).MoveTo(ws.Cell(r, 2)).WithSize(180, 90);
                r += 6;
            }
            ws.Cell(r, 2).Value = "DanCI — Dan Consult Intelligente";
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Blue);
            r += 1;
            ws.Cell(r, 2).Value = "Bureau d'études — Kinshasa, RDC";
            r += 3;
            ws.Range(r, 2, r, 3).Merge().Value = "BORDEREAU DES QUANTITÉS ET DEVIS ESTIMATIF";
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(Blue);
            r += 2;

            ProjectInfo p = ctx.Project ?? new ProjectInfo();
            var rows = new List<(string, string)>
            {
                ("Projet", ctx.ProjectTitle),
                ("Numéro de projet", p.Number),
                ("Maître d'ouvrage", p.Client),
                ("Lieu", p.Location),
                ("Date", string.IsNullOrWhiteSpace(p.IssueDate) ? ctx.GeneratedAt.ToString("d MMMM yyyy", Fr) : p.IssueDate),
                ("Indice de révision", string.IsNullOrWhiteSpace(p.Revision) ? "(non renseigné)" : p.Revision),
                ("Établi par", string.IsNullOrWhiteSpace(p.Author) ? ProductInfo.Author : p.Author),
                ("Statut", p.Status),
                ("Devise", ctx.Boq.Currency.ToString()),
                ("Maquette", ctx.DocumentTitle),
                ("Portée du métré", ctx.ScopeDescription),
                ("Généré le", ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr) + " — " + ProductInfo.Name + " " + ProductInfo.ApplicationVersion +
                              " (moteur " + ProductInfo.QuantityEngineVersion + ")"),
                ("Empreinte du métré", DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint) +
                                       " (SHA-256 des quantités : deux analyses du même modèle donnent la même empreinte)")
            };
            foreach ((string label, string value) in rows)
            {
                ws.Cell(r, 2).Value = label;
                ws.Cell(r, 2).Style.Font.SetBold();
                ws.Cell(r, 3).Value = value ?? string.Empty;
                r++;
            }
            r += 2;
            ws.Range(r, 2, r + 2, 3).Merge().Value =
                "Les quantités sont issues de la maquette numérique ; chaque ligne est traçable jusqu'aux éléments " +
                "(feuille « Détail du métré »). Les quantités estimées (ratios, hypothèses) sont signalées et justifiées " +
                "(feuilles « Audit » et « Hypothèses »). Document à vérifier par l'ingénieur ou le métreur responsable avant signature.";
            ws.Range(r, 2, r + 2, 3).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
            ws.Row(r).Height = 18;
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        // ---------------------------------------------------------------- 2. BOQ
        private static BoqSheetMap Boq(IXLWorksheet ws, ExportContext ctx)
        {
            var map = new BoqSheetMap();
            BoqResult boq = ctx.Boq;
            EstimateSettings settings = ctx.Configuration?.Settings ?? new EstimateSettings();
            string cur = boq.Currency.ToString();

            ws.Column(1).Width = 11;
            ws.Column(2).Width = 62;
            ws.Column(3).Width = 7;
            ws.Column(4).Width = 13;
            ws.Column(5).Width = 13;
            ws.Column(6).Width = 16;
            ws.Column(7).Width = 16;

            ws.Range("A1:F1").Merge().Value = "BORDEREAU DES QUANTITÉS ET DEVIS ESTIMATIF";
            ws.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Blue);
            ws.Range("A2:F2").Merge().Value = ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number);
            ws.Range("A3:F3").Merge().Value = "Maître d'ouvrage : " + (ctx.Project?.Client ?? "—") + "   |   Montants en " + cur +
                                              "   |   Indice : " + (string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "—" : ctx.Project.Revision);
            if (!boq.IsComplete)
            {
                ws.Range("A4:F4").Merge().Value = "ATTENTION : " + boq.AllLines.Count(l => !l.Amount.HasValue) +
                    " ligne(s) sans prix unitaire — les totaux sont des MINORANTS du coût réel.";
                ws.Range("A4:F4").Style.Font.SetBold().Font.SetFontColor(Red);
            }

            const int header = 5;
            string[] headers = { "N°", "Désignation", "Unité", "Quantité", "PU (" + cur + ")", "Montant (" + cur + ")", "Statut (hors impression)" };
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(header, c + 1).Value = headers[c];
            }
            ws.Range(header, 1, header, 7).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                .Fill.SetBackgroundColor(Blue).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Center).Alignment.SetWrapText();
            ws.Row(header).Height = 28;

            int r = header + 1;
            foreach (BoqLotResult lot in boq.Lots)
            {
                ws.Cell(r, 1).Value = "LOT " + lot.Code;
                ws.Cell(r, 2).Value = lot.Designation;
                ws.Range(r, 1, r, 6).Style.Font.SetBold().Fill.SetBackgroundColor(LightBlue);
                r++;
                int first = r;
                foreach (BoqLine line in lot.Lines)
                {
                    ws.Cell(r, 1).Value = line.Code;
                    ws.Cell(r, 2).Value = line.Designation + (line.Reliability == Reliability.Estimated ? " (E)" : string.Empty);
                    ws.Cell(r, 2).Style.Alignment.SetWrapText();
                    ws.Cell(r, 3).Value = line.Unit.Symbol();
                    ws.Cell(r, 3).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);
                    ws.Cell(r, 4).Value = line.Quantity;
                    ws.Cell(r, 4).Style.NumberFormat.Format = QuantityFormat(settings.DecimalsFor(line.Unit));
                    if (line.UnitPrice.HasValue)
                    {
                        ws.Cell(r, 5).Value = line.UnitPrice.Value;
                    }
                    else
                    {
                        ws.Cell(r, 5).Style.Fill.SetBackgroundColor(Orange);
                    }
                    ws.Cell(r, 5).Style.NumberFormat.Format = MoneyFormat;
                    ws.Cell(r, 6).FormulaA1 = "IF(E" + r + "=\"\",\"\",ROUND(D" + r + "*E" + r + "," + settings.AmountDecimals + "))";
                    ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                    ws.Cell(r, 7).Value = line.Reliability.Label() + (line.IsUnclassified ? " — non classé" : string.Empty) +
                                          (line.UnitPrice.HasValue ? string.Empty : " — sans prix");
                    ws.Cell(r, 7).Style.Font.SetFontColor(XLColor.Gray);
                    map.LineAmounts[line.Code] = "F" + r;
                    r++;
                }
                int last = r - 1;
                ws.Cell(r, 2).Value = "Sous-total LOT " + lot.Code + (lot.IsComplete ? string.Empty : " (incomplet)");
                ws.Cell(r, 6).FormulaA1 = last >= first ? "SUM(F" + first + ":F" + last + ")" : "0";
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                ws.Range(r, 2, r, 6).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
                ws.Cell(r, 2).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
                map.LotSubtotals[lot.Code] = "F" + r;
                r += 2;
            }

            // Recapitulatif et pied de devis.
            ws.Cell(r, 1).Value = "RÉCAPITULATIF";
            ws.Range(r, 1, r, 6).Style.Font.SetBold().Fill.SetBackgroundColor(LightBlue);
            r++;
            int recapFirst = r;
            foreach (BoqLotResult lot in boq.Lots)
            {
                ws.Cell(r, 1).Value = "LOT " + lot.Code;
                ws.Cell(r, 2).Value = lot.Designation;
                ws.Cell(r, 6).FormulaA1 = map.LotSubtotals[lot.Code];
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                r++;
            }
            int recapLast = r - 1;
            ws.Cell(r, 2).Value = "TOTAL DES TRAVAUX HT";
            ws.Cell(r, 6).FormulaA1 = "SUM(F" + recapFirst + ":F" + recapLast + ")";
            Total(ws, r);
            map.WorksTotal = "F" + r;
            r++;

            var markupCells = new List<string>();
            foreach (MarkupLine mk in boq.Markups)
            {
                ws.Cell(r, 2).Value = mk.Designation + (mk.Base == MarkupBase.Cumul ? " (sur cumul)" : " (sur travaux)");
                ws.Cell(r, 5).Value = mk.Percent;
                ws.Cell(r, 5).Style.NumberFormat.Format = "0.00\" %\"";
                string baseExpr = mk.Base == MarkupBase.Travaux || markupCells.Count == 0
                    ? map.WorksTotal
                    : "(" + map.WorksTotal + "+" + string.Join("+", markupCells) + ")";
                ws.Cell(r, 6).FormulaA1 = "ROUND(" + baseExpr + "*E" + r + "/100," + settings.AmountDecimals + ")";
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                markupCells.Add("F" + r);
                map.Markups.Add("F" + r);
                r++;
            }
            ws.Cell(r, 2).Value = "TOTAL HT";
            ws.Cell(r, 6).FormulaA1 = markupCells.Count == 0 ? map.WorksTotal : map.WorksTotal + "+" + string.Join("+", markupCells);
            Total(ws, r);
            map.TotalExcludingVat = "F" + r;
            r++;
            ws.Cell(r, 2).Value = "TVA";
            ws.Cell(r, 5).Value = boq.VatPercent;
            ws.Cell(r, 5).Style.NumberFormat.Format = "0.00\" %\"";
            ws.Cell(r, 6).FormulaA1 = "ROUND(" + map.TotalExcludingVat + "*E" + r + "/100," + settings.AmountDecimals + ")";
            ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
            map.Vat = "F" + r;
            r++;
            ws.Cell(r, 2).Value = "TOTAL TTC (" + cur + ")";
            ws.Cell(r, 6).FormulaA1 = map.TotalExcludingVat + "+" + map.Vat;
            Total(ws, r);
            ws.Range(r, 2, r, 6).Style.Font.SetFontSize(12).Font.SetFontColor(Blue);
            map.TotalIncludingVat = "F" + r;
            r++;

            if (boq.SecondaryCurrency.HasValue && boq.TotalIncludingVatSecondary.HasValue && settings.ExchangeRate != null)
            {
                bool toCdf = boq.Currency == Currency.USD;
                ws.Cell(r, 2).Value = "Soit TOTAL TTC en " + boq.SecondaryCurrency + " (taux " +
                                      (settings.ExchangeRate.Date?.ToString("dd/MM/yyyy", Fr) ?? "non daté") + " : 1 USD = … CDF)";
                ws.Cell(r, 5).Value = settings.ExchangeRate.CdfPerUsd;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.####";
                ws.Cell(r, 6).FormulaA1 = "ROUND(" + map.TotalIncludingVat + (toCdf ? "*" : "/") + "E" + r + "," + settings.AmountDecimals + ")";
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                map.TotalSecondary = "F" + r;
                r++;
            }

            r++;
            ws.Range(r, 1, r, 6).Merge().Value = "(E) : quantité estimée en tout ou partie (ratio, hypothèse) — voir feuilles Audit et Hypothèses. " +
                (boq.ReliabilityIndex.HasValue
                    ? "Indice de fiabilité : " + (boq.ReliabilityIndex.Value * 100m).ToString("0.0", Fr) + " % du montant issu de quantités mesurées."
                    : "Indice de fiabilité : non calculable (aucun montant).");
            ws.Range(r, 1, r, 6).Style.Alignment.SetWrapText().Font.SetItalic();
            ws.Row(r).Height = 30;
            int lastRow = r;

            ws.SheetView.FreezeRows(header);
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
            ws.PageSetup.SetRowsToRepeatAtTop(header, header);
            ws.PageSetup.PrintAreas.Clear();
            ws.PageSetup.PrintAreas.Add("A1:F" + lastRow);
            return map;
        }

        private static void Total(IXLWorksheet ws, int r)
        {
            ws.Range(r, 2, r, 6).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);
            ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
        }

        private static string QuantityFormat(int decimals) => decimals <= 0 ? "#,##0" : "#,##0." + new string('0', decimals);

        // ---------------------------------------------------------------- 3. Recapitulatif
        private static void Recap(IXLWorksheet ws, ExportContext ctx, BoqSheetMap map)
        {
            ws.Column(1).Width = 10;
            ws.Column(2).Width = 55;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 12;
            ws.Range("A1:D1").Merge().Value = "RÉCAPITULATIF PAR LOT — " + ctx.ProjectTitle;
            ws.Cell("A1").Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
            string[] h = { "Lot", "Désignation", "Montant HT (" + ctx.Boq.Currency + ")", "Part" };
            for (int c = 0; c < h.Length; c++) ws.Cell(3, c + 1).Value = h[c];
            ws.Range(3, 1, 3, 4).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);

            int r = 4;
            int first = r;
            int totalRow = 4 + ctx.Boq.Lots.Count;
            foreach (BoqLotResult lot in ctx.Boq.Lots)
            {
                ws.Cell(r, 1).Value = lot.Code;
                ws.Cell(r, 2).Value = lot.Designation + (lot.IsComplete ? string.Empty : " (incomplet)");
                ws.Cell(r, 3).FormulaA1 = "'BOQ'!" + map.LotSubtotals[lot.Code];
                ws.Cell(r, 3).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 4).FormulaA1 = "IF($C$" + totalRow + "=0,0,C" + r + "/$C$" + totalRow + ")";
                ws.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
                r++;
            }
            int last = r - 1;
            ws.Cell(r, 2).Value = "TOTAL DES TRAVAUX HT";
            ws.Cell(r, 3).FormulaA1 = last >= first ? "SUM(C" + first + ":C" + last + ")" : "0";
            ws.Cell(r, 3).Style.NumberFormat.Format = MoneyFormat;
            ws.Range(r, 2, r, 4).Style.Font.SetBold().Border.SetTopBorder(XLBorderStyleValues.Thin);

            if (last >= first)
            {
                // Repartition : barres de donnees (ClosedXML ne sait pas creer de graphique Excel natif).
                ws.Range(first, 3, last, 3).AddConditionalFormat().DataBar(Blue).LowestValue().HighestValue();
            }
            ws.Cell(r + 2, 1).Value = "Répartition visualisée par barres de données ; le PDF du devis contient le graphique de répartition.";
            ws.Cell(r + 2, 1).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray);
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        // ---------------------------------------------------------------- 4. Detail du metre
        private static void Detail(IXLWorksheet ws, ExportContext ctx)
        {
            string[] h = { "Article", "Désignation de l'article", "ElementId", "Document", "Catégorie", "Famille", "Type", "Matériau",
                           "Niveau", "Nature", "Quantité unitaire", "Unité", "Méthode de mesure", "Statut", "Hypothèse", "Règle" };
            int[] widths = { 10, 40, 11, 14, 18, 20, 24, 22, 12, 14, 14, 7, 50, 14, 50, 16 };
            Header(ws, h, widths);
            int r = 2;
            foreach (BoqLine line in ctx.Boq.AllLines)
            {
                foreach (LineContribution c in line.Contributions)
                {
                    ElementQuantity q = c.Element;
                    ws.Cell(r, 1).Value = line.Code;
                    ws.Cell(r, 2).Value = line.Designation;
                    ws.Cell(r, 3).Value = q.ElementId;
                    ws.Cell(r, 4).Value = string.IsNullOrEmpty(q.SourceDocument) ? "(modèle hôte)" : q.SourceDocument;
                    ws.Cell(r, 5).Value = q.CategoryLabel ?? q.Category;
                    ws.Cell(r, 6).Value = q.Family ?? string.Empty;
                    ws.Cell(r, 7).Value = q.TypeName ?? string.Empty;
                    ws.Cell(r, 8).Value = q.Material ?? string.Empty;
                    ws.Cell(r, 9).Value = q.Level ?? string.Empty;
                    ws.Cell(r, 10).Value = q.Kind ?? string.Empty;
                    ws.Cell(r, 11).Value = q.Value;
                    ws.Cell(r, 11).Style.NumberFormat.Format = "#,##0.000";
                    ws.Cell(r, 12).Value = q.Unit.Symbol();
                    ws.Cell(r, 13).Value = q.MethodDescription ?? q.MethodCode ?? string.Empty;
                    ws.Cell(r, 14).Value = q.Reliability.Label();
                    ws.Cell(r, 15).Value = q.Assumption ?? string.Empty;
                    ws.Cell(r, 16).Value = c.RuleId ?? string.Empty;
                    r++;
                }
            }
            Finish(ws, r - 1, h.Length, ctx);
        }

        // ---------------------------------------------------------------- 5. Audit
        private static void Audit(IXLWorksheet ws, ExportContext ctx)
        {
            string[] h = { "Rubrique", "Gravité", "Code", "Article", "Constat", "Nb éléments", "ElementIds" };
            int[] widths = { 42, 14, 24, 10, 90, 11, 40 };
            Header(ws, h, widths);
            int r = 2;
            AuditReport audit = ctx.Audit ?? new AuditReport();
            foreach (AuditFinding f in audit.Findings)
            {
                ws.Cell(r, 1).Value = ((int)f.Section) + ". " + AuditReport.Title(f.Section);
                ws.Cell(r, 2).Value = f.Severity == IssueSeverity.Error ? "Erreur" : f.Severity == IssueSeverity.Warning ? "Avertissement" : "Information";
                if (f.Severity == IssueSeverity.Error) ws.Cell(r, 2).Style.Font.SetFontColor(Red).Font.SetBold();
                ws.Cell(r, 3).Value = f.Code;
                ws.Cell(r, 4).Value = f.ArticleCode ?? string.Empty;
                ws.Cell(r, 5).Value = f.Message;
                ws.Cell(r, 5).Style.Alignment.SetWrapText();
                ws.Cell(r, 6).Value = f.ElementIds.Count;
                ws.Cell(r, 7).Value = string.Join(" ", f.ElementIds);
                r++;
            }
            r++;
            ws.Cell(r, 1).Value = "Indice de fiabilité";
            ws.Cell(r, 1).Style.Font.SetBold();
            ws.Cell(r, 5).Value = audit.ReliabilityIndex.HasValue
                ? (audit.ReliabilityIndex.Value * 100m).ToString("0.0", Fr) + " % du montant issu de quantités mesurées"
                : "Non calculable (aucun montant). Part mesurée par nature : " +
                  string.Join(", ", audit.MeasuredShareByKind.OrderBy(k => k.Key, StringComparer.Ordinal).Select(k => k.Key + " " + (k.Value * 100m).ToString("0", Fr) + " %"));
            Finish(ws, Math.Max(1, r), h.Length, ctx);
        }

        // ---------------------------------------------------------------- 6. Hypotheses
        private static void Hypotheses(IXLWorksheet ws, ExportContext ctx)
        {
            ws.Column(1).Width = 34;
            ws.Column(2).Width = 22;
            ws.Column(3).Width = 18;
            ws.Column(4).Width = 34;
            ws.Column(5).Width = 50;
            ws.Column(6).Width = 14;
            var cfg = ctx.Configuration;
            EstimateSettings s = cfg?.Settings ?? new EstimateSettings();
            ExtractionSettings x = cfg?.Extraction ?? new ExtractionSettings();
            int r = 1;
            Title(ws, ref r, "HYPOTHÈSES DU MÉTRÉ ET DU DEVIS — pour la défense du devis");

            Section(ws, ref r, "Généralités");
            Pair(ws, ref r, "Version", ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — moteur " + ProductInfo.QuantityEngineVersion +
                                       " — schéma de configuration " + ProductInfo.ConfigurationSchemaVersion);
            Pair(ws, ref r, "Maquette / portée", ctx.DocumentTitle + " — " + ctx.ScopeDescription);
            Pair(ws, ref r, "Empreinte du métré", ctx.Fingerprint);
            Pair(ws, ref r, "Devise du devis", s.Currency.ToString());
            Pair(ws, ref r, "Quantité valorisée", s.PricedQuantity == PricedQuantity.Brute
                ? "BRUTE (nette × coefficients) : les PU sont réputés HORS pertes."
                : "NETTE : les pertes sont réputées incluses dans les PU.");
            Pair(ws, ref r, "Arrondis", "Quantités " + s.QuantityDecimals + " décimale(s)" +
                                        (s.QuantityDecimalsByUnit != null && s.QuantityDecimalsByUnit.Count > 0
                                            ? " (" + string.Join(", ", s.QuantityDecimalsByUnit.Select(k => k.Key.Symbol() + " : " + k.Value)) + ")"
                                            : string.Empty) +
                                        ", PU et montants " + s.AmountDecimals + " ; arrondi commercial ; montant = ARRONDI(quantité arrondie × PU).");
            Pair(ws, ref r, "TVA", s.VatPercent.ToString("0.##", Fr) + " %");
            foreach (Markup m in s.Markups ?? new List<Markup>())
            {
                Pair(ws, ref r, m.Designation, m.Percent.ToString("0.##", Fr) + " % sur " + (m.Base == MarkupBase.Travaux ? "le total des travaux" : "le cumul"));
            }
            Pair(ws, ref r, "Taux de change", s.ExchangeRate == null ? "Aucun" :
                "1 USD = " + s.ExchangeRate.CdfPerUsd.ToString("N4", Fr) + " CDF, au " + (s.ExchangeRate.Date?.ToString("dd/MM/yyyy", Fr) ?? "(non daté)") +
                " — " + s.ExchangeRate.Source + " (saisi manuellement)");
            if (cfg?.Prices != null)
            {
                Pair(ws, ref r, "Bibliothèque de prix", cfg.Prices.Name + " — " + cfg.Prices.City + (cfg.Prices.Client != null ? " — " + cfg.Prices.Client : string.Empty) +
                                                         " — " + cfg.Prices.Prices.Count + " prix" + (cfg.Prices.Date.HasValue ? " — " + cfg.Prices.Date.Value.ToString("dd/MM/yyyy", Fr) : string.Empty));
            }
            r++;

            Section(ws, ref r, "Coefficients de majoration des quantités (brute = nette × Π(1 + c))");
            Row(ws, ref r, true, "Code", "Valeur", "Appliqué à", "Désignation", "Source", "Statut");
            foreach (Coefficient c in cfg?.Coefficients?.Items ?? new List<Coefficient>())
            {
                Row(ws, ref r, false, c.Code, (c.Value * 100m).ToString("0.##", Fr) + " %", c.AppliesTo?.ToString(), c.Designation, c.Source,
                    c.NeedsValidation ? "À VALIDER" : "Validé");
            }
            r++;

            Section(ws, ref r, "Ratios d'armatures (éléments sans armature modélisée — quantités ESTIMÉES)");
            Row(ws, ref r, true, "Catégorie", "kg/m³", "Désignation", "Source", "", "Statut");
            foreach (RebarRatio q in x.RebarRatios ?? new List<RebarRatio>())
            {
                Row(ws, ref r, false, q.Category?.ToString(), q.KgPerM3.ToString("0.##", Fr), q.Designation, q.Source, string.Empty, q.NeedsValidation ? "À VALIDER" : "Validé");
            }
            r++;

            Section(ws, ref r, "Conventions d'extraction");
            Pair(ws, ref r, "Béton", "Volume par matériau (Revit, après jointures) ; matériau béton reconnu par actif structurel, classe, puis nom (" +
                                     x.ConcreteMaterialNames + ") ; classe Cxx/yy lue dans le nom, jamais devinée.");
            Pair(ws, ref r, "Maçonnerie", "Face de mesure : " + x.MasonryFace + " ; " + OpeningDeductionRule.ModeLabel(x) + ".");
            Pair(ws, ref r, "Coffrage", "Faces latérales et sous-faces, moins les contacts avec les voisins ; dessus de pente < " +
                                        x.Formwork.MaxUncoveredTopSlopeDeg.ToString("0", Fr) + "° non coffrés" +
                                        (x.Formwork.ExcludeFoundationBottoms ? " ; sous-faces de fondations exclues" : string.Empty) +
                                        (x.Formwork.FoundationsCastAgainstSoil ? " ; fondations coulées pleine fouille" : string.Empty) +
                                        " ; terre-plein : " + x.Formwork.SlabOnGradeTypes + ".");
            Pair(ws, ref r, "Armatures", "Modélisées : longueur totale × masse HA (HA6 à HA40) ; sinon ratio par catégorie (estimé).");
            Pair(ws, ref r, "Charpente", "Longueur de coupe × poids nominal du type, sinon table des profilés, sinon volume × " +
                                         x.Steel.DensityKgPerM3.ToString("0", Fr) + " kg/m³ (estimé). Assemblages : " +
                                         (x.Steel.ConnectionsShare * 100m).ToString("0.#", Fr) + " % de la masse (estimé).");
            r++;

            Section(ws, ref r, "Règles de mapping (évaluées par ordre croissant, la première qui correspond gagne)");
            Row(ws, ref r, true, "Ordre / Id", "Article", "Exclusion", "Critères", "Commentaire / motif", "");
            foreach (MappingRule rule in (cfg?.Mapping?.Rules ?? new List<MappingRule>()).OrderBy(q => q.Order))
            {
                Row(ws, ref r, false, rule.Order + " — " + rule.Id, rule.Article ?? string.Empty, rule.Exclude ? "Oui" : string.Empty,
                    Criteria(rule.When), rule.Exclude ? rule.Reason : rule.Comment, string.Empty);
            }
            ws.Columns(4, 5).Style.Alignment.SetWrapText();
            SetupPrint(ws, ctx, XLPageOrientation.Landscape);
        }

        private static string Criteria(RuleCriteria c)
        {
            if (c == null) return string.Empty;
            var parts = new List<string>();
            void Add(string name, PatternList p) { if (p != null && !p.IsEmpty) parts.Add(name + " = " + p); }
            Add("nature", c.Kind);
            Add("catégorie", c.Category);
            Add("famille", c.Family);
            Add("type", c.TypeName);
            Add("matériau", c.Material);
            Add("niveau", c.Level);
            foreach (KeyValuePair<string, PatternList> p in c.Parameters ?? new Dictionary<string, PatternList>())
            {
                Add(p.Key, p.Value);
            }
            return string.Join(" ET ", parts);
        }

        // ---------------------------------------------------------------- 7. Exclus
        private static void Excluded(IXLWorksheet ws, ExportContext ctx)
        {
            string[] h = { "ElementId", "Catégorie", "Type", "Nature", "Quantité", "Unité", "Motif", "Règle" };
            int[] widths = { 11, 20, 28, 14, 12, 7, 60, 16 };
            Header(ws, h, widths);
            int r = 2;
            foreach (ExcludedQuantity x in ctx.Boq.Excluded)
            {
                ws.Cell(r, 1).Value = x.Element.ElementId;
                ws.Cell(r, 2).Value = x.Element.CategoryLabel ?? x.Element.Category;
                ws.Cell(r, 3).Value = x.Element.TypeName ?? string.Empty;
                ws.Cell(r, 4).Value = x.Element.Kind ?? string.Empty;
                ws.Cell(r, 5).Value = x.Element.Value;
                ws.Cell(r, 5).Style.NumberFormat.Format = "#,##0.000";
                ws.Cell(r, 6).Value = x.Element.Unit.Symbol();
                ws.Cell(r, 7).Value = x.Reason ?? string.Empty;
                ws.Cell(r, 8).Value = x.RuleId ?? string.Empty;
                r++;
            }
            Finish(ws, Math.Max(1, r - 1), h.Length, ctx);
        }

        // ---------------------------------------------------------------- utilitaires
        private static void Header(IXLWorksheet ws, string[] headers, int[] widths)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(1, c + 1).Value = headers[c];
                ws.Column(c + 1).Width = widths[c];
            }
            ws.Range(1, 1, 1, headers.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue)
                .Alignment.SetWrapText();
            ws.SheetView.FreezeRows(1);
        }

        private static void Finish(IXLWorksheet ws, int lastRow, int columns, ExportContext ctx)
        {
            if (lastRow >= 1)
            {
                ws.Range(1, 1, lastRow, columns).SetAutoFilter();
            }
            SetupPrint(ws, ctx, XLPageOrientation.Landscape);
            ws.PageSetup.SetRowsToRepeatAtTop(1, 1);
        }

        private static void Title(IXLWorksheet ws, ref int r, string text)
        {
            ws.Cell(r, 1).Value = text;
            ws.Cell(r, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
            r += 2;
        }

        private static void Section(IXLWorksheet ws, ref int r, string text)
        {
            ws.Cell(r, 1).Value = text;
            ws.Range(r, 1, r, 6).Style.Font.SetBold().Fill.SetBackgroundColor(LightBlue);
            r++;
        }

        private static void Pair(IXLWorksheet ws, ref int r, string label, string value)
        {
            ws.Cell(r, 1).Value = label ?? string.Empty;
            ws.Cell(r, 1).Style.Font.SetBold();
            ws.Range(r, 2, r, 6).Merge().Value = value ?? string.Empty;
            ws.Range(r, 2, r, 6).Style.Alignment.SetWrapText();
            r++;
        }

        private static void Row(IXLWorksheet ws, ref int r, bool bold, params string[] cells)
        {
            for (int c = 0; c < cells.Length; c++)
            {
                ws.Cell(r, c + 1).Value = cells[c] ?? string.Empty;
            }
            if (bold)
            {
                ws.Range(r, 1, r, cells.Length).Style.Font.SetBold().Border.SetBottomBorder(XLBorderStyleValues.Thin);
            }
            r++;
        }

        private static void SetupPrint(IXLWorksheet ws, ExportContext ctx, XLPageOrientation orientation)
        {
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.PageOrientation = orientation;
            ws.PageSetup.FitToPages(1, 0);
            ws.PageSetup.Margins.SetLeft(0.5).SetRight(0.5).SetTop(0.6).SetBottom(0.6);
            ws.PageSetup.Header.Left.AddText("DanCI — " + ctx.ProjectTitle);
            ws.PageSetup.Header.Right.AddText("Indice " + (string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "—" : ctx.Project.Revision));
            ws.PageSetup.Footer.Left.AddText(ws.Name);
            ws.PageSetup.Footer.Right.AddText("Page ");
            ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.PageNumber);
            ws.PageSetup.Footer.Right.AddText(" / ");
            ws.PageSetup.Footer.Right.AddText(XLHFPredefinedText.NumberOfPages);
        }
    }
}
