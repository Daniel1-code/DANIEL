using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la semelle filante : la coupe transversale, qui est la vraie vue de calcul,
    /// avec les crochets d'extremite lorsqu'ils sont exiges, et sous elle le diagramme des
    /// contraintes du sol, trapezoidal des qu'il y a excentrement.
    /// </summary>
    public static class StripFootingPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush SoilFill = new SolidColorBrush(Color.FromRgb(222, 214, 198));
        private static readonly Brush PressureFill = new SolidColorBrush(Color.FromArgb(70, 70, 130, 180));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Brush BarBrush = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen WallOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 1.2);
        private static readonly Pen BarPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 2.0);
        private static readonly Pen StarterPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.0);
        private static readonly Pen PressurePen =
            new Pen(new SolidColorBrush(Color.FromRgb(70, 130, 180)), 1.2);
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(StripFootingDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Footing != null)
                {
                    DrawSection(dc, result, new Rect(0, 0, width, height * 0.62));
                    DrawPressure(dc, result, new Rect(0, height * 0.62, width, height * 0.38));
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
        // Coupe transversale
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, StripFootingDesignResult result,
                                        Rect area)
        {
            StripFootingData f = result.Footing;
            StripFootingReinforcement r = result.Reinforcement;

            double margin = area.Width * 0.12;
            double wallHeight = Math.Max(r.StarterProjectionMm, 250.0);
            double totalHeight = f.ThicknessMm + wallHeight;
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(f.WidthMm, 1.0),
                                    (area.Height - 2 * margin) / Math.Max(totalHeight, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double baseY = area.Top + area.Height - margin;

            Func<double, double, Point> toCanvas = (xMm, zMm) =>
                new Point(centreX + xMm * scale, baseY - zMm * scale);

            // Sol de part et d'autre.
            dc.DrawRectangle(SoilFill, null,
                new Rect(new Point(area.Left, baseY), new Size(area.Width, Math.Max(margin * 0.4, 5))));

            // Semelle.
            dc.DrawRectangle(ConcreteFill, ConcreteOutline, new Rect(
                toCanvas(-f.WidthMm / 2.0, f.ThicknessMm),
                new Size(f.WidthMm * scale, f.ThicknessMm * scale)));

            // Amorce du voile.
            dc.DrawRectangle(ConcreteFill, WallOutline, new Rect(
                toCanvas(-f.WallThicknessMm / 2.0, f.ThicknessMm + wallHeight),
                new Size(f.WallThicknessMm * scale, wallHeight * scale)));

            // Armature transversale, avec ses crochets si le moteur les exige.
            double half = f.WidthMm / 2.0 - r.CoverMm;
            double zBar = r.CoverMm + r.Transverse.DiameterMm / 2.0;
            dc.DrawLine(BarPen, toCanvas(-half, zBar), toCanvas(half, zBar));
            if (r.TransverseNeedsHook)
            {
                double hook = Math.Min(10.0 * r.Transverse.DiameterMm,
                                       f.ThicknessMm - 2.0 * r.CoverMm);
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    dc.DrawLine(BarPen, toCanvas(side * half, zBar),
                                toCanvas(side * half, zBar + hook));
                }
            }

            // Repartition longitudinale, vue en bout.
            double zLong = r.CoverMm + r.Transverse.DiameterMm
                           + r.Longitudinal.DiameterMm / 2.0;
            int count = r.Longitudinal.CountOver(2.0 * half);
            double radius = Math.Max(1.8, r.Longitudinal.DiameterMm / 2.0 * scale);
            for (int i = 0; i < count; i++)
            {
                double t = count > 1 ? (double)i / (count - 1) : 0.5;
                dc.DrawEllipse(BarBrush, null, toCanvas(-half + 2.0 * half * t, zLong),
                               radius, radius);
            }

            // Attentes du voile, en L.
            if (r.HasStarters)
            {
                double inset = f.WallThicknessMm / 2.0 - r.CoverMm - r.StarterDiameterMm / 2.0;
                double zStart = zLong + r.Longitudinal.DiameterMm / 2.0
                                + r.StarterDiameterMm / 2.0;
                if (inset > 0)
                {
                    foreach (double side in new[] { -1.0, 1.0 })
                    {
                        double x = side * inset;
                        dc.DrawLine(StarterPen, toCanvas(x, zStart),
                                    toCanvas(x - side * r.StarterReturnMm, zStart));
                        dc.DrawLine(StarterPen, toCanvas(x, zStart),
                                    toCanvas(x, f.ThicknessMm + r.StarterProjectionMm));
                    }
                }
            }

            // Cote du debord, qui commande tout le calcul.
            foreach (double side in new[] { -1.0, 1.0 })
            {
                double from = side * f.WallThicknessMm / 2.0;
                double to = side * f.WidthMm / 2.0;
                double y = baseY + margin * 0.55;
                dc.DrawLine(WallOutline, new Point(centreX + from * scale, y),
                            new Point(centreX + to * scale, y));
            }
            DrawText(dc, string.Format("debord {0:0} mm", f.OverhangMm), 9.5,
                     centreX + (f.WallThicknessMm / 2.0 + f.OverhangMm / 2.0) * scale,
                     baseY + margin * 0.55 - 8, true);

            DrawText(dc, string.Format("Coupe transversale - {0}", f.SectionLabel),
                     11.5, centreX, area.Top + 12, true);
            DrawText(dc, r.TransverseLabel
                         + (r.TransverseNeedsHook ? " avec crochets d'extremite" : ""),
                     10.5, centreX, area.Top + 28, true);
        }

        // ------------------------------------------------------------------
        // Diagramme des contraintes
        // ------------------------------------------------------------------

        private static void DrawPressure(DrawingContext dc, StripFootingDesignResult result,
                                         Rect area)
        {
            if (result.Pressure == null) return;
            StripFootingData f = result.Footing;

            double margin = area.Width * 0.12;
            double scale = (area.Width - 2 * margin) / Math.Max(f.WidthMm, 1.0);
            double centreX = area.Left + area.Width / 2.0;
            double top = area.Top + 34;
            double maxHeight = area.Height * 0.44;

            double maxPressure = Math.Max(result.Pressure.MaxPressureKpa, 1.0);
            double left = centreX - f.WidthMm / 2.0 * scale;
            double right = centreX + f.WidthMm / 2.0 * scale;

            // La semelle, en trait fin, au-dessus du diagramme.
            dc.DrawLine(ConcreteOutline, new Point(left, top), new Point(right, top));

            double hLeft = Math.Max(result.Pressure.MinPressureKpa, 0.0) / maxPressure * maxHeight;
            double hRight = result.Pressure.MaxPressureKpa / maxPressure * maxHeight;

            var geometry = new StreamGeometry();
            using (StreamGeometryContext ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(left, top), true, true);
                ctx.LineTo(new Point(right, top), true, false);
                ctx.LineTo(new Point(right, top + hRight), true, false);
                ctx.LineTo(new Point(left, top + hLeft), true, false);
            }
            geometry.Freeze();
            dc.DrawGeometry(PressureFill, PressurePen, geometry);

            DrawText(dc, string.Format("{0:0} kPa", result.Pressure.MinPressureKpa), 9.5,
                     left, top + hLeft + 12, true);
            DrawText(dc, string.Format("{0:0} kPa", result.Pressure.MaxPressureKpa), 9.5,
                     right, top + hRight + 12, true);

            string title = result.Pressure.EccentricityXMm > 1.0
                ? string.Format("Contraintes du sol - excentricite {0:0} mm{1}",
                                result.Pressure.EccentricityXMm,
                                result.Pressure.WithinCore ? "" : " HORS NOYAU CENTRAL")
                : "Contraintes du sol - charge centree";
            DrawText(dc, title, 11.0, centreX, area.Top + 12, true);
            DrawText(dc, string.Format("sigma' sur B' = {0:0} mm  ->  {1:0} kPa",
                         result.Pressure.EffectiveWidthMm,
                         result.Pressure.EffectivePressureKpa),
                     9.5, centreX, area.Bottom - 10, true);
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
