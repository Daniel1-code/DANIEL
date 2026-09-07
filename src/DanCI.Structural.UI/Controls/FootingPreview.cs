using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la semelle telle qu'elle sera modelisee : la coupe verticale au-dessus, avec
    /// les nappes, les attentes et le cone de poinconnement retenu ; la vue en plan
    /// au-dessous, avec le quadrillage reel des barres.
    /// </summary>
    public static class FootingPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush SoilFill = new SolidColorBrush(Color.FromRgb(222, 214, 198));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen ColumnOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 1.2);
        private static readonly Pen MeshPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 1.4);
        private static readonly Pen ThinMeshPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 0.7);
        private static readonly Pen StarterPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.0);
        private static readonly Brush BarFill = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(FootingDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Footing != null)
                {
                    DrawSection(dc, result, new Rect(0, 0, width, height * 0.46));
                    DrawPlan(dc, result, new Rect(0, height * 0.46, width, height * 0.54));
                }
                else
                {
                    DrawText(dc, "Aucune semelle selectionnee", 12, width / 2.0, height / 2.0, true);
                }
            }

            var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        // ------------------------------------------------------------------
        // Coupe verticale
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, FootingDesignResult result, Rect area)
        {
            FootingData f = result.Footing;
            FootingReinforcement r = result.Reinforcement;

            double margin = area.Width * 0.10;
            double totalHeight = f.ThicknessMm + Math.Max(r.StarterProjectionMm, 200.0);
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(f.WidthXMm, 1.0),
                                    (area.Height - 2 * margin) / Math.Max(totalHeight, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double baseY = area.Top + area.Height - margin;

            Func<double, double, Point> toCanvas = (xMm, zMm) =>
                new Point(centreX + xMm * scale, baseY - zMm * scale);

            // Sol de part et d'autre.
            dc.DrawRectangle(SoilFill, null, new Rect(
                new Point(area.Left, baseY), new Size(area.Width, Math.Max(margin * 0.5, 6))));

            // Semelle
            dc.DrawRectangle(ConcreteFill, ConcreteOutline, new Rect(
                toCanvas(-f.WidthXMm / 2.0, f.ThicknessMm),
                new Size(f.WidthXMm * scale, f.ThicknessMm * scale)));

            // Amorce du poteau
            double columnHeight = Math.Max(r.StarterProjectionMm, 200.0);
            dc.DrawRectangle(ConcreteFill, ColumnOutline, new Rect(
                toCanvas(-f.ColumnWidthXMm / 2.0, f.ThicknessMm + columnHeight),
                new Size(f.ColumnWidthXMm * scale, columnHeight * scale)));

            // Nappe inferieure vue en coupe : les barres // X en trait, celles // Y en points.
            double halfX = f.WidthXMm / 2.0 - r.CoverMm;
            double zBottomX = r.CoverMm + r.BottomX.DiameterMm / 2.0;
            double zBottomY = r.CoverMm + r.BottomX.DiameterMm + r.BottomY.DiameterMm / 2.0;
            dc.DrawLine(MeshPen, toCanvas(-halfX, zBottomX), toCanvas(halfX, zBottomX));

            double radius = Math.Max(1.6, r.BottomY.DiameterMm / 2.0 * scale);
            int countY = r.BottomY.CountOver(2.0 * halfX);
            for (int i = 0; i < countY; i++)
            {
                double t = countY > 1 ? (double)i / (countY - 1) : 0.5;
                dc.DrawEllipse(BarFill, null, toCanvas(-halfX + 2.0 * halfX * t, zBottomY),
                               radius, radius);
            }

            if (r.HasTopMesh)
            {
                double zTop = f.ThicknessMm - r.CoverMm - r.TopX.DiameterMm / 2.0;
                dc.DrawLine(MeshPen, toCanvas(-halfX, zTop), toCanvas(halfX, zTop));
            }

            // Attentes en L.
            if (r.StarterBarCount > 0)
            {
                double inset = f.ColumnWidthXMm / 2.0 - 40.0;
                double zStart = r.CoverMm + r.BottomX.DiameterMm + r.BottomY.DiameterMm
                                + r.StarterBarDiameterMm / 2.0;
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    double x = side * inset;
                    dc.DrawLine(StarterPen, toCanvas(x, zStart),
                                toCanvas(x - side * r.StarterReturnMm, zStart));
                    dc.DrawLine(StarterPen, toCanvas(x, zStart),
                                toCanvas(x, f.ThicknessMm + r.StarterProjectionMm));
                }
            }

            // Perimetre de controle retenu, reporte en coupe.
            if (result.Punching != null && result.Punching.Critical != null)
            {
                double a = result.Punching.Critical.DistanceMm;
                var dashed = new Pen(new SolidColorBrush(Color.FromRgb(150, 110, 30)), 1.0)
                {
                    DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
                };
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    dc.DrawLine(dashed,
                        toCanvas(side * f.ColumnWidthXMm / 2.0, f.ThicknessMm),
                        toCanvas(side * (f.ColumnWidthXMm / 2.0 + a), zBottomX));
                }
                DrawText(dc, string.Format("perimetre critique a {0:0} mm ({1:0.00} d)",
                             a, a / Math.Max(r.MeanEffectiveDepthMm, 1.0)),
                         9.5, centreX, area.Top + area.Height - 6, true);
            }

            DrawText(dc, string.Format("Coupe - {0:0} x {1:0} mm, h = {2:0} mm",
                         f.WidthXMm, f.WidthYMm, f.ThicknessMm),
                     11.5, centreX, area.Top + 12, true);
        }

        // ------------------------------------------------------------------
        // Vue en plan
        // ------------------------------------------------------------------

        private static void DrawPlan(DrawingContext dc, FootingDesignResult result, Rect area)
        {
            FootingData f = result.Footing;
            FootingReinforcement r = result.Reinforcement;

            double margin = Math.Min(area.Width, area.Height) * 0.14;
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(f.WidthXMm, 1.0),
                                    (area.Height - 2 * margin) / Math.Max(f.WidthYMm, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double centreY = area.Top + area.Height / 2.0 + 6;

            Func<double, double, Point> toCanvas = (xMm, yMm) =>
                new Point(centreX + xMm * scale, centreY - yMm * scale);

            dc.DrawRectangle(ConcreteFill, ConcreteOutline, new Rect(
                toCanvas(-f.WidthXMm / 2.0, f.WidthYMm / 2.0),
                new Size(f.WidthXMm * scale, f.WidthYMm * scale)));

            double halfX = f.WidthXMm / 2.0 - r.CoverMm;
            double halfY = f.WidthYMm / 2.0 - r.CoverMm;

            // Barres // X, repetees suivant Y.
            int countX = r.BottomX.CountOver(2.0 * halfY);
            for (int i = 0; i < countX; i++)
            {
                double t = countX > 1 ? (double)i / (countX - 1) : 0.5;
                double y = -halfY + 2.0 * halfY * t;
                dc.DrawLine(ThinMeshPen, toCanvas(-halfX, y), toCanvas(halfX, y));
            }

            // Barres // Y, repetees suivant X.
            int countY = r.BottomY.CountOver(2.0 * halfX);
            for (int i = 0; i < countY; i++)
            {
                double t = countY > 1 ? (double)i / (countY - 1) : 0.5;
                double x = -halfX + 2.0 * halfX * t;
                dc.DrawLine(ThinMeshPen, toCanvas(x, -halfY), toCanvas(x, halfY));
            }

            // Poteau porte.
            dc.DrawRectangle(null, ColumnOutline, new Rect(
                toCanvas(-f.ColumnWidthXMm / 2.0, f.ColumnWidthYMm / 2.0),
                new Size(f.ColumnWidthXMm * scale, f.ColumnWidthYMm * scale)));

            // Perimetre de controle du poinconnement, contour reel a coins arrondis.
            if (result.Punching != null && result.Punching.Critical != null)
            {
                double a = result.Punching.Critical.DistanceMm;
                var dashed = new Pen(new SolidColorBrush(Color.FromRgb(150, 110, 30)), 1.2)
                {
                    DashStyle = new DashStyle(new double[] { 4, 3 }, 0)
                };
                var rect = new Rect(
                    toCanvas(-f.ColumnWidthXMm / 2.0 - a, f.ColumnWidthYMm / 2.0 + a),
                    new Size((f.ColumnWidthXMm + 2 * a) * scale, (f.ColumnWidthYMm + 2 * a) * scale));
                dc.DrawRoundedRectangle(null, dashed, rect, a * scale, a * scale);
            }

            DrawText(dc, "Vue en plan - " + r.BottomLabel, 11.5, centreX, area.Top + 10, true);
            DrawText(dc, string.Format("{0} barres // X, {1} barres // Y", countX, countY),
                     10.0, centreX, area.Top + area.Height - 8, true);
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x, double y,
                                     bool centred, Brush brush = null)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, brush ?? TextBrush, 1.0);
            double leftEdge = centred ? x - formatted.Width / 2.0 : x;
            dc.DrawText(formatted, new Point(leftEdge, y - formatted.Height / 2.0));
        }
    }
}
