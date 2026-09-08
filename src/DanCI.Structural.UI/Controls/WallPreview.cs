using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine le voile aux deux echelles qu'il faut garder distinctes : l'elevation avec
    /// ses deux nappes et ses barres de rive, et la coupe horizontale qui montre
    /// l'epaisseur, l'enrobage et la position reelle des deux nappes.
    /// </summary>
    public static class WallPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Brush VerticalBrush = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush EdgeBrush = new SolidColorBrush(Color.FromRgb(196, 46, 34));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.6);
        private static readonly Pen VerticalPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 1.1);
        private static readonly Pen HorizontalPen =
            new Pen(new SolidColorBrush(Color.FromRgb(70, 130, 180)), 0.8);
        private static readonly Pen EdgePen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 2.2);
        private static readonly Pen LinkPen =
            new Pen(new SolidColorBrush(Color.FromRgb(150, 110, 30)), 1.2);
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(WallDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Wall != null)
                {
                    DrawElevation(dc, result, new Rect(0, 0, width, height * 0.60));
                    DrawSection(dc, result, new Rect(0, height * 0.60, width, height * 0.40));
                }
                else
                {
                    DrawText(dc, "Aucun voile selectionne", 12, width / 2.0, height / 2.0, true);
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

        private static void DrawElevation(DrawingContext dc, WallDesignResult result, Rect area)
        {
            WallData wall = result.Wall;
            WallReinforcement r = result.Reinforcement;
            if (wall.LengthMm <= 0 || wall.ClearHeightMm <= 0) return;

            double margin = Math.Min(area.Width, area.Height) * 0.12;
            double scale = Math.Min((area.Width - 2 * margin) / wall.LengthMm,
                                    (area.Height - 2 * margin) / wall.ClearHeightMm);
            double left = area.Left + (area.Width - wall.LengthMm * scale) / 2.0;
            double bottom = area.Top + area.Height - margin;
            double top = bottom - wall.ClearHeightMm * scale;

            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(new Point(left, top), new Size(wall.LengthMm * scale,
                                                        wall.ClearHeightMm * scale)));

            // Aciers verticaux, a leur espacement reel.
            if (r.VerticalPerFace.SpacingMm > 0)
            {
                for (double x = r.CoverMm; x <= wall.LengthMm - r.CoverMm;
                     x += r.VerticalPerFace.SpacingMm)
                {
                    double px = left + x * scale;
                    dc.DrawLine(VerticalPen, new Point(px, top + 2), new Point(px, bottom - 2));
                }
            }

            // Aciers horizontaux, a leur espacement reel.
            if (r.HorizontalPerFace.SpacingMm > 0)
            {
                for (double z = r.CoverMm; z <= wall.ClearHeightMm - r.CoverMm;
                     z += r.HorizontalPerFace.SpacingMm)
                {
                    double pz = bottom - z * scale;
                    dc.DrawLine(HorizontalPen, new Point(left + 2, pz),
                                new Point(left + wall.LengthMm * scale - 2, pz));
                }
            }

            // Barres de rive : la concentration d'acier aux extremites.
            if (r.HasEdgeBars)
            {
                int pairs = Math.Max(r.EdgeBarCount / 2, 1);
                double pitch = Math.Max(r.EdgeBarDiameterMm * 4.0, 100.0);
                foreach (double edge in new[] { r.CoverMm, wall.LengthMm - r.CoverMm })
                {
                    double direction = edge < wall.LengthMm / 2.0 ? 1.0 : -1.0;
                    for (int i = 0; i < pairs; i++)
                    {
                        double px = left + (edge + direction * i * pitch) * scale;
                        dc.DrawLine(EdgePen, new Point(px, top + 2), new Point(px, bottom - 2));
                    }
                }
            }

            DrawText(dc, string.Format("Elevation - {0:0} x {1:0} mm, h = {2:0} mm",
                         wall.LengthMm, wall.ThicknessMm, wall.ClearHeightMm),
                     11.5, area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc, string.Format("lambda = {0:0.0}{1}", result.SlendernessRatio,
                         result.SecondOrder != null && result.SecondOrder.Required
                             ? string.Format("  -  e2 = {0:0} mm",
                                             result.SecondOrder.SecondOrderEccentricityMm)
                             : "  -  second ordre neglige"),
                     10.5, area.Left + area.Width / 2.0, bottom + 14, true);
        }

        // ------------------------------------------------------------------
        // Coupe horizontale
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, WallDesignResult result, Rect area)
        {
            WallData wall = result.Wall;
            WallReinforcement r = result.Reinforcement;

            // On ne represente qu'un metre de voile : a l'echelle de la longueur totale,
            // l'epaisseur serait invisible.
            const double shownLength = WallData.StripWidthMm;
            double margin = area.Width * 0.12;
            double scale = (area.Width - 2 * margin) / shownLength;
            double drawnThickness = Math.Min(area.Height * 0.42, wall.ThicknessMm * scale * 3.0);
            double vertical = drawnThickness / wall.ThicknessMm;

            double left = area.Left + margin;
            double top = area.Top + area.Height * 0.34;
            double bottom = top + drawnThickness;

            dc.DrawRectangle(ConcreteFill, ConcreteOutline,
                new Rect(new Point(left, top), new Size(shownLength * scale, drawnThickness)));

            // Les deux nappes verticales, vues en bout.
            double radius = Math.Max(2.0, r.VerticalPerFace.DiameterMm / 2.0 * scale * 2.0);
            double inset = (r.CoverMm + r.VerticalPerFace.DiameterMm / 2.0) * vertical;
            if (r.VerticalPerFace.SpacingMm > 0)
            {
                for (double x = r.VerticalPerFace.SpacingMm / 2.0; x < shownLength;
                     x += r.VerticalPerFace.SpacingMm)
                {
                    double px = left + x * scale;
                    dc.DrawEllipse(VerticalBrush, null, new Point(px, top + inset), radius, radius);
                    dc.DrawEllipse(VerticalBrush, null, new Point(px, bottom - inset), radius, radius);
                }
            }

            // Aciers horizontaux, filants, derriere les verticaux.
            double horizontalInset = (r.CoverMm + r.VerticalPerFace.DiameterMm
                                      + r.HorizontalPerFace.DiameterMm / 2.0) * vertical;
            dc.DrawLine(HorizontalPen, new Point(left + 2, top + horizontalInset),
                        new Point(left + shownLength * scale - 2, top + horizontalInset));
            dc.DrawLine(HorizontalPen, new Point(left + 2, bottom - horizontalInset),
                        new Point(left + shownLength * scale - 2, bottom - horizontalInset));

            // Epingles de liaison, si l'article 9.6.4 les exige.
            if (r.HasLinks)
            {
                double pitch = 1000.0 / Math.Sqrt(Math.Max(r.LinksPerSquareMetre, 1.0));
                for (double x = pitch / 2.0; x < shownLength; x += pitch)
                {
                    double px = left + x * scale;
                    dc.DrawLine(LinkPen, new Point(px, top + inset), new Point(px, bottom - inset));
                }
            }

            DrawText(dc, "Coupe horizontale sur 1 000 mm - epaisseur a une echelle dilatee",
                     10.5, area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc, string.Format("{0}  -  enrobage {1:0} mm",
                         r.VerticalLabel, r.CoverMm),
                     10.0, area.Left + area.Width / 2.0, bottom + 16, true);
            if (r.HasLinks)
            {
                DrawText(dc, string.Format("epingles HA{0:0}, {1:0.0} au m2 (art. 9.6.4)",
                             r.LinkDiameterMm, r.LinksPerSquareMetre),
                         9.5, area.Left + area.Width / 2.0, bottom + 32, true);
            }
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
