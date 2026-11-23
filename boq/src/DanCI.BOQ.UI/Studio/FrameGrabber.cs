using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace DanCI.BOQ.UI.Studio
{
    /// <summary>
    /// Capture d'une image de la vue 3D de Revit pour la video du Studio 4D : la fenetre de la vue
    /// est retrouvee sous le centre du rectangle de la vue, puis copiee (PrintWindow, meme si une
    /// autre fenetre la recouvre ; a defaut, copie de l'ecran). La date est incrustee en grand en
    /// bas a gauche et une barre d'avancement en haut, comme dans les videos 4D de chantier.
    /// </summary>
    public sealed class FrameGrabber
    {
        private int outW, outH;

        public int Width => outW;
        public int Height => outH;

        /// <summary>Image JPEG de la vue (rectangle ecran de la vue), ou null si la capture echoue.</summary>
        public byte[] Capture(IntPtr revitMain, int left, int top, int right, int bottom, string date, double progress, string title)
        {
            int w = right - left, h = bottom - top;
            if (w < 16 || h < 16) return null;
            BitmapSource shot = PrintView(revitMain, left, top, w, h) ?? CopyScreen(left, top, w, h);
            if (shot == null) return null;
            if (outW == 0)
            {
                // Taille fixe de la video (paire, au plus 1280 de large).
                double k = Math.Min(1.0, 1280.0 / w);
                outW = Math.Max(16, (int)(w * k) / 2 * 2);
                outH = Math.Max(16, (int)(h * k) / 2 * 2);
            }
            var dv = new DrawingVisual();
            using (DrawingContext dc = dv.RenderOpen())
            {
                dc.DrawRectangle(Brushes.Black, null, new Rect(0, 0, outW, outH));
                dc.DrawImage(shot, new Rect(0, 0, outW, outH));
                // Barre d'avancement en haut.
                dc.DrawRectangle(new SolidColorBrush(Color.FromArgb(150, 7, 13, 26)), null, new Rect(0, 0, outW, 8));
                dc.DrawRectangle(new SolidColorBrush(Color.FromRgb(0, 229, 255)), null, new Rect(0, 0, outW * Math.Max(0, Math.Min(1, progress)), 8));
                // Date en grand en bas a gauche.
                var face = new Typeface(new FontFamily("Segoe UI"), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                double size = Math.Max(16, outH / 18.0);
                var text = new FormattedText(date ?? string.Empty, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight,
                    face, size, Brushes.White, 1.0);
                var sub = new FormattedText(title ?? string.Empty, System.Globalization.CultureInfo.GetCultureInfo("fr-FR"), FlowDirection.LeftToRight,
                    new Typeface("Segoe UI"), size * 0.42, new SolidColorBrush(Color.FromRgb(124, 242, 255)), 1.0);
                double bx = 18, bh = text.Height + sub.Height + 18, by = outH - bh - 18;
                dc.DrawRoundedRectangle(new SolidColorBrush(Color.FromArgb(200, 11, 31, 58)), new Pen(new SolidColorBrush(Color.FromRgb(0, 229, 255)), 1.5),
                    new Rect(bx, by, Math.Max(text.Width, sub.Width) + 28, bh), 6, 6);
                dc.DrawText(sub, new Point(bx + 14, by + 7));
                dc.DrawText(text, new Point(bx + 14, by + 9 + sub.Height));
            }
            var rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            var enc = new JpegBitmapEncoder { QualityLevel = 88 };
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var ms = new MemoryStream())
            {
                enc.Save(ms);
                return ms.ToArray();
            }
        }

        private static BitmapSource PrintView(IntPtr main, int left, int top, int w, int h)
        {
            if (main == IntPtr.Zero) return null;
            try
            {
                // Fenetre la plus profonde de Revit sous le centre de la vue.
                var center = new POINT { X = left + w / 2, Y = top + h / 2 };
                IntPtr hwnd = main;
                for (int i = 0; i < 32; i++)
                {
                    POINT p = center;
                    ScreenToClient(hwnd, ref p);
                    IntPtr child = ChildWindowFromPointEx(hwnd, p, 0x0001 | 0x0004); // invisibles et transparentes ignorees
                    if (child == IntPtr.Zero || child == hwnd) break;
                    hwnd = child;
                }
                if (!GetWindowRect(hwnd, out RECT wr)) return null;
                int ww = wr.Right - wr.Left, wh = wr.Bottom - wr.Top;
                if (ww < 16 || wh < 16) return null;
                BitmapSource full = Grab(ww, wh, (dc) => PrintWindow(hwnd, dc, 2 /* PW_RENDERFULLCONTENT */));
                if (full == null || IsBlank(full)) return null;
                int cx = Math.Max(0, left - wr.Left), cy = Math.Max(0, top - wr.Top);
                int cw = Math.Min(w, ww - cx), ch = Math.Min(h, wh - cy);
                if (cw < 16 || ch < 16) return full;
                return new CroppedBitmap(full, new Int32Rect(cx, cy, cw, ch));
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BitmapSource CopyScreen(int left, int top, int w, int h)
        {
            try
            {
                return Grab(w, h, (dc) =>
                {
                    IntPtr screen = GetDC(IntPtr.Zero);
                    try { return BitBlt(dc, 0, 0, w, h, screen, left, top, 0x00CC0020 /* SRCCOPY */); }
                    finally { ReleaseDC(IntPtr.Zero, screen); }
                });
            }
            catch (Exception)
            {
                return null;
            }
        }

        private static BitmapSource Grab(int w, int h, Func<IntPtr, bool> draw)
        {
            IntPtr screen = GetDC(IntPtr.Zero);
            IntPtr mem = CreateCompatibleDC(screen);
            IntPtr bmp = CreateCompatibleBitmap(screen, w, h);
            IntPtr old = SelectObject(mem, bmp);
            try
            {
                if (!draw(mem)) return null;
                SelectObject(mem, old);
                BitmapSource src = Imaging.CreateBitmapSourceFromHBitmap(bmp, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                src.Freeze();
                return src;
            }
            finally
            {
                DeleteObject(bmp);
                DeleteDC(mem);
                ReleaseDC(IntPtr.Zero, screen);
            }
        }

        /// <summary>Image toute noire (PrintWindow non supporte par la vue) : on passe a la copie d'ecran.</summary>
        private static bool IsBlank(BitmapSource b)
        {
            var conv = new FormatConvertedBitmap(b, PixelFormats.Bgra32, null, 0);
            int stride = conv.PixelWidth * 4;
            var px = new byte[stride * conv.PixelHeight];
            conv.CopyPixels(px, stride, 0);
            for (int i = 0; i < px.Length; i += 4 * 97)
            {
                if (px[i] > 8 || px[i + 1] > 8 || px[i + 2] > 8) return false;
            }
            return true;
        }

        [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
        [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] private static extern IntPtr ChildWindowFromPointEx(IntPtr parent, POINT pt, uint flags);
        [DllImport("user32.dll")] private static extern bool ScreenToClient(IntPtr hwnd, ref POINT pt);
        [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
        [DllImport("user32.dll")] private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdc, uint flags);
        [DllImport("user32.dll")] private static extern IntPtr GetDC(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int w, int h);
        [DllImport("gdi32.dll")] private static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
        [DllImport("gdi32.dll")] private static extern bool DeleteDC(IntPtr hdc);
        [DllImport("gdi32.dll")] private static extern bool BitBlt(IntPtr dst, int x, int y, int w, int h, IntPtr src, int sx, int sy, uint rop);
    }
}
