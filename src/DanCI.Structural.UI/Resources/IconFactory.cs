using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanCI.Structural.UI.Resources
{
    /// <summary>
    /// Dessine les icones du ruban a la volee : le plugin reste un fichier unique,
    /// sans ressource image a deployer.
    /// </summary>
    public static class IconFactory
    {
        /// <summary>Icone representant un poteau arme (section beton, barres et cadres).</summary>
        public static ImageSource CreateColumnRebarIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                double margin = s * 0.18;
                var concrete = new Rect(margin, s * 0.06, s - 2 * margin, s * 0.88);

                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(214, 214, 210)),
                                 new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), s * 0.03),
                                 concrete);

                var steel = new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), s * 0.055);
                double inset = s * 0.10;
                double left = concrete.Left + inset;
                double right = concrete.Right - inset;
                double top = concrete.Top + inset * 0.6;
                double bottom = concrete.Bottom - inset * 0.6;

                // Barres longitudinales.
                dc.DrawLine(steel, new Point(left, top), new Point(left, bottom));
                dc.DrawLine(steel, new Point(right, top), new Point(right, bottom));
                dc.DrawLine(steel, new Point((left + right) / 2, top), new Point((left + right) / 2, bottom));

                // Cadres, resserres en pied et en tete.
                double[] levels = { 0.06, 0.16, 0.34, 0.56, 0.74, 0.84, 0.94 };
                foreach (double level in levels)
                {
                    double y = top + (bottom - top) * level;
                    dc.DrawLine(steel, new Point(left, y), new Point(right, y));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Icone representant une poutre armee, vue en elevation.</summary>
        public static ImageSource CreateBeamIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                var concrete = new Rect(s * 0.05, s * 0.28, s * 0.90, s * 0.44);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(214, 214, 210)),
                                 new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), s * 0.03),
                                 concrete);

                var steel = new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), s * 0.05);
                double inset = s * 0.07;
                double left = concrete.Left + inset;
                double right = concrete.Right - inset;
                double top = concrete.Top + inset;
                double bottom = concrete.Bottom - inset;

                // Barres filantes, haute et basse.
                dc.DrawLine(steel, new Point(left, bottom), new Point(right, bottom));
                dc.DrawLine(steel, new Point(left, top), new Point(right, top));

                // Cadres : resserres aux appuis, espaces en travee.
                double[] levels = { 0.02, 0.10, 0.18, 0.36, 0.64, 0.82, 0.90, 0.98 };
                foreach (double level in levels)
                {
                    double x = left + (right - left) * level;
                    dc.DrawLine(steel, new Point(x, top), new Point(x, bottom));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Icone du bouton Footing : la semelle, son poteau et sa nappe.</summary>
        public static ImageSource CreateFootingIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                var fill = new SolidColorBrush(Color.FromRgb(214, 214, 210));
                var outline = new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), s * 0.03);

                // Semelle, puis amorce du poteau.
                var footing = new Rect(s * 0.05, s * 0.58, s * 0.90, s * 0.30);
                dc.DrawRectangle(fill, outline, footing);
                dc.DrawRectangle(fill, outline,
                    new Rect(s * 0.37, s * 0.12, s * 0.26, s * 0.46));

                var steel = new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), s * 0.05);
                double inset = s * 0.07;
                double y = footing.Bottom - inset;
                dc.DrawLine(steel, new Point(footing.Left + inset, y),
                            new Point(footing.Right - inset, y));

                // Attentes en L, ancrees dans la nappe et remontant dans le poteau.
                var starter = new Pen(new SolidColorBrush(Color.FromRgb(196, 46, 34)), s * 0.05);
                foreach (double side in new[] { -1.0, 1.0 })
                {
                    double x = s * 0.5 + side * s * 0.09;
                    dc.DrawLine(starter, new Point(x, y + s * 0.02), new Point(x, s * 0.16));
                    dc.DrawLine(starter, new Point(x, y + s * 0.02),
                                new Point(x - side * s * 0.10, y + s * 0.02));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Icone du bouton Slab : la dalle, ses nappes et ses appuis.</summary>
        public static ImageSource CreateSlabIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                var slab = new Rect(s * 0.05, s * 0.36, s * 0.90, s * 0.22);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(214, 214, 210)),
                                 new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), s * 0.03),
                                 slab);

                var steel = new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), s * 0.045);
                double inset = s * 0.06;
                double left = slab.Left + inset;
                double right = slab.Right - inset;

                // Nappe inferieure filante.
                dc.DrawLine(steel, new Point(left, slab.Bottom - inset),
                            new Point(right, slab.Bottom - inset));

                // Chapeaux sur les deux appuis, sur le quart de la portee.
                double quarter = (right - left) / 4.0;
                dc.DrawLine(steel, new Point(left, slab.Top + inset),
                            new Point(left + quarter, slab.Top + inset));
                dc.DrawLine(steel, new Point(right - quarter, slab.Top + inset),
                            new Point(right, slab.Top + inset));

                // Appuis triangules.
                var support = new Pen(new SolidColorBrush(Color.FromRgb(90, 90, 88)), s * 0.03);
                foreach (double x in new[] { slab.Left + s * 0.12, slab.Right - s * 0.12 })
                {
                    dc.DrawLine(support, new Point(x, slab.Bottom),
                                new Point(x - s * 0.07, slab.Bottom + s * 0.16));
                    dc.DrawLine(support, new Point(x, slab.Bottom),
                                new Point(x + s * 0.07, slab.Bottom + s * 0.16));
                    dc.DrawLine(support, new Point(x - s * 0.09, slab.Bottom + s * 0.16),
                                new Point(x + s * 0.09, slab.Bottom + s * 0.16));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Icone du bouton Wall : le voile, ses deux nappes et son epaisseur.</summary>
        public static ImageSource CreateWallIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                var wall = new Rect(s * 0.22, s * 0.06, s * 0.56, s * 0.88);
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(214, 214, 210)),
                                 new Pen(new SolidColorBrush(Color.FromRgb(120, 120, 118)), s * 0.03),
                                 wall);

                var steel = new Pen(new SolidColorBrush(Color.FromRgb(28, 62, 122)), s * 0.04);
                double inset = s * 0.08;

                // Aciers verticaux, trois files.
                for (int i = 0; i < 3; i++)
                {
                    double x = wall.Left + inset + (wall.Width - 2 * inset) * i / 2.0;
                    dc.DrawLine(steel, new Point(x, wall.Top + inset),
                                new Point(x, wall.Bottom - inset));
                }

                // Aciers horizontaux, quatre lits.
                var horizontal = new Pen(new SolidColorBrush(Color.FromRgb(70, 130, 180)), s * 0.03);
                for (int i = 0; i < 4; i++)
                {
                    double y = wall.Top + inset + (wall.Height - 2 * inset) * i / 3.0;
                    dc.DrawLine(horizontal, new Point(wall.Left + inset, y),
                                new Point(wall.Right - inset, y));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Icone du bouton "A propos".</summary>
        public static ImageSource CreateInfoIcon(int size)
        {
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                double s = size;
                var brush = new SolidColorBrush(Color.FromRgb(38, 108, 176));
                dc.DrawEllipse(brush, null, new Point(s / 2, s / 2), s * 0.42, s * 0.42);
                var white = new Pen(Brushes.White, s * 0.12);
                dc.DrawLine(white, new Point(s / 2, s * 0.42), new Point(s / 2, s * 0.72));
                dc.DrawEllipse(Brushes.White, null, new Point(s / 2, s * 0.29), s * 0.065, s * 0.065);
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
