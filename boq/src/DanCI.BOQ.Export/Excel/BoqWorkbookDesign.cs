using System;
using System.Globalization;
using ClosedXML.Excel;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>Feuille « Conception » : images extraites du modele Revit (vue 3D, plans par niveau).</summary>
    public static partial class BoqWorkbookWriter
    {
        public const string DesignSheetName = "Conception";

        /// <summary>Largeur et hauteur maximales d'une image dans la feuille (pixels).</summary>
        private const double DesignMaxWidth = 1000, DesignMaxHeight = 640;

        private static void DesignSheet(XLWorkbook wb, ExportContext ctx)
        {
            var images = ctx.ExistingDesignImages;
            if (images.Count == 0) return;
            SheetTheme t = ThemeFor(ctx);
            IXLWorksheet ws = wb.AddWorksheet(DesignSheetName);
            ws.ShowGridLines = false;
            ws.Column(1).Width = 3;
            for (int c = 2; c <= 13; c++) ws.Column(c).Width = 12;
            int lastRow = 4 + images.Count * 40;
            if (t.Dark) ws.Range(1, 1, lastRow, 14).Style.Fill.SetBackgroundColor(t.Page);

            int r = 2;
            ws.Cell(r, 2).Value = "CONCEPTION — VUES DU MODÈLE";
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(t.Accent);
            r++;
            ws.Cell(r, 2).Value = ctx.ProjectTitle + " — images extraites du modèle Revit le " +
                                  ctx.GeneratedAt.ToString("dd/MM/yyyy à HH:mm", CultureInfo.GetCultureInfo("fr-FR")) +
                                  " (illustration de la conception, sans échelle ; les quantités proviennent du métré).";
            ws.Cell(r, 2).Style.Font.SetItalic().Font.SetFontColor(t.Muted);
            r += 2;

            foreach (DesignImage img in images)
            {
                ws.Cell(r, 2).Value = img.Title;
                ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(t.Dark ? t.Text : t.Accent);
                if (!string.IsNullOrWhiteSpace(img.Caption))
                {
                    ws.Cell(r, 8).Value = img.Caption;
                    ws.Cell(r, 8).Style.Font.SetFontColor(t.Muted);
                }
                ws.Range(r, 2, r, 13).Style.Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(t.Accent);
                r++;
                var pic = ws.AddPicture(img.Path).MoveTo(ws.Cell(r, 2), 4, 4);
                double scale = Math.Min(1.0, Math.Min(DesignMaxWidth / Math.Max(1, pic.OriginalWidth), DesignMaxHeight / Math.Max(1, pic.OriginalHeight)));
                pic.Scale(scale);
                int rows = (int)Math.Ceiling(pic.OriginalHeight * scale / 20.0) + 1;
                // Fond blanc sous l'image (rendu Revit), cadre a la couleur du theme.
                var frame = ws.Range(r, 2, r + rows - 1, 13);
                frame.Style.Fill.SetBackgroundColor(XLColor.White);
                frame.Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(t.Accent);
                r += rows + 2;
            }
            if (t.Dark && r > lastRow) ws.Range(lastRow, 1, r + 2, 14).Style.Fill.SetBackgroundColor(t.Page);
            ws.PageSetup.PageOrientation = XLPageOrientation.Landscape;
            ws.PageSetup.PaperSize = XLPaperSize.A4Paper;
            ws.PageSetup.FitToPages(1, 0);
        }
    }
}
