using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Spreadsheet;

namespace DanCI.BOQ.Export.Excel
{
    public enum ChartKind
    {
        /// <summary>Colonnes verticales.</summary>
        Column,
        /// <summary>Barres horizontales (la premiere categorie en haut).</summary>
        Bar,
        Doughnut
    }

    /// <summary>
    /// Graphique Excel NATIF, relie a des plages du classeur : il se met a jour quand les
    /// cellules changent et ne montre que les lignes visibles (il suit les filtres).
    /// </summary>
    public sealed class ChartSpec
    {
        public string SheetName { get; set; }
        public ChartKind Kind { get; set; }
        public string Title { get; set; }
        public string SeriesName { get; set; }
        /// <summary>Plages au format Excel, ex. 'Tableau de bord'!$B$40:$B$45.</summary>
        public string CategoryRef { get; set; }
        public string ValueRef { get; set; }
        /// <summary>Valeurs en cache (affichees avant tout recalcul, et par les visionneuses).</summary>
        public List<string> Categories { get; } = new List<string>();
        public List<double> Values { get; } = new List<double>();
        /// <summary>Couleur de chaque point (#RRGGBB) ; vide = couleur de serie.</summary>
        public List<string> PointColors { get; } = new List<string>();
        public string SeriesColor { get; set; } = "#00E5FF";
        public string NumberFormat { get; set; } = "#,##0";
        /// <summary>Ancrage en cellules, indices a partir de 0 (colonne B = 1).</summary>
        public int FromColumn { get; set; }
        public int FromRow { get; set; }
        public int ToColumn { get; set; }
        public int ToRow { get; set; }
        public bool Dark { get; set; } = true;
    }

    /// <summary>
    /// Ajoute des graphiques natifs a un classeur deja ecrit par ClosedXML (qui ne sait pas en
    /// creer), avec le SDK Open XML : une partie dessin par feuille, une partie graphique par
    /// graphique. Le classeur reste un .xlsx standard.
    /// </summary>
    public static class ExcelCharts
    {
        private const string C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
        private const string A = "http://schemas.openxmlformats.org/drawingml/2006/main";
        private const string R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
        private const string Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";

        public static void Inject(string path, IReadOnlyList<ChartSpec> charts)
        {
            if (charts == null || charts.Count == 0)
            {
                return;
            }
            using (SpreadsheetDocument doc = SpreadsheetDocument.Open(path, true))
            {
                WorkbookPart wbPart = doc.WorkbookPart;
                foreach (IGrouping<string, ChartSpec> group in charts.GroupBy(c => c.SheetName))
                {
                    Sheet sheet = wbPart.Workbook.Sheets.Elements<Sheet>().First(s => s.Name == group.Key);
                    var wsPart = (WorksheetPart)wbPart.GetPartById(sheet.Id);
                    if (wsPart.DrawingsPart != null)
                    {
                        throw new InvalidOperationException("La feuille « " + group.Key + " » a déjà des dessins : graphiques non ajoutés.");
                    }
                    DrawingsPart drawings = wsPart.AddNewPart<DrawingsPart>();
                    var anchors = new StringBuilder();
                    int id = 2;
                    foreach (ChartSpec spec in group)
                    {
                        ChartPart chartPart = drawings.AddNewPart<ChartPart>();
                        using (Stream s = new MemoryStream(Encoding.UTF8.GetBytes(ChartXml(spec))))
                        {
                            chartPart.FeedData(s);
                        }
                        anchors.Append(Anchor(spec, drawings.GetIdOfPart(chartPart), id++));
                    }
                    string drawingXml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                                        "<xdr:wsDr xmlns:xdr=\"" + Xdr + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\" xmlns:c=\"" + C + "\">" +
                                        anchors + "</xdr:wsDr>";
                    using (Stream s = new MemoryStream(Encoding.UTF8.GetBytes(drawingXml)))
                    {
                        drawings.FeedData(s);
                    }
                    AttachDrawing(wsPart.Worksheet, wsPart.GetIdOfPart(drawings));
                    wsPart.Worksheet.Save();
                }
            }
        }

        /// <summary>L'element &lt;drawing&gt; doit preceder legacyDrawing, tableParts, extLst... (ordre du schema).</summary>
        private static void AttachDrawing(Worksheet ws, string relId)
        {
            var drawing = new Drawing { Id = relId };
            DocumentFormat.OpenXml.OpenXmlElement before = ws.ChildElements.FirstOrDefault(e =>
                e is LegacyDrawing || e is LegacyDrawingHeaderFooter || e is DrawingHeaderFooter || e is Picture || e is OleObjects ||
                e is Controls || e is WebPublishItems || e is TableParts || e is WorksheetExtensionList);
            if (before != null)
            {
                ws.InsertBefore(drawing, before);
            }
            else
            {
                ws.Append(drawing);
            }
        }

