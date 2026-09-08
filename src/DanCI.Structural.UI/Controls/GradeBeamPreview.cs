using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la longrine : l'elevation avec ses deux nappes filantes et ses cadres a
    /// l'espacement reel, et la coupe transversale. Les deux nappes sont dessinees de la
    /// meme facon a dessein — sur une longrine, la nappe superieure n'est pas un montage,
    /// elle travaille.
    /// </summary>
    public static class GradeBeamPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush SoilFill = new SolidColorBrush(Color.FromRgb(222, 214, 198));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Brush BarBrush = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen BarPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 2.0);
        private static readonly Pen StirrupPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 1.0);
        private static readonly Pen SupportPen =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 1.4);
        private static readonly Pen TiePen =
            new Pen(new SolidColorBrush(Color.FromRgb(150, 110, 30)), 1.6);

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(GradeBeamDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Beam != null)
                {
                    DrawElevation(dc, result, new Rect(0, 0, width, height * 0.55));
                    DrawSection(dc, result, new Rect(0, height * 0.55, width, height * 0.45));
                }
                else
                {
                    DrawText(dc, "Aucune longrine selectionnee", 12, width / 2.0, height / 2.0, true);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------------
        // Elevation
        // ------------------------------------------------------------------

        private static void DrawElevation(DrawingContext dc, GradeBeamDesignResult result,
                                          Rect area)
        {
            GradeBeamData beam = result.Beam;
            GradeBeamReinforcement r = result.Reinforcement;
            if (beam.SpanMm <= 0) return;

            double margin = area.Width * 0.09;
            double scale = (area.Width - 2 * margin) / beam.SpanMm;
            double left = area.Left + margin;
            double right = left + beam.SpanMm * scale;
            double drawnHeight = Math.Min(area.Height * 0.40, beam.HeightMm * scale * 3.0);
            double top = area.Top + area.Height * 0.30;
            double bottom = top + drawnHeight;
            double vertical = drawnHeight / beam.HeightMm;

            // Le sol sous la longrine : suspendue, il y a un vide dessous.
            bool suspended = result.Notes.Exists(n => n.Contains("suspendue"));
            dc.DrawRectangle(SoilFill, null, new Rect(
                new Point(area.Left, bottom + (suspended ? drawnHeight * 0.35 : 2)),
                new Size(area.Width, Math.Max(margin * 0.35, 5))));
            if (suspended)
            {
                DrawText(dc, "vide sous longrine", 8.5, (left + right) / 2.0,
                         bottom + drawnHeight * 0.18, true);
            }

            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(new Point(left, top), new Size(beam.SpanMm * scale, drawnHeight)));

            // Les appuis, c'est-a-dire les semelles.
            foreach (double x in new[] { left, right })
            {
                double w = Math.Max(10.0, drawnHeight * 0.5);
                dc.DrawRectangle(ConcreteFill, SupportPen, new Rect(
                    new Point(x - w / 2.0, bottom), new Size(w, drawnHeight * 0.45)));
            }

            // Les deux nappes, filantes, dessinees a l'identique : la superieure travaille.
            double zBottom = bottom - (r.CoverMm + r.StirrupDiameterMm
                                       + r.BottomBars.DiameterMm / 2.0) * vertical;
            double zTop = top + (r.CoverMm + r.StirrupDiameterMm
                                 + r.TopBars.DiameterMm / 2.0) * vertical;
            dc.DrawLine(BarPen, new Point(left - 6, zBottom), new Point(right + 6, zBottom));
            dc.DrawLine(BarPen, new Point(left - 6, zTop), new Point(right + 6, zTop));

            // Cadres, a leur espacement reel.
            if (r.StirrupSpacingMm > 0)
            {
                for (double x = r.StirrupSpacingMm / 2.0; x < beam.SpanMm;
                     x += r.StirrupSpacingMm)
                {
                    double px = left + x * scale;
                    dc.DrawLine(StirrupPen, new Point(px, top + 2), new Point(px, bottom - 2));
                }
            }

            // L'effort de liaison, alterne : c'est la raison d'etre de l'element.
            if (result.TieForceKn > 0)
            {
                double y = top - 16;
                dc.DrawLine(TiePen, new Point(left, y), new Point(right, y));
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    double x = side < 0 ? left : right;
                    dc.DrawLine(TiePen, new Point(x, y),
                                new Point(x - side * 8, y - 4));
                    dc.DrawLine(TiePen, new Point(x, y),
                                new Point(x - side * 8, y + 4));
                }
                DrawText(dc, string.Format("liaison +- {0:0.0} kN", result.TieForceKn), 9.5,
                         (left + right) / 2.0, y - 10, true);
            }

            DrawText(dc, string.Format("Elevation - {0}", beam.SectionLabel), 11.5,
                     area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc, r.TransverseLabel, 10.0, area.Left + area.Width / 2.0,
                     area.Bottom - 10, true);
        }

        // ------------------------------------------------------------------
        // Coupe transversale
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, GradeBeamDesignResult result,
                                        Rect area)
        {
            GradeBeamData beam = result.Beam;
            GradeBeamReinforcement r = result.Reinforcement;

            double margin = Math.Min(area.Width, area.Height) * 0.16;
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(beam.WidthMm, 1.0),
                                    (area.Height - 2 * margin) / Math.Max(beam.HeightMm, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double bottom = area.Top + area.Height - margin;

            Func<double, double, Point> toCanvas = (yMm, zMm) =>
                new Point(centreX + yMm * scale, bottom - zMm * scale);

            dc.DrawRectangle(ConcreteFill, ConcreteOutline, new Rect(
                toCanvas(-beam.WidthMm / 2.0, beam.HeightMm),
                new Size(beam.WidthMm * scale, beam.HeightMm * scale)));

            // Le cadre.
            double halfY = beam.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
            double zLow = r.CoverMm + r.StirrupDiameterMm / 2.0;
            double zHigh = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm / 2.0;
            if (halfY > 0 && zHigh > zLow)
            {
                var rect = new Rect(toCanvas(-halfY, zHigh),
                                    new Size(2.0 * halfY * scale, (zHigh - zLow) * scale));
                double radius = Math.Min(5.0, Math.Min(rect.Width, rect.Height) / 8.0);
                dc.DrawRoundedRectangle(null,
                    new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.0),
                    rect, radius, radius);
            }

            DrawBarRow(dc, toCanvas, beam, r, r.BottomBars, true, scale);
            DrawBarRow(dc, toCanvas, beam, r, r.TopBars, false, scale);

            DrawText(dc, "Coupe transversale", 11.5, centreX, area.Top + 12, true);
            DrawText(dc, r.LongitudinalLabel, 10.0, centreX, area.Bottom - 12, true);
        }

        private static void DrawBarRow(DrawingContext dc, Func<double, double, Point> toCanvas,
                                       GradeBeamData beam, GradeBeamReinforcement r,
                                       Reinforcement.Optimization.BarSelection selection,
                                       bool atBottom, double scale)
        {
            if (selection == null || selection.Count <= 0) return;

            double halfSpan = beam.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm
                              - selection.DiameterMm / 2.0;
            if (halfSpan <= 0) return;

            int perLayer = Math.Max(selection.BarsPerLayer, 1);
            int remaining = selection.Count;
            int layer = 0;
            double radius = Math.Max(2.0, selection.DiameterMm / 2.0 * scale);

            while (remaining > 0)
            {
                int count = Math.Min(perLayer, remaining);
                double offset = layer * 2.0 * selection.DiameterMm;
                double z = atBottom
                    ? r.CoverMm + r.StirrupDiameterMm + selection.DiameterMm / 2.0 + offset
                    : beam.HeightMm - r.CoverMm - r.StirrupDiameterMm
                      - selection.DiameterMm / 2.0 - offset;

                for (int i = 0; i < count; i++)
                {
                    double y = count > 1 ? -halfSpan + 2.0 * halfSpan * i / (count - 1) : 0.0;
                    dc.DrawEllipse(BarBrush, null, toCanvas(y, z), radius, radius);
                }

                remaining -= count;
                layer++;
            }
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x,
                                     double y, bool centred)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, TextBrush, 1.0);
            double leftEdge = centred ? x - formatted.Width / 2.0 : x;
            dc.DrawText(formatted, new Point(leftEdge, y - formatted.Height / 2.0));
        }
    }
}
