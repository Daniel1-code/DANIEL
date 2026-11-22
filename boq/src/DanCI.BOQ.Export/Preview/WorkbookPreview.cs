using System;
using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Preview;
using DanCI.BOQ.Export.Excel;

namespace DanCI.BOQ.Export.Preview
{
    /// <summary>
    /// Apercu du classeur AVANT export : le classeur est construit en memoire par le MEME code
    /// que l'export (BoqWorkbookWriter), puis lu cellule par cellule, formules calculees. Rien
    /// n'est ecrit sur le disque.
    /// </summary>
    public static class WorkbookPreview
    {
        public static List<SheetPreview> Build(ExportContext ctx, int maxRowsPerSheet = 300)
        {
            var sheets = new List<SheetPreview>();
            using (var wb = new XLWorkbook())
            {
                BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Build(wb, ctx);
                foreach (IXLWorksheet ws in wb.Worksheets)
                {
                    SheetPreview sheet = Read(ws, maxRowsPerSheet);
                    sheet.ChartCount = map.Charts.Count(c => c.SheetName == ws.Name);
                    sheets.Add(sheet);
                }
            }
            return sheets;
        }

        private static SheetPreview Read(IXLWorksheet ws, int maxRows)
        {
            var sheet = new SheetPreview { Name = ws.Name, PictureCount = ws.Pictures.Count, ShowGridLines = ws.ShowGridLines };
            IXLRange used = ws.RangeUsed(XLCellsUsedOptions.All);
            if (used == null)
            {
                return sheet;
            }
            int lastRow = used.RangeAddress.LastAddress.RowNumber;
            int lastCol = Math.Min(30, used.RangeAddress.LastAddress.ColumnNumber);
            for (int c = 1; c <= lastCol; c++)
            {
                sheet.ColumnWidths.Add(ws.Column(c).Width);
            }
            var merged = ws.MergedRanges.ToList();
            int shownLast = Math.Min(lastRow, maxRows);
            sheet.HiddenRows = Math.Max(0, lastRow - shownLast);
            for (int r = 1; r <= shownLast; r++)
            {
                var row = new RowPreview { Number = r, Height = ws.Row(r).Height };
                for (int c = 1; c <= lastCol; c++)
                {
                    IXLCell cell = ws.Cell(r, c);
                    IXLRange merge = merged.FirstOrDefault(m => m.Contains(cell));
                    if (merge != null && (merge.FirstCell().Address.RowNumber != r || merge.FirstCell().Address.ColumnNumber != c))
                    {
                        continue; // cellule couverte par une fusion
                    }
                    IXLStyle st = cell.Style;
                    string text = Text(cell);
                    string back = Color(st.Fill.BackgroundColor);
                    if (string.IsNullOrEmpty(text) && back == null)
                    {
                        continue;
                    }
                    int span = merge == null ? 1 : Math.Min(lastCol, merge.RangeAddress.LastAddress.ColumnNumber) - c + 1;
                    row.Cells.Add(new CellPreview
                    {
                        Column = c,
                        ColumnSpan = Math.Max(1, span),
                        Text = text,
                        Bold = st.Font.Bold,
                        Italic = st.Font.Italic,
                        FontSize = st.Font.FontSize,
                        Foreground = Color(st.Font.FontColor),
                        Background = back,
                        AlignRight = st.Alignment.Horizontal == XLAlignmentHorizontalValues.Right ||
                                     (st.Alignment.Horizontal == XLAlignmentHorizontalValues.General && IsNumber(cell)),
                        Wrap = st.Alignment.WrapText
                    });
                }
                sheet.Rows.Add(row);
            }
            return sheet;
        }

        private static string Text(IXLCell cell)
        {
            try
            {
                return cell.GetFormattedString();
            }
            catch (Exception ex)
            {
                // Formule non calculable par ClosedXML : on le dit, Excel la calculera a l'ouverture.
                return "(formule : " + cell.FormulaA1 + " — " + ex.GetType().Name + ")";
            }
        }

        private static bool IsNumber(IXLCell cell)
        {
            try
            {
                return cell.Value.IsNumber;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static string Color(XLColor color)
        {
            if (color == null || !color.HasValue) return null;
            try
            {
                if (color.ColorType != XLColorType.Color) return null;
                System.Drawing.Color c = color.Color;
                if (c.A == 0) return null;
                return "#" + c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2");
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