        private static string Anchor(ChartSpec s, string relId, int shapeId) =>
            "<xdr:twoCellAnchor editAs=\"oneCell\">" +
            "<xdr:from><xdr:col>" + s.FromColumn + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + s.FromRow + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:from>" +
            "<xdr:to><xdr:col>" + s.ToColumn + "</xdr:col><xdr:colOff>0</xdr:colOff><xdr:row>" + s.ToRow + "</xdr:row><xdr:rowOff>0</xdr:rowOff></xdr:to>" +
            "<xdr:graphicFrame macro=\"\"><xdr:nvGraphicFramePr><xdr:cNvPr id=\"" + shapeId + "\" name=\"" + Esc(s.Title) + "\"/><xdr:cNvGraphicFramePr/></xdr:nvGraphicFramePr>" +
            "<xdr:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/></xdr:xfrm>" +
            "<a:graphic><a:graphicData uri=\"" + C + "\"><c:chart r:id=\"" + relId + "\"/></a:graphicData></a:graphic>" +
            "</xdr:graphicFrame><xdr:clientData/></xdr:twoCellAnchor>";

        public static string ChartXml(ChartSpec s)
        {
            string text = s.Dark ? "E6F1FF" : "1B2633";
            string muted = s.Dark ? "8AA4C8" : "5B6B7D";
            string grid = s.Dark ? "1E3A5F" : "D9E1EA";
            string back = s.Dark ? "0F1A2E" : "FFFFFF";
            string titleColor = s.Dark ? "00E5FF" : "1F4E79";
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>");
            sb.Append("<c:chartSpace xmlns:c=\"" + C + "\" xmlns:a=\"" + A + "\" xmlns:r=\"" + R + "\">");
            sb.Append("<c:roundedCorners val=\"0\"/><c:chart>");
            sb.Append("<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1200\" b=\"1\">" + Fill(titleColor) + "</a:defRPr></a:pPr>");
            sb.Append("<a:r><a:rPr lang=\"fr-FR\" sz=\"1200\" b=\"1\">" + Fill(titleColor) + "</a:rPr><a:t>" + Esc(s.Title) + "</a:t></a:r></a:p></c:rich></c:tx>");
            sb.Append("<c:overlay val=\"0\"/></c:title><c:autoTitleDeleted val=\"0\"/><c:plotArea><c:layout/>");

            if (s.Kind == ChartKind.Doughnut)
            {
                sb.Append("<c:doughnutChart><c:varyColors val=\"1\"/>");
                Series(sb, s, isBar: false, muted: muted, text: text, back: back);
                sb.Append("<c:firstSliceAng val=\"0\"/><c:holeSize val=\"58\"/></c:doughnutChart>");
            }
            else
            {
                sb.Append("<c:barChart><c:barDir val=\"" + (s.Kind == ChartKind.Bar ? "bar" : "col") + "\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
                Series(sb, s, isBar: true, muted: muted, text: text, back: back);
                sb.Append("<c:gapWidth val=\"55\"/><c:axId val=\"5001\"/><c:axId val=\"5002\"/></c:barChart>");
                sb.Append("<c:catAx><c:axId val=\"5001\"/><c:scaling><c:orientation val=\"" + (s.Kind == ChartKind.Bar ? "maxMin" : "minMax") + "\"/></c:scaling>");
                sb.Append("<c:delete val=\"0\"/><c:axPos val=\"" + (s.Kind == ChartKind.Bar ? "l" : "b") + "\"/><c:numFmt formatCode=\"General\" sourceLinked=\"0\"/>");
                sb.Append("<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/>");
                sb.Append("<c:spPr><a:ln w=\"9525\">" + Fill(grid) + "</a:ln></c:spPr>" + TxPr(muted, 800));
                sb.Append("<c:crossAx val=\"5002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>");
                sb.Append("<c:valAx><c:axId val=\"5002\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/>");
                sb.Append("<c:axPos val=\"" + (s.Kind == ChartKind.Bar ? "t" : "l") + "\"/>");
                sb.Append("<c:majorGridlines><c:spPr><a:ln w=\"6350\">" + Fill(grid) + "</a:ln></c:spPr></c:majorGridlines>");
                sb.Append("<c:numFmt formatCode=\"" + Esc(s.NumberFormat) + "\" sourceLinked=\"0\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/>");
                sb.Append("<c:tickLblPos val=\"nextTo\"/><c:spPr><a:ln><a:noFill/></a:ln></c:spPr>" + TxPr(muted, 800));
                sb.Append("<c:crossAx val=\"5001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>");
            }
            sb.Append("<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr></c:plotArea>");
            if (s.Kind == ChartKind.Doughnut)
            {
                sb.Append("<c:legend><c:legendPos val=\"r\"/><c:overlay val=\"0\"/>" + TxPr(text, 800) + "</c:legend>");
            }
            sb.Append("<c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>");
            sb.Append("<c:spPr>" + Fill(back) + "<a:ln w=\"9525\">" + Fill(grid) + "</a:ln></c:spPr>");
            sb.Append(TxPr(text, 900));
            sb.Append("</c:chartSpace>");
            return sb.ToString();
        }

        private static void Series(StringBuilder sb, ChartSpec s, bool isBar, string muted, string text, string back)
        {
            sb.Append("<c:ser><c:idx val=\"0\"/><c:order val=\"0\"/><c:tx><c:v>" + Esc(s.SeriesName ?? s.Title) + "</c:v></c:tx>");
            sb.Append("<c:spPr>" + Fill(Hex(s.SeriesColor)) + (isBar ? string.Empty : "<a:ln w=\"12700\">" + Fill(back) + "</a:ln>") + "</c:spPr>");
            if (isBar)
            {
                sb.Append("<c:invertIfNegative val=\"0\"/>");
            }
            for (int i = 0; i < s.PointColors.Count; i++)
            {
                if (string.IsNullOrEmpty(s.PointColors[i])) continue;
                sb.Append("<c:dPt><c:idx val=\"" + i + "\"/>" + (isBar ? "<c:invertIfNegative val=\"0\"/>" : string.Empty) + "<c:bubble3D val=\"0\"/>");
                sb.Append("<c:spPr>" + Fill(Hex(s.PointColors[i])) + (isBar ? string.Empty : "<a:ln w=\"12700\">" + Fill(back) + "</a:ln>") + "</c:spPr></c:dPt>");
            }
            sb.Append("<c:dLbls>");
            sb.Append("<c:numFmt formatCode=\"" + (isBar ? Esc(s.NumberFormat) : "0%") + "\" sourceLinked=\"0\"/>");
            sb.Append("<c:spPr><a:noFill/><a:ln><a:noFill/></a:ln></c:spPr>" + TxPr(isBar ? text : "FFFFFF", 750));
            if (isBar) sb.Append("<c:dLblPos val=\"outEnd\"/>");
            sb.Append("<c:showLegendKey val=\"0\"/><c:showVal val=\"" + (isBar ? 1 : 0) + "\"/><c:showCatName val=\"0\"/><c:showSerName val=\"0\"/>");
            sb.Append("<c:showPercent val=\"" + (isBar ? 0 : 1) + "\"/><c:showBubbleSize val=\"0\"/>" + (isBar ? string.Empty : "<c:showLeaderLines val=\"0\"/>") + "</c:dLbls>");
            sb.Append("<c:cat><c:strRef><c:f>" + Esc(s.CategoryRef) + "</c:f><c:strCache><c:ptCount val=\"" + s.Categories.Count + "\"/>");
            for (int i = 0; i < s.Categories.Count; i++)
            {
                sb.Append("<c:pt idx=\"" + i + "\"><c:v>" + Esc(s.Categories[i]) + "</c:v></c:pt>");
            }
            sb.Append("</c:strCache></c:strRef></c:cat>");
            sb.Append("<c:val><c:numRef><c:f>" + Esc(s.ValueRef) + "</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val=\"" + s.Values.Count + "\"/>");
            for (int i = 0; i < s.Values.Count; i++)
            {
                sb.Append("<c:pt idx=\"" + i + "\"><c:v>" + s.Values[i].ToString("R", CultureInfo.InvariantCulture) + "</c:v></c:pt>");
            }
            sb.Append("</c:numCache></c:numRef></c:val></c:ser>");
        }

        private static string TxPr(string color, int size) =>
            "<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"" + size + "\">" + Fill(color) + "</a:defRPr></a:pPr><a:endParaRPr lang=\"fr-FR\"/></a:p></c:txPr>";

        private static string Fill(string hex) => "<a:solidFill><a:srgbClr val=\"" + hex + "\"/></a:solidFill>";

        private static string Hex(string color) => (color ?? "00E5FF").TrimStart('#').ToUpperInvariant();

        private static string Esc(string s) => SecurityElement.Escape(s ?? string.Empty);
    }
}
