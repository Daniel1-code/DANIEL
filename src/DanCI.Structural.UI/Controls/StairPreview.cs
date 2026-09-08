using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.UI.Controls
{
    /// <summary>
    /// Dessine la volee : l'elevation avec ses marches, sa paillasse et son palier, et un
    /// agrandissement du noeud volee-palier.
    ///
    /// L'agrandissement n'est pas decoratif. Le noeud est le point ou le ferraillage d'un
    /// escalier se joue, et un dessin qui montrerait les barres suivant le pli enseignerait
    /// le mauvais detail. Ici elles se croisent, et l'oeil le voit.
    /// </summary>
    public static class StairPreview
    {
        private static readonly Brush ConcreteFill = new SolidColorBrush(Color.FromRgb(232, 232, 228));
        private static readonly Brush Background = new SolidColorBrush(Color.FromRgb(252, 252, 251));
        private static readonly Brush BarBrush = new SolidColorBrush(Color.FromRgb(28, 62, 122));
        private static readonly Brush TextBrush = new SolidColorBrush(Color.FromRgb(50, 50, 48));
        private static readonly Brush AlertBrush = new SolidColorBrush(Color.FromRgb(150, 40, 30));
        private static readonly Pen ConcreteOutline =
            new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), 1.5);
        private static readonly Pen StepPen =
            new Pen(new SolidColorBrush(Color.FromRgb(140, 140, 136)), 0.9);
        private static readonly Pen BarPen =
            new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), 2.2);
        private static readonly Pen TopBarPen =
            new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), 1.8);
        private static readonly Pen SupportPen =
            new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), 1.4);

        private static readonly Typeface Font =
            new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Normal,
                         FontStretches.Normal);

        public static ImageSource Render(StairDesignResult result, int width, int height)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Background, null, new Rect(0, 0, width, height));
                if (result != null && result.IsValid && result.Stair != null)
                {
                    DrawElevation(dc, result, new Rect(0, 0, width, height * 0.62));
                    if (result.Reinforcement.HasKneeJoint)
                    {
                        DrawKneeDetail(dc, result, new Rect(0, height * 0.62, width, height * 0.38));
                    }
                    else
                    {
                        DrawSection(dc, result, new Rect(0, height * 0.62, width, height * 0.38));
                    }
                }
                else
                {
                    DrawText(dc, "Aucune volee selectionnee", 12, width / 2.0, height / 2.0, true);
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

        private static void DrawElevation(DrawingContext dc, StairDesignResult result, Rect area)
        {
            StairData stair = result.Stair;
            StairReinforcement r = result.Reinforcement;
            if (stair.SpanMm <= 0 || stair.RiserCount < 2) return;

            double totalX = stair.TotalGoingMm + Math.Max(stair.LandingSpanMm, 0.0);
            double totalZ = stair.TotalRiseMm + stair.WaistThicknessMm;
            if (totalX <= 0 || totalZ <= 0) return;

            double margin = Math.Min(area.Width, area.Height) * 0.13;
            double scale = Math.Min((area.Width - 2 * margin) / totalX,
                                    (area.Height - 2 * margin) / totalZ);
            double originX = area.Left + margin;
            double originY = area.Top + area.Height - margin;

            Func<double, double, Point> P = (x, z) =>
                new Point(originX + x * scale, originY - z * scale);

            double slope = stair.SlopeTangent;
            double going = stair.TotalGoingMm;
            double landing = Math.Max(stair.LandingSpanMm, 0.0);

            // Epaisseur de paillasse mesuree VERTICALEMENT : t / cos alpha. C'est cette
            // hauteur de beton qui pese, et le dessin doit la montrer telle quelle.
            double verticalWaist = stair.WaistThicknessMm / Math.Max(stair.SlopeCosine, 0.05);

            // Contour du beton : sous-face montante, pli, palier, puis les marches.
            var figure = new PathFigure { StartPoint = P(0, 0), IsClosed = true, IsFilled = true };
            figure.Segments.Add(new LineSegment(P(going, going * slope), true));
            if (landing > 0)
            {
                figure.Segments.Add(new LineSegment(P(going + landing, going * slope), true));
                figure.Segments.Add(new LineSegment(
                    P(going + landing, going * slope + stair.LandingThicknessMm), true));
                figure.Segments.Add(new LineSegment(
                    P(going, going * slope + stair.LandingThicknessMm), true));
            }
            // Les marches, du haut vers le bas.
            double topOfWaist = going * slope + verticalWaist;
            figure.Segments.Add(new LineSegment(P(going, topOfWaist), true));
            for (int i = stair.RiserCount - 1; i >= 1; i--)
            {
                double x = (i - 1) * stair.TreadDepthMm;
                double zNose = (i - 1) * slope * stair.TreadDepthMm + verticalWaist;
                figure.Segments.Add(new LineSegment(P(x, zNose + stair.RiserHeightMm), true));
                figure.Segments.Add(new LineSegment(P(x, zNose), true));
            }
            figure.Segments.Add(new LineSegment(P(0, verticalWaist), true));

            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(ConcreteFill, ConcreteOutline, geometry);

            // La ligne de paillasse, pour montrer qu'elle est bien perpendiculaire a la pente.
            dc.DrawLine(StepPen, P(0, verticalWaist), P(going, topOfWaist));

            // Les appuis.
            foreach (double x in new[] { 0.0, going + landing })
            {
                double z = x <= 0 ? 0.0 : going * slope;
                Point p = P(x, z);
                dc.DrawLine(SupportPen, new Point(p.X - 10, p.Y + 4), new Point(p.X + 10, p.Y + 4));
                dc.DrawLine(SupportPen, new Point(p.X, p.Y), new Point(p.X - 7, p.Y + 4));
                dc.DrawLine(SupportPen, new Point(p.X, p.Y), new Point(p.X + 7, p.Y + 4));
            }

            // Nappe inferieure : elle monte le long de la sous-face puis remonte au pli.
            // L'enrobage d'une face inclinee se mesure NORMALEMENT a la pente : le
            // decalage vertical vaut c / cos alpha, et le dessin doit le montrer tel quel.
            double cos = Math.Max(stair.SlopeCosine, 0.05);
            double zBar = (r.CoverMm + r.BottomMain.DiameterMm / 2.0) / cos;
            double zBarLanding = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            dc.DrawLine(BarPen, P(0, zBar), P(going, going * slope + zBar));
            if (landing > 0)
            {
                double zLandingTop = going * slope + stair.LandingThicknessMm - r.CoverMm;
                dc.DrawLine(BarPen, P(going, going * slope + zBar), P(going, zLandingTop));
                dc.DrawLine(BarPen, P(going, zLandingTop),
                            P(Math.Min(going + r.KneeAnchorageMm, going + landing), zLandingTop));

                dc.DrawLine(BarPen, P(going + landing, going * slope + zBarLanding),
                            P(going, going * slope + zBarLanding));
            }

            // Chapeaux.
            if (r.HasTopReinforcement && r.TopBarLengthMm > 0)
            {
                double reach = Math.Min(r.TopBarLengthMm, going);
                double zTopNormal = verticalWaist - r.CoverMm / cos;
                dc.DrawLine(TopBarPen, P(0, zTopNormal),
                            P(reach, reach * slope + zTopNormal));
                if (landing > 0)
                {
                    double zTop = going * slope + stair.LandingThicknessMm - r.CoverMm;
                    double from = Math.Max(going + landing - r.TopBarLengthMm, going);
                    dc.DrawLine(TopBarPen, P(from, zTop), P(going + landing, zTop));
                }
            }

            DrawText(dc, string.Format("Elevation - {0}", stair.SectionLabel), 11.0,
                     area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc, string.Format(
                "portee {0:0} mm - paillasse {1:0} mm perpendiculaire a la pente " +
                "({2:0} mm verticalement)",
                stair.SpanMm, stair.WaistThicknessMm, verticalWaist),
                9.5, area.Left + area.Width / 2.0, area.Top + 26, true);
        }

        // ------------------------------------------------------------------
        // Le noeud, en gros
        // ------------------------------------------------------------------

        private static void DrawKneeDetail(DrawingContext dc, StairDesignResult result, Rect area)
        {
            StairData stair = result.Stair;
            StairReinforcement r = result.Reinforcement;

            double margin = Math.Min(area.Width, area.Height) * 0.18;
            double windowMm = Math.Max(stair.WaistThicknessMm * 5.0, 600.0);
            double scale = Math.Min((area.Width - 2 * margin) / (windowMm * 1.6),
                                    (area.Height - 2 * margin) / windowMm);
            double cx = area.Left + area.Width * 0.42;
            double cy = area.Top + area.Height * 0.68;
            double slope = stair.SlopeTangent;
            double t = stair.WaistThicknessMm;
            double tv = t / Math.Max(stair.SlopeCosine, 0.05);
            double reach = windowMm * 0.62;

            Func<double, double, Point> P = (x, z) => new Point(cx + x * scale, cy - z * scale);

            // Beton : la volee arrive par la gauche en montant, le palier repart a droite.
            var figure = new PathFigure { StartPoint = P(-reach, -reach * slope), IsClosed = true };
            figure.Segments.Add(new LineSegment(P(0, 0), true));                      // le pli
            figure.Segments.Add(new LineSegment(P(reach, 0), true));                  // sous-face palier
            figure.Segments.Add(new LineSegment(P(reach, stair.LandingThicknessMm), true));
            figure.Segments.Add(new LineSegment(P(0, stair.LandingThicknessMm), true));
            figure.Segments.Add(new LineSegment(P(0, tv), true));
            figure.Segments.Add(new LineSegment(P(-reach, tv - reach * slope), true));
            var geometry = new PathGeometry();
            geometry.Figures.Add(figure);
            dc.DrawGeometry(ConcreteFill, ConcreteOutline, geometry);

            double c = r.CoverMm;
            double zTopLanding = stair.LandingThicknessMm - c;

            // Barre de volee : elle monte, traverse au droit du pli, s'ancre en face
            // superieure du palier.
            var flight = new PathFigure { StartPoint = P(-reach, -reach * slope + c) };
            flight.Segments.Add(new LineSegment(P(0, c), true));
            flight.Segments.Add(new LineSegment(P(0, zTopLanding), true));
            flight.Segments.Add(new LineSegment(P(reach * 0.85, zTopLanding), true));
            var flightGeometry = new PathGeometry();
            flightGeometry.Figures.Add(flight);
            dc.DrawGeometry(null, BarPen, flightGeometry);

            // Barre de palier : symetrique, elle s'ancre en face superieure de la paillasse.
            var landingBar = new PathFigure { StartPoint = P(reach, c) };
            landingBar.Segments.Add(new LineSegment(P(0, c), true));
            landingBar.Segments.Add(new LineSegment(P(0, tv - c), true));
            landingBar.Segments.Add(new LineSegment(
                P(-reach * 0.85, tv - c - reach * 0.85 * slope), true));
            var landingGeometry = new PathGeometry();
            landingGeometry.Figures.Add(landingBar);
            dc.DrawGeometry(null, BarPen, landingGeometry);

            // Le point que le dessin doit faire passer.
            DrawText(dc, "Noeud volee-palier : les nappes se CROISENT", 11.0,
                     area.Left + area.Width / 2.0, area.Top + 12, true);
            DrawText(dc,
                "l'angle rentrant est tendu - une barre qui suivrait le pli ferait sauter " +
                "l'enrobage",
                9.0, area.Left + area.Width / 2.0, area.Top + 26, true, AlertBrush);
            DrawText(dc, string.Format("ancrage {0:0} mm au-dela du pli", r.KneeAnchorageMm),
                     9.5, area.Left + area.Width * 0.80, cy + 18, true);
        }

        // ------------------------------------------------------------------
        // Coupe transversale, quand il n'y a pas de noeud
        // ------------------------------------------------------------------

        private static void DrawSection(DrawingContext dc, StairDesignResult result, Rect area)
        {
            StairData stair = result.Stair;
            StairReinforcement r = result.Reinforcement;

            double margin = Math.Min(area.Width, area.Height) * 0.18;
            double scale = Math.Min((area.Width - 2 * margin) / Math.Max(stair.WidthMm, 1.0),
                                    (area.Height - 2 * margin)
                                    / Math.Max(stair.WaistThicknessMm * 2.5, 1.0));
            double centreX = area.Left + area.Width / 2.0;
            double bottom = area.Top + area.Height - margin;

            Func<double, double, Point> P = (y, z) =>
                new Point(centreX + y * scale, bottom - z * scale);

            dc.DrawRectangle(ConcreteFill, ConcreteOutline, new Rect(
                P(-stair.WidthMm / 2.0, stair.WaistThicknessMm),
                new Size(stair.WidthMm * scale, stair.WaistThicknessMm * scale)));

            double z = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double radius = Math.Max(2.0, r.BottomMain.DiameterMm / 2.0 * scale);
            int count = Math.Max(r.BottomMain.CountOver(stair.WidthMm - 2 * r.CoverMm), 1);
            for (int i = 0; i < count; i++)
            {
                double y = count > 1
                    ? -stair.WidthMm / 2.0 + r.CoverMm
                      + (stair.WidthMm - 2 * r.CoverMm) * i / (count - 1)
                    : 0.0;
                dc.DrawEllipse(BarBrush, null, P(y, z), radius, radius);
            }

            DrawText(dc, "Coupe sur la paillasse", 11.0, centreX, area.Top + 12, true);
            DrawText(dc, r.BottomLabel, 9.5, centreX, area.Bottom - 12, true);
        }

        private static void DrawText(DrawingContext dc, string text, double size, double x,
                                     double y, bool centred, Brush brush = null)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, Font, size, brush ?? TextBrush, 1.0);
            double leftEdge = centred ? x - formatted.Width / 2.0 : x;
            dc.DrawText(formatted, new Point(leftEdge, y - formatted.Height / 2.0));
        }
    }
}
