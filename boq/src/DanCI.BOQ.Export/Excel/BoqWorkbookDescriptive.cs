using System;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Planning;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>Feuilles « BOQ descriptif » et « Temps unitaires ».</summary>
    public static partial class BoqWorkbookWriter
    {
        public const string DescriptiveSheetName = "BOQ descriptif";
        public const string UnitTimesSheetName = "Temps unitaires";

        /// <summary>
        /// BOQ descriptif : chaque article avec sa prescription (catalogue, a defaut texte type signale), sa
        /// composition reelle tiree du modele (types et nombres, materiaux, niveaux), son mode de metre.
        /// </summary>
        private static void DescriptiveSheet(IXLWorksheet ws, ExportContext ctx)
        {
            var rows = DescriptiveBoq.Build(ctx.Boq, ctx.Configuration?.Mapping);
            string cur = ctx.Boq.Currency.ToString();
            ws.Column(1).Width = 11;
            ws.Column(2).Width = 95;
            ws.Column(3).Width = 7;
            ws.Column(4).Width = 13;
            ws.Column(5).Width = 14;
            ws.Column(6).Width = 16;
            ws.Range(1, 1, 1, 6).Merge().Value = "BORDEREAU DES QUANTITÉS DESCRIPTIF — " + ctx.ProjectTitle;
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
            ws.Range(2, 1, 2, 6).Merge().Value =
                "Pour chaque article : prescriptions (catalogue ; à défaut texte TYPE signalé, à préciser dans le CCTP), composition réelle tirée du modèle Revit " +
                "(types et nombres, matériaux, niveaux), mode de métré et fiabilité. Montants hors taxes en " + cur + ".";
            ws.Range(2, 1, 2, 6).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray).Alignment.SetWrapText();
            ws.Row(2).Height = 30;
            string[] head = { "N°", "Désignation et description des ouvrages", "Unité", "Quantité", "PU (" + cur + ")", "Montant (" + cur + ")" };
            for (int c = 0; c < head.Length; c++) ws.Cell(4, c + 1).Value = head[c];
            ws.Range(4, 1, 4, 6).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#0B1F3A"));
            int r = 5;
            string lot = null;
            foreach (DescriptiveRow row in rows)
            {
                if (row.LotCode != lot)
                {
                    lot = row.LotCode;
                    ws.Cell(r, 1).Value = "LOT " + row.LotCode;
                    ws.Cell(r, 2).Value = row.LotDesignation;
                    ws.Range(r, 1, r, 6).Style.Font.SetBold().Fill.SetBackgroundColor(LightBlue);
                    r++;
                }
                ws.Cell(r, 1).Value = row.Code;
                ws.Cell(r, 1).Style.Font.SetBold().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                var cell = ws.Cell(r, 2);
                var rt = cell.GetRichText();
                rt.AddText(row.Designation).SetBold().SetFontColor(Blue);
                rt.AddText(Environment.NewLine + row.Prescription).SetItalic(!row.PrescriptionFromCatalog);
                rt.AddText(Environment.NewLine + row.Composition).SetFontColor(XLColor.FromHtml("#3E4C5E"));
                rt.AddText(Environment.NewLine + "Métré : " + row.Measurement).SetFontColor(XLColor.Gray).SetFontSize(9);
                if (!string.IsNullOrEmpty(row.Derived)) rt.AddText(Environment.NewLine + "Quantités dérivées : " + row.Derived).SetFontColor(XLColor.Gray).SetFontSize(9);
                cell.Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Cell(r, 3).Value = row.Unit.Symbol();
                ws.Cell(r, 4).Value = row.Quantity;
                ws.Cell(r, 4).Style.NumberFormat.Format = "#,##0.00";
                if (row.UnitPrice.HasValue) ws.Cell(r, 5).Value = row.UnitPrice.Value;
                else ws.Cell(r, 5).Style.Fill.SetBackgroundColor(Orange);
                ws.Cell(r, 5).Style.NumberFormat.Format = MoneyFormat;
                if (row.Amount.HasValue) ws.Cell(r, 6).Value = row.Amount.Value;
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                ws.Range(r, 3, r, 6).Style.Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(r, 1, r, 6).Style.Border.SetBottomBorder(XLBorderStyleValues.Hair).Border.SetBottomBorderColor(XLColor.LightGray);
                ws.Row(r).Height = 15 * (4 + (string.IsNullOrEmpty(row.Derived) ? 0 : 1) + (row.Prescription.Length + row.Composition.Length + row.Measurement.Length) / 110);
                r++;
            }
            ws.SheetView.FreezeRows(4);
            ws.PageSetup.PageOrientation = XLPageOrientation.Portrait;
            ws.PageSetup.FitToPages(1, 0);
        }

        /// <summary>
        /// Temps unitaires : heures-homme par unite (heures/jour x effectif / rendement), heures totales,
        /// jours d'equipe, totaux par lot, taux horaire implicite si la main-d'oeuvre du PU est decomposee.
        /// </summary>
        private static void UnitTimesSheet(IXLWorksheet ws, ExportContext ctx)
        {
            var settings = ctx.Configuration?.Planning ?? new PlanningSettings();
            var rows = UnitTimes.Build(ctx.Boq, settings, ctx.Configuration?.Prices);
            string cur = ctx.Boq.Currency.ToString();
            int[] widths = { 11, 52, 7, 12, 12, 9, 12, 13, 11, 13, 13, 44 };
            for (int c = 0; c < widths.Length; c++) ws.Column(c + 1).Width = widths[c];
            ws.Range(1, 1, 1, 12).Merge().Value = "TEMPS UNITAIRES DE MAIN-D'ŒUVRE — " + ctx.ProjectTitle;
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
            ws.Range(2, 1, 2, 12).Merge().Value =
                "Temps unitaire (heures-homme par unité) = heures par jour (" + settings.HoursPerDay.ToString("0.#") + " h) × effectif de l'équipe ÷ rendement journalier de l'équipe. " +
                "Rendements et effectifs : config\\planning.json (« rendements », « effectif », « effectifs », « heuresParJour »). ESTIMATION à valider par l'entreprise. " +
                "Taux horaire implicite = main-d'œuvre du PU (décomposition de la bibliothèque) ÷ temps unitaire : contrôle de cohérence des prix.";
            ws.Range(2, 1, 2, 12).Style.Font.SetItalic().Font.SetFontColor(XLColor.Gray).Alignment.SetWrapText();
            ws.Row(2).Height = 44;
            string[] head = { "N°", "Désignation", "Unité", "Quantité", "Rendement (u/j/équipe)", "Effectif", "Temps unitaire (h.h/u)", "Heures-homme", "Jours d'équipe",
                              "Main-d'œuvre du PU (" + cur + "/u)", "Taux horaire implicite (" + cur + "/h)", "Origine du rendement" };
            for (int c = 0; c < head.Length; c++) ws.Cell(4, c + 1).Value = head[c];
            ws.Range(4, 1, 4, 12).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(XLColor.FromHtml("#0B1F3A")).Alignment.SetWrapText();
            ws.Row(4).Height = 32;
            int r = 5;
            foreach (var g in rows.GroupBy(x => (x.LotCode, x.LotDesignation)))
            {
                ws.Cell(r, 1).Value = "LOT " + g.Key.LotCode;
                ws.Cell(r, 2).Value = g.Key.LotDesignation;
                ws.Range(r, 1, r, 12).Style.Font.SetBold().Fill.SetBackgroundColor(LightBlue);
                int first = r + 1;
                r++;
                foreach (UnitTimeRow x in g)
                {
                    ws.Cell(r, 1).Value = x.Code;
                    ws.Cell(r, 2).Value = x.Designation;
                    ws.Cell(r, 3).Value = x.Unit.Symbol();
                    ws.Cell(r, 4).Value = x.Quantity;
                    ws.Cell(r, 5).Value = x.PerDay;
                    ws.Cell(r, 6).Value = x.Workers;
                    // Formules : modifiables dans le classeur (rendement, effectif) avec recalcul.
                    ws.Cell(r, 7).FormulaA1 = string.Format(System.Globalization.CultureInfo.InvariantCulture, "IF(E{0}>0,{1}*F{0}/E{0},0)", r, x.HoursPerDay);
                    ws.Cell(r, 8).FormulaA1 = "G" + r + "*D" + r;
                    ws.Cell(r, 9).FormulaA1 = "IF(E" + r + ">0,D" + r + "/E" + r + ",0)";
                    if (x.LabourPerUnit.HasValue)
                    {
                        ws.Cell(r, 10).Value = x.LabourPerUnit.Value;
                        ws.Cell(r, 11).FormulaA1 = "IF(G" + r + ">0,J" + r + "/G" + r + ",\"\")";
                    }
                    ws.Cell(r, 12).Value = x.Basis;
                    if (x.IsDefaultRate) ws.Range(r, 5, r, 5).Style.Fill.SetBackgroundColor(Orange);
                    ws.Range(r, 4, r, 4).Style.NumberFormat.Format = "#,##0.00";
                    ws.Range(r, 5, r, 5).Style.NumberFormat.Format = "#,##0.##";
                    ws.Range(r, 7, r, 9).Style.NumberFormat.Format = "#,##0.00";
                    ws.Range(r, 10, r, 11).Style.NumberFormat.Format = MoneyFormat;
                    ws.Cell(r, 12).Style.Font.SetFontColor(XLColor.Gray).Font.SetFontSize(9);
                    r++;
                }
                ws.Cell(r, 2).Value = "Total lot " + g.Key.LotCode;
                ws.Cell(r, 8).FormulaA1 = "SUM(H" + first + ":H" + (r - 1) + ")";
                ws.Cell(r, 9).FormulaA1 = "SUM(I" + first + ":I" + (r - 1) + ")";
                ws.Range(r, 1, r, 12).Style.Font.SetBold();
                ws.Range(r, 8, r, 9).Style.NumberFormat.Format = "#,##0.00";
                r++;
            }
            ws.Cell(r + 1, 2).Value = "TOTAL GÉNÉRAL (heures-homme, jours d'équipe)";
            ws.Cell(r + 1, 8).FormulaA1 = "SUMIF(B5:B" + r + ",\"Total lot*\",H5:H" + r + ")";
            ws.Cell(r + 1, 9).FormulaA1 = "SUMIF(B5:B" + r + ",\"Total lot*\",I5:I" + r + ")";
            ws.Range(r + 1, 1, r + 1, 12).Style.Font.SetBold().Fill.SetBackgroundColor(XLColor.FromHtml("#FFE7A3"));
            ws.Range(r + 1, 8, r + 1, 9).Style.NumberFormat.Format = "#,##0.00";
            ws.Cell(r + 2, 2).Value = "Soit en jours-homme (÷ " + settings.HoursPerDay.ToString("0.#") + " h)";
            ws.Cell(r + 2, 8).FormulaA1 = string.Format(System.Globalization.CultureInfo.InvariantCulture, "H{0}/{1}", r + 1, settings.HoursPerDay > 0 ? settings.HoursPerDay : 8);
            ws.Cell(r + 2, 8).Style.NumberFormat.Format = "#,##0.0";
            ws.SheetView.FreezeRows(4);
        }
    }
}
