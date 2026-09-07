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
