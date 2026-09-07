using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la dalle telle qu'elle sera modelisee : la coupe longitudinale au-dessus,
    /// avec les nappes et l'etendue reelle des chapeaux, et sous elle le diagramme de
    /// l'elancement, qui montre d'un coup d'oeil si c'est la fleche qui gouverne.
    /// </summary>
    public static class SlabPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Brush BarBrush = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush GaugeOk = new SolidColorBrush(Color.FromRgb(46, 125, 74));
        private static readonly Brush GaugeFail = new SolidColorBrush(Color.FromRgb(196, 46, 34));
        private static readonly Brush GaugeTrack = new SolidColorBrush(Color.FromRgb(226, 226, 222));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen MainPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 2.0);
        private static readonly Pen TransversePen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 0.9);
        private static readonly Pen SupportPen =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 1.2);
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(SlabDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Slab != null)
                {
                    DrawSection(dc, result, new Rect(0, 0, width, height * 0.52));
                    DrawGauges(dc, result, new Rect(0, height * 0.52, width, height * 0.48));
                }
                else
                {
                    DrawText(dc, "Aucune dalle selectionnee", 12, width / 2.0, height / 2.0, true);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------------
        // Coupe longitudinale
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, SlabDesignResult result, Rect area)
        {
            SlabData slab = result.Slab;
            SlabReinforcement r = result.Reinforcement;
            if (slab.SpanMm <= 0) return;

            double margin = area.Width * 0.08;
            double scale = (area.Width - 2 * margin) / slab.SpanMm;
            double left = area.Left + margin;
            // L'epaisseur est exageree : a l'echelle de la portee, une dalle serait un trait.
            double drawnThickness = Math.Min(area.Height * 0.42, slab.ThicknessMm * scale * 8.0);
            double top = area.Top + area.Height * 0.32;
            double bottom = top + drawnThickness;
            double vertical = drawnThickness / slab.ThicknessMm;

            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(new Point(left, top), new Size(slab.SpanMm * scale, drawnThickness)));

            // Appuis.
            double supportSize = Math.Max(6.0, drawnThickness * 0.35);
            if (slab.SpanKind == SlabSpanKind.Cantilever)
            {
                dc.DrawLine(SupportPen, new Point(left, top - 4), new Point(left, bottom + 4));
            }
            else
            {
                foreach (double x in new[] { left, left + slab.SpanMm * scale })
                {
                    dc.DrawLine(SupportPen, new Point(x - supportSize / 2.0, bottom + supportSize),
                                new Point(x + supportSize / 2.0, bottom + supportSize));
                    dc.DrawLine(SupportPen, new Point(x, bottom), new Point(x, bottom + supportSize));
                }
            }

            // Nappe inferieure porteuse : un trait sur toute la portee.
            double zMain = bottom - (r.CoverMm + r.BottomMain.DiameterMm / 2.0) * vertical;
            dc.DrawLine(MainPen, new Point(left + 3, zMain),
                        new Point(left + slab.SpanMm * scale - 3, zMain));

            // Repartition : vue en bout, ponctuelle, a son espacement reel.
            double zTransverse = zMain - (r.BottomTransverse.DiameterMm) * vertical;
            if (r.BottomTransverse.SpacingMm > 0)
            {
                for (double x = r.BottomTransverse.SpacingMm / 2.0; x < slab.SpanMm;
                     x += r.BottomTransverse.SpacingMm)
                {
                    double px = left + x * scale;
                    dc.DrawEllipse(BarBrush, null, new Point(px, zTransverse), 1.6, 1.6);
                }
            }

            // Chapeaux : dessines sur leur etendue reelle, pas sur toute la travee.
            if (r.HasTopReinforcement)
            {
                double zTop = top + (r.CoverMm + r.TopMain.DiameterMm / 2.0) * vertical;
                if (slab.SpanKind == SlabSpanKind.Cantilever)
                {
                    dc.DrawLine(MainPen, new Point(left + 3, zTop),
                                new Point(left + slab.SpanMm * scale - 3, zTop));
                }
                else
                {
                    double reach = Math.Min(r.TopBarLengthMm, slab.SpanMm / 2.0) * scale;
                    dc.DrawLine(MainPen, new Point(left + 3, zTop), new Point(left + reach, zTop));
                    dc.DrawLine(MainPen,
                        new Point(left + slab.SpanMm * scale - reach, zTop),
                        new Point(left + slab.SpanMm * scale - 3, zTop));
                    DrawText(dc, string.Format("{0:0} mm", r.TopBarLengthMm), 9.5,
                             left + reach / 2.0, zTop - 10, true);
                }
            }

            DrawText(dc, string.Format("Coupe - h = {0:0} mm, portee {1:0} mm ({2})",
                         slab.ThicknessMm, slab.SpanMm, slab.SpanKind),
                     11.5, area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc, r.BottomLabel, 10.5, area.Left + area.Width / 2.0,
                     bottom + supportSize + 18, true);
            DrawText(dc, "epaisseur representee a une echelle dilatee", 9.0,
                     area.Left + area.Width / 2.0, area.Top + area.Height - 8, true);
        }

        // ------------------------------------------------------------------
        // Taux de travail des criteres qui gouvernent une dalle
        // ------------------------------------------------------------------

        private static void DrawGauges(DrawingContext dc, SlabDesignResult result, Rect area)
        {
            DrawText(dc, "Ce qui gouverne", 11.5, area.Left + area.Width / 2.0,
                     area.Top + 12, true);

            double y = area.Top + 34;
            double labelWidth = area.Width * 0.34;
            double barLeft = area.Left + labelWidth + 12;
            double barWidth = area.Width - labelWidth - area.Width * 0.10 - 12;

            foreach (CheckResult check in result.Checks)
            {
                if (check.Status == CheckStatus.NotApplicable) continue;
                if (y > area.Bottom - 18) break;

                DrawText(dc, Shorten(check.Description), 10.0, area.Left + 8, y + 5, false);

                dc.DrawRectangle(GaugeTrack, null,
                    new Rect(new Point(barLeft, y), new Size(barWidth, 10)));

                double filled = Math.Min(check.Utilization, 1.35) / 1.35 * barWidth;
                if (filled > 0)
                {
                    dc.DrawRectangle(check.Utilization <= 1.0 ? GaugeOk : GaugeFail, null,
                        new Rect(new Point(barLeft, y), new Size(filled, 10)));
                }

                // Repere du taux 1,00.
                double markX = barLeft + barWidth / 1.35;
                dc.DrawLine(SupportPen, new Point(markX, y - 2), new Point(markX, y + 12));

                DrawText(dc, string.Format("{0:0.00}", check.Utilization), 9.5,
                         barLeft + barWidth + 8, y + 5, false);
                y += 20;
            }
        }

        private static string Shorten(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= 34 ? text : text.Substring(0, 33) + "...";
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x, double y,
                                     bool centred)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, TextBrush, 1.0);
            double leftEdge = centred ? x - formatted.Width / 2.0 : x;
            dc.DrawText(formatted, new Point(leftEdge, y - formatted.Height / 2.0));
        }
    }
}
