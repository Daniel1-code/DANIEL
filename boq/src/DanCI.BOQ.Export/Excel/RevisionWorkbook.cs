using System;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Revisions;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>
    /// Comparaison de deux revisions du devis : synthese, ecarts par lot (avec graphique) et
    /// ecarts par article (tableau filtrable : ajoutees, supprimees, modifiees ; cause : quantite
    /// ou prix).
    /// </summary>
    public static class RevisionWorkbook
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private static readonly XLColor Blue = XLColor.FromHtml("#1F4E79");
        private static readonly XLColor Up = XLColor.FromHtml("#C00000");
        private static readonly XLColor Down = XLColor.FromHtml("#2E7D32");

        public static void Write(string path, RevisionComparison c)
        {
            var charts = new System.Collections.Generic.List<ChartSpec>();
            using (var wb = new XLWorkbook())
            {
                Summary(wb.AddWorksheet("Synthèse"), c);
                Lots(wb.AddWorksheet("Par lot"), c, charts);
                Lines(wb.AddWorksheet("Par article"), c);
                wb.SaveAs(path);
            }
            ExcelCharts.Inject(path, charts);
        }

        private static void Summary(IXLWorksheet ws, RevisionComparison c)
        {
            ws.Column(1).Width = 34;
            ws.Column(2).Width = 22;
            ws.Column(3).Width = 22;
            ws.Column(4).Width = 18;
            ws.Column(5).Width = 12;
            ws.Cell(1, 1).Value = "COMPARAISON DES RÉVISIONS DU DEVIS";
            ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(Blue);
            ws.Cell(2, 1).Value = c.B.Project ?? c.A.Project;
            ws.Range(4, 1, 4, 5).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);
            string[] h = { "", "Révision A (référence)", "Révision B (nouvelle)", "Écart B − A", "Écart %" };
            for (int i = 0; i < h.Length; i++) ws.Cell(4, i + 1).Value = h[i];
            int r = 5;
            void Text(string label, string a, string b)
            {
                ws.Cell(r, 1).Value = label;
                ws.Cell(r, 2).Value = a;
                ws.Cell(r, 3).Value = b;
                r++;
            }
            void Money(string label, decimal a, decimal b)
            {
                ws.Cell(r, 1).Value = label;
                ws.Cell(r, 2).Value = a;
                ws.Cell(r, 3).Value = b;
                ws.Cell(r, 4).FormulaA1 = "C" + r + "-B" + r;
                ws.Cell(r, 5).FormulaA1 = "IF(B" + r + "=0,\"\",D" + r + "/B" + r + ")";
                ws.Range(r, 2, r, 4).Style.NumberFormat.Format = BoqWorkbookWriter.MoneyFormat;
                ws.Cell(r, 5).Style.NumberFormat.Format = "+0.0%;-0.0%;0.0%";
                ws.Cell(r, 4).Style.Font.SetFontColor(b > a ? Up : b < a ? Down : XLColor.Black);
                r++;
            }
            Text("Indice", c.A.Revision ?? "—", c.B.Revision ?? "—");
            Text("Date d'export", c.A.Date.ToString("dd/MM/yyyy HH:mm", Fr), c.B.Date.ToString("dd/MM/yyyy HH:mm", Fr));
            Text("Devise", c.A.Currency, c.B.Currency);
            Text("Empreinte du métré", c.A.Fingerprint, c.B.Fingerprint);
            Text("Devis complet (tous les prix)", c.A.IsComplete ? "oui" : "NON", c.B.IsComplete ? "oui" : "NON");
            Money("Total des travaux HT", c.A.WorksTotal, c.B.WorksTotal);
            Money("Total HT", c.A.TotalExcludingVat, c.B.TotalExcludingVat);
            Money("TVA", c.A.Vat, c.B.Vat);
            Money("TOTAL TTC", c.A.TotalIncludingVat, c.B.TotalIncludingVat);
            ws.Range(r - 1, 1, r - 1, 5).Style.Font.SetBold();
            r++;
            ws.Range(r, 1, r + 2, 5).Merge().Value = c.Summary();
            ws.Range(r, 1, r + 2, 5).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top).Font.SetBold();
            r += 4;
            ws.Cell(r, 1).Value = "Lignes";
            ws.Cell(r, 1).Style.Font.SetBold();
            foreach (ChangeKind k in new[] { ChangeKind.Modified, ChangeKind.Added, ChangeKind.Removed, ChangeKind.Unchanged })
            {
                r++;
                ws.Cell(r, 1).Value = Label(k);
                ws.Cell(r, 2).Value = c.Lines.Count(l => l.Kind == k);
            }
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
        }

        private static void Lots(IXLWorksheet ws, RevisionComparison c, System.Collections.Generic.List<ChartSpec> charts)
        {
            string[] h = { "Lot", "Désignation", "Montant A", "Montant B", "Écart B − A", "Écart %" };
            int[] w = { 8, 46, 16, 16, 16, 10 };
            for (int i = 0; i < h.Length; i++)
            {
                ws.Cell(1, i + 1).Value = h[i];
                ws.Column(i + 1).Width = w[i];
            }
            ws.Range(1, 1, 1, h.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);
            int r = 2;
            foreach (LotChange l in c.Lots)
            {
                ws.Cell(r, 1).Value = l.Code;
                ws.Cell(r, 2).Value = l.Designation;
                ws.Cell(r, 3).Value = l.TotalA;
                ws.Cell(r, 4).Value = l.TotalB;
                ws.Cell(r, 5).FormulaA1 = "D" + r + "-C" + r;
                ws.Cell(r, 6).FormulaA1 = "IF(C" + r + "=0,\"\",E" + r + "/C" + r + ")";
                ws.Range(r, 3, r, 5).Style.NumberFormat.Format = BoqWorkbookWriter.MoneyFormat;
                ws.Cell(r, 6).Style.NumberFormat.Format = "+0.0%;-0.0%;0.0%";
                ws.Cell(r, 5).Style.Font.SetFontColor(l.Delta > 0 ? Up : l.Delta < 0 ? Down : XLColor.Black);
                r++;
            }
            if (r > 2)
            {
                ws.Range(1, 1, r - 1, h.Length).SetAutoFilter();
                var chart = new ChartSpec
                {
                    SheetName = ws.Name, Kind = ChartKind.Column, Title = "Écart de montant par lot (B − A)", SeriesName = "Écart",
                    CategoryRef = "'" + ws.Name + "'!$A$2:$A$" + (r - 1), ValueRef = "'" + ws.Name + "'!$E$2:$E$" + (r - 1),
                    FromColumn = 7, FromRow = 0, ToColumn = 15, ToRow = 18, Dark = false, SeriesColor = "#1F4E79"
                };
                foreach (LotChange l in c.Lots)
                {
                    chart.Categories.Add(l.Code);
                    chart.Values.Add((double)l.Delta);
                    chart.PointColors.Add(l.Delta > 0 ? "#C00000" : "#2E7D32");
                }
                charts.Add(chart);
            }
        }

        private static void Lines(IXLWorksheet ws, RevisionComparison c)
        {
            string[] h = { "Code", "Lot", "Désignation", "Unité", "Quantité A", "Quantité B", "PU A", "PU B", "Montant A", "Montant B", "Écart", "Changement", "Cause" };
            int[] w = { 10, 6, 46, 7, 12, 12, 11, 11, 14, 14, 14, 16, 18 };
            for (int i = 0; i < h.Length; i++)
            {
                ws.Cell(1, i + 1).Value = h[i];
                ws.Column(i + 1).Width = w[i];
            }
            ws.Range(1, 1, 1, h.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);
            int r = 2;
            // Les plus gros ecarts d'abord.
            foreach (LineChange l in c.Lines.OrderByDescending(x => Math.Abs(x.Delta)).ThenBy(x => x.Code, StringComparer.Ordinal))
            {
                ws.Cell(r, 1).Value = l.Code;
                ws.Cell(r, 2).Value = l.Lot;
                ws.Cell(r, 3).Value = l.Designation;
                ws.Cell(r, 4).Value = l.Unit;
                if (l.QuantityA.HasValue) ws.Cell(r, 5).Value = l.QuantityA.Value;
                if (l.QuantityB.HasValue) ws.Cell(r, 6).Value = l.QuantityB.Value;
                if (l.UnitPriceA.HasValue) ws.Cell(r, 7).Value = l.UnitPriceA.Value;
                if (l.UnitPriceB.HasValue) ws.Cell(r, 8).Value = l.UnitPriceB.Value;
                ws.Cell(r, 9).Value = l.AmountA;
                ws.Cell(r, 10).Value = l.AmountB;
                ws.Cell(r, 11).FormulaA1 = "J" + r + "-I" + r;
                ws.Cell(r, 12).Value = Label(l.Kind);
                ws.Cell(r, 13).Value = l.Cause;
                ws.Range(r, 5, r, 6).Style.NumberFormat.Format = "#,##0.00";
                ws.Range(r, 7, r, 11).Style.NumberFormat.Format = BoqWorkbookWriter.MoneyFormat;
                ws.Cell(r, 11).Style.Font.SetFontColor(l.Delta > 0 ? Up : l.Delta < 0 ? Down : XLColor.Black);
                if (l.QuantityA != l.QuantityB) ws.Range(r, 5, r, 6).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF2CC"));
                if (l.UnitPriceA != l.UnitPriceB) ws.Range(r, 7, r, 8).Style.Fill.SetBackgroundColor(XLColor.FromHtml("#FFF2CC"));
                ws.Cell(r, 12).Style.Font.SetFontColor(l.Kind == ChangeKind.Added ? Down : l.Kind == ChangeKind.Removed ? Up :
                    l.Kind == ChangeKind.Modified ? XLColor.FromHtml("#B7791F") : XLColor.Gray);
                r++;
            }
            if (r > 2)
            {
                ws.Range(1, 1, r - 1, h.Length).SetAutoFilter();
            }
            ws.SheetView.FreezeRows(1);
        }

        private static string Label(ChangeKind k)
        {
            switch (k)
            {
                case ChangeKind.Added: return "Ajoutée";
                case ChangeKind.Removed: return "Supprimée";
                case ChangeKind.Modified: return "Modifiée";
                default: return "Inchangée";
            }
        }
    }
}
