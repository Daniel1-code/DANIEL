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
