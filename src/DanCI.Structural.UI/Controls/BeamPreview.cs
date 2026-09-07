using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la poutre telle qu'elle sera modelisee : la coupe en travee au-dessus, et
    /// l'elevation des zones de cadres au-dessous, avec leur espacement.
    /// </summary>
    public static class BeamPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen StirrupPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.0);
        private static readonly Pen ThinStirrupPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 1.0);
        private static readonly Pen DimensionPen =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 0.8);
        private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(BeamDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Beam != null)
                {
                    DrawSection(dc, result, new Rect(0, 0, width, height * 0.62));
                    DrawElevation(dc, result, new Rect(0, height * 0.62, width, height * 0.38));
                }
                else
                {
                    DrawText(dc, "Aucune poutre selectionnee", 12, width / 2.0, height / 2.0, true);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------------
        // Coupe en travee
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, BeamDesignResult result, Rect area)
        {
            BeamData beam = result.Beam;
            BeamReinforcement r = result.Reinforcement;

            double margin = Math.Min(area.Width, area.Height) * 0.16;
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(beam.WebWidthMm, 1.0),
                                    (area.Height - 2 * margin) / Math.Max(beam.HeightMm, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double bottomY = area.Top + area.Height - margin;

            Func<double, double, Point> toCanvas = (yMm, zMm) =>
                new Point(centreX + yMm * scale, bottomY - zMm * scale);

            // Beton
            Point topLeft = toCanvas(-beam.WebWidthMm / 2.0, beam.HeightMm);
            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(topLeft, new Size(beam.WebWidthMm * scale, beam.HeightMm * scale)));

            // Cadre
            double halfY = beam.WebWidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
            double zBottom = r.CoverMm + r.StirrupDiameterMm / 2.0;
            double zTop = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm / 2.0;
            if (halfY > 0 && zTop > zBottom)
            {
                Point corner = toCanvas(-halfY, zTop);
                var rect = new Rect(corner, new Size(2.0 * halfY * scale, (zTop - zBottom) * scale));
                double radius = Math.Min(5.0, Math.Min(rect.Width, rect.Height) / 8.0);
                dc.DrawRoundedRectangle(null, StirrupPen, rect, radius, radius);
            }

            DrawBarRow(dc, toCanvas, beam, r, r.BottomSpan, true, scale);
            DrawBarRow(dc, toCanvas, beam, r, r.TopContinuous, false, scale);

            // Cotes
            DrawText(dc, string.Format("{0:0}", beam.WebWidthMm), 10.5, centreX,
                     bottomY + 12, true);
            DrawText(dc, string.Format("{0:0}", beam.HeightMm), 10.5,
                     toCanvas(-beam.WebWidthMm / 2.0, 0).X - 8,
                     bottomY - beam.HeightMm * scale / 2.0, false, null, true);

            DrawText(dc, "Coupe en travee", 11.5, centreX, area.Top + 12, true);
        }

        private static void DrawBarRow(DrawingContext dc, Func<double, double, Point> toCanvas,
                                       BeamData beam, BeamReinforcement r, BarSelection selection,
                                       bool bottom, double scale)
        {
            if (selection == null || selection.Count <= 0) return;

            double diameter = selection.DiameterMm;
            double halfSpan = beam.WebWidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm - diameter / 2.0;
            if (halfSpan <= 0) return;

            int perLayer = Math.Max(selection.BarsPerLayer, 1);
            int remaining = selection.Count;
            int layerIndex = 0;
            double radius = Math.Max(2.0, diameter / 2.0 * scale);

            while (remaining > 0)
            {
                int count = Math.Min(perLayer, remaining);
                double offset = layerIndex * 2.0 * diameter;
                double z = bottom
                    ? r.CoverMm + r.StirrupDiameterMm + diameter / 2.0 + offset
                    : beam.HeightMm - r.CoverMm - r.StirrupDiameterMm - diameter / 2.0 - offset;

                for (int i = 0; i < count; i++)
                {
                    double y = count > 1
                        ? -halfSpan + 2.0 * halfSpan * i / (count - 1)
                        : 0.0;
                    dc.DrawEllipse(BarFill, null, toCanvas(y, z), radius, radius);
                }

                remaining -= count;
                layerIndex++;
            }
        }

        // ------------------------------------------------------------------
        // Elevation des zones de cadres
        // ------------------------------------------------------------------

        private static void DrawElevation(DrawingContext dc, BeamDesignResult result, Rect area)
        {
            BeamData beam = result.Beam;
            BeamReinforcement r = result.Reinforcement;
            if (beam.SpanMm <= 0) return;

            double margin = area.Width * 0.08;
            double scale = (area.Width - 2 * margin) / beam.SpanMm;
            double left = area.Left + margin;
            double beamHeight = Math.Min(area.Height * 0.42, beam.HeightMm * scale * 4.0);
            double top = area.Top + area.Height * 0.30;

            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(new Point(left, top), new Size(beam.SpanMm * scale, beamHeight)));

            // Cadres, dessines a leur espacement reel.
            foreach (BeamStirrupZone zone in r.StirrupZones)
            {
                if (zone.SpacingMm <= 0) continue;
                for (double x = zone.StartMm; x <= zone.EndMm + 0.1; x += zone.SpacingMm)
                {
                    double px = left + x * scale;
                    dc.DrawLine(ThinStirrupPen, new Point(px, top + 2), new Point(px, top + beamHeight - 2));
                }

                double centre = left + (zone.StartMm + zone.EndMm) / 2.0 * scale;
                DrawText(dc, string.Format("e={0:0}", zone.SpacingMm), 10.0, centre,
                         top + beamHeight + 12, true);
                DrawText(dc, string.Format("{0:0} kN", zone.DesignShearN / 1000.0), 9.5, centre,
                         top - 10, true);
            }

            // Limites de zones
            foreach (BeamStirrupZone zone in r.StirrupZones)
            {
                double px = left + zone.EndMm * scale;
                dc.DrawLine(DimensionPen, new Point(px, top - 4),
                            new Point(px, top + beamHeight + 4));
            }

            DrawText(dc, string.Format("Elevation - portee {0:0} mm", beam.SpanMm), 11.5,
                     area.Left + area.Width / 2.0, area.Top + 10, true);
            DrawText(dc, r.TransverseLabel, 10.5, area.Left + area.Width / 2.0,
                     top + beamHeight + 28, true);
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x, double y,
                                     bool centred, Brush brush = null, bool rightAligned = false)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, brush ?? TextBrush, 1.0);
            double leftEdge = x;
            if (centred) leftEdge = x - formatted.Width / 2.0;
            else if (rightAligned) leftEdge = x - formatted.Width;
            dc.DrawText(formatted, new Point(leftEdge, y - formatted.Height / 2.0));
        }
    }
}
