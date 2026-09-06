using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.UI
{
    /// <summary>
    /// Dessine la coupe du poteau telle qu'elle sera modelisee : beton, cadre, epingles,
    /// barres longitudinales et cotes. Permet de valider le ferraillage d'un coup d'oeil
    /// avant de generer quoi que ce soit dans Revit.
    /// </summary>
    public static class SectionPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen StirrupPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.2);
        private static readonly Pen CrossTiePen =
            new Pen(new SolidColorBrush(Color.FromRgb(224, 122, 40)), 1.8);
        private static readonly Pen DimensionPen =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 0.8);
        private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        /// <summary>Rend la coupe dans une image carree de <paramref name="pixels"/> de cote.</summary>
        public static ImageSource Render(ColumnGeometry geometry, DesignResult design, int pixels)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, pixels, pixels));
                if (geometry != null && design != null && design.IsValid)
                {
                    Draw(dc, geometry, design, pixels);
                }
                else
                {
                    DrawText(dc, "Aucun poteau selectionne", 12, pixels / 2.0, pixels / 2.0, true);
                }
            }

            var bitmap = new RenderTargetBitmap(pixels, pixels, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        private static void Draw(DrawingContext dc, ColumnGeometry g, DesignResult d, int pixels)
        {
            double margin = pixels * 0.16;
            double available = pixels - 2.0 * margin;
            double sizeMm = Math.Max(g.MaxDimensionMm, 1.0);
            double scale = available / sizeMm;
            double centreX = pixels / 2.0;
            double centreY = pixels * 0.47;

            Func<double, double, Point> toCanvas = (xMm, yMm) =>
                new Point(centreX + xMm * scale, centreY - yMm * scale);

            // --- Beton ---
            if (g.Kind == SectionKind.Circular)
            {
                double radius = g.DiameterMm / 2.0 * scale;
                dc.DrawEllipse(ConcreteFill, ConcreteOutline, new Point(centreX, centreY), radius, radius);
            }
            else
            {
                Point topLeft = toCanvas(-g.WidthMm / 2.0, g.DepthMm / 2.0);
                dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                    new Rect(topLeft, new Size(g.WidthMm * scale, g.DepthMm * scale)));
            }

            // --- Cadre ---
            if (g.Kind == SectionKind.Circular)
            {
                double radius = RebarLayout.StirrupRadius(g, d) * scale;
                if (radius > 0)
                {
                    dc.DrawEllipse(null, StirrupPen, new Point(centreX, centreY), radius, radius);
                }
            }
            else
            {
                double halfX = RebarLayout.StirrupHalfX(g, d);
                double halfY = RebarLayout.StirrupHalfY(g, d);
                if (halfX > 0 && halfY > 0)
                {
                    Point corner = toCanvas(-halfX, halfY);
                    var rect = new Rect(corner, new Size(2.0 * halfX * scale, 2.0 * halfY * scale));
                    double radius = Math.Min(6.0, Math.Min(rect.Width, rect.Height) / 6.0);
                    dc.DrawRoundedRectangle(null, StirrupPen, rect, radius, radius);
                }

                // --- Epingles ---
                double barHalfX = RebarLayout.BarHalfSpanX(g, d);
                double barHalfY = RebarLayout.BarHalfSpanY(g, d);
                foreach (double x in RebarLayout.CrossTieXPositions(g, d))
                {
                    dc.DrawLine(CrossTiePen, toCanvas(x, -barHalfY), toCanvas(x, barHalfY));
                }
                foreach (double y in RebarLayout.CrossTieYPositions(g, d))
                {
                    dc.DrawLine(CrossTiePen, toCanvas(-barHalfX, y), toCanvas(barHalfX, y));
                }
            }

            // --- Barres longitudinales ---
            List<BarPoint> bars = RebarLayout.Bars(g, d);
            double barRadius = Math.Max(2.5, d.BarDiameterMm / 2.0 * scale);
            foreach (BarPoint bar in bars)
            {
                dc.DrawEllipse(BarFill, null, toCanvas(bar.XMm, bar.YMm), barRadius, barRadius);
            }

            // --- Cotes ---
            if (g.Kind == SectionKind.Rectangular)
            {
                DrawHorizontalDimension(dc, toCanvas, g.WidthMm, g.DepthMm,
                    string.Format("{0:0}", g.WidthMm));
                DrawVerticalDimension(dc, toCanvas, g.WidthMm, g.DepthMm,
                    string.Format("{0:0}", g.DepthMm));
            }
            else
            {
                double half = g.DiameterMm / 2.0;
                Point left = toCanvas(-half, -half - 40);
                Point right = toCanvas(half, -half - 40);
                dc.DrawLine(DimensionPen, left, right);
                DrawText(dc, string.Format("D {0:0}", g.DiameterMm), 11,
                         (left.X + right.X) / 2.0, left.Y + 11, true);
            }

            // --- Legende ---
            double lineHeight = 14;
            double y0 = pixels - 4.0 * lineHeight - 6;
            DrawText(dc, d.LongitudinalLabel + "   (enrobage " + string.Format("{0:0}", d.CoverMm) + " mm)",
                     11.5, centreX, y0, true);
            DrawText(dc, "Cadres " + d.TransverseLabel, 11.5, centreX, y0 + lineHeight, true);

            if (g.Kind == SectionKind.Rectangular)
            {
                string pitch = string.Format("Entraxe {0:0} x {1:0} mm",
                    RebarLayout.PitchX(g, d), RebarLayout.PitchY(g, d));
                int ties = d.CrossTiesAlongX + d.CrossTiesAlongY;
                if (ties > 0) pitch += string.Format("  -  {0} epingle(s) par lit", ties);
                DrawText(dc, pitch, 11.5, centreX, y0 + 2 * lineHeight, true);
            }

            if (d.Check != null && d.Check.Performed)
            {
                string verdict = string.Format("Verification N-M : {0} (taux {1:0.00})",
                    d.Check.Passes ? "OK" : "NE RESISTE PAS", d.Check.Utilisation);
                var brush = d.Check.Passes
                    ? (Brush)new SolidColorBrush(Color.FromRgb(24, 122, 62))
                    : new SolidColorBrush(Color.FromRgb(190, 32, 28));
                DrawText(dc, verdict, 11.5, centreX, y0 + 3 * lineHeight, true, brush);
            }
        }

        private static void DrawHorizontalDimension(DrawingContext dc, Func<double, double, Point> toCanvas,
                                                    double widthMm, double depthMm, string label)
        {
            double offset = depthMm / 2.0 + Math.Max(30.0, depthMm * 0.12);
            Point left = toCanvas(-widthMm / 2.0, -offset);
            Point right = toCanvas(widthMm / 2.0, -offset);
            dc.DrawLine(DimensionPen, left, right);
            dc.DrawLine(DimensionPen, new Point(left.X, left.Y - 4), new Point(left.X, left.Y + 4));
            dc.DrawLine(DimensionPen, new Point(right.X, right.Y - 4), new Point(right.X, right.Y + 4));
            DrawText(dc, label, 11, (left.X + right.X) / 2.0, left.Y + 10, true);
        }

        private static void DrawVerticalDimension(DrawingContext dc, Func<double, double, Point> toCanvas,
                                                  double widthMm, double depthMm, string label)
        {
            double offset = widthMm / 2.0 + Math.Max(30.0, widthMm * 0.12);
            Point bottom = toCanvas(-offset, -depthMm / 2.0);
            Point top = toCanvas(-offset, depthMm / 2.0);
            dc.DrawLine(DimensionPen, bottom, top);
            dc.DrawLine(DimensionPen, new Point(bottom.X - 4, bottom.Y), new Point(bottom.X + 4, bottom.Y));
            dc.DrawLine(DimensionPen, new Point(top.X - 4, top.Y), new Point(top.X + 4, top.Y));
            DrawText(dc, label, 11, bottom.X - 6, (bottom.Y + top.Y) / 2.0, false, null, true);
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x, double y,
                                     bool centred, Brush brush = null, bool rightAligned = false)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, brush ?? TextBrush, 1.0);
            double left = x;
            if (centred) left = x - formatted.Width / 2.0;
            else if (rightAligned) left = x - formatted.Width;
            dc.DrawText(formatted, new Point(left, y - formatted.Height / 2.0));
        }
    }
}
