using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanCI.BOQ.Addin.Resources
{
    /// <summary>
    /// Icones du ruban dessinees en code (pas de fichier image a embarquer ni a perdre).
    /// Motif : une feuille de bordereau, en-tete colore et lignes de quantites.
    /// </summary>
    internal static class IconFactory
    {
        private static readonly Color DanciBlue = Color.FromRgb(0x1F, 0x4E, 0x79);
        private static readonly Color DanciOrange = Color.FromRgb(0xE0, 0x7B, 0x00);

        /// <summary>Bordereau et loupe : controle du metre.</summary>
        public static BitmapSource CreateDiagnosticIcon(int size)
        {
            double s = size;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var blue = new SolidColorBrush(DanciBlue);
                var sheet = new Rect(s * 0.08, s * 0.06, s * 0.6, s * 0.8);
                dc.DrawRectangle(Brushes.White, new Pen(blue, s / 16.0), sheet);
                var line = new Pen(blue, s / 24.0);
                for (int i = 0; i < 4; i++)
                {
                    double y = s * (0.22 + i * 0.13);
                    dc.DrawLine(line, new Point(s * 0.16, y), new Point(s * 0.58, y));
                }
                var lens = new Pen(new SolidColorBrush(DanciOrange), s / 10.0);
                dc.DrawEllipse(Brushes.White, lens, new Point(s * 0.62, s * 0.62), s * 0.17, s * 0.17);
                dc.DrawLine(lens, new Point(s * 0.75, s * 0.75), new Point(s * 0.94, s * 0.94));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>Cercle « i » : informations.</summary>
        public static BitmapSource CreateInfoIcon(int size)
        {
            double s = size;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawEllipse(new SolidColorBrush(DanciBlue), null, new Point(s / 2, s / 2), s * 0.44, s * 0.44);
                var white = new Pen(Brushes.White, s / 8.0);
                dc.DrawEllipse(Brushes.White, null, new Point(s / 2, s * 0.28), s * 0.07, s * 0.07);
                dc.DrawLine(white, new Point(s / 2, s * 0.44), new Point(s / 2, s * 0.76));
            }
            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public static BitmapSource CreateBoqIcon(int size)
        {
            double s = size;
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                var sheet = new Rect(s * 0.16, s * 0.06, s * 0.68, s * 0.88);
                dc.DrawRectangle(Brushes.White, new Pen(new SolidColorBrush(DanciBlue), s / 16.0), sheet);

                // En-tete du bordereau.
                dc.DrawRectangle(new SolidColorBrush(DanciBlue), null,
                    new Rect(sheet.X, sheet.Y, sheet.Width, s * 0.18));

                // Lignes de quantites et colonne des montants.
                var line = new Pen(new SolidColorBrush(DanciBlue), s / 24.0);
                for (int i = 0; i < 4; i++)
                {
                    double y = s * (0.38 + i * 0.13);
                    dc.DrawLine(line, new Point(s * 0.24, y), new Point(s * 0.56, y));
                }
                var amount = new Pen(new SolidColorBrush(DanciOrange), s / 16.0);
                for (int i = 0; i < 4; i++)
                {
                    double y = s * (0.38 + i * 0.13);
                    dc.DrawLine(amount, new Point(s * 0.62, y), new Point(s * 0.76, y));
                }
            }

            var bitmap = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }
    }
}
