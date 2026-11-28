using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DanCI.BOQ.Core.Video
{
    /// <summary>
    /// Fichier video AVI « Motion JPEG » ecrit image par image (une image JPEG par trame), sans
    /// bibliotheque externe : lisible par VLC, le Lecteur Windows Media, PowerPoint, et convertible
    /// en MP4 par n'importe quel outil. Les tailles des en-tetes sont completees a la fermeture.
    /// </summary>
    public sealed class AviWriter : IDisposable
    {
        private readonly Stream s;
        private readonly BinaryWriter w;
        private readonly int width, height, fps;
        private readonly List<(long Offset, int Size)> index = new List<(long, int)>();
        private long riffSizePos, avihFramesPos, strhLengthPos, moviSizePos, moviStart, bufferPos, strhBufferPos;
        private int maxFrame;
        private bool closed;

        public int Frames => index.Count;
        public string Path { get; }

        public AviWriter(string path, int width, int height, int fps) : this(new FileStream(path, FileMode.Create, FileAccess.ReadWrite), width, height, fps)
        {
            Path = path;
        }

        public AviWriter(Stream stream, int width, int height, int fps)
        {
            if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
            s = stream;
            w = new BinaryWriter(s, Encoding.ASCII, leaveOpen: false);
            this.width = width;
            this.height = height;
            this.fps = Math.Max(1, Math.Min(60, fps));
            WriteHeaders();
        }

        private void Four(string cc) => w.Write(Encoding.ASCII.GetBytes(cc));

        private void WriteHeaders()
        {
            Four("RIFF"); riffSizePos = s.Position; w.Write(0); Four("AVI ");
            Four("LIST"); w.Write(4 + 8 + 56 + 8 + 4 + 8 + 56 + 8 + 40); Four("hdrl");
            Four("avih"); w.Write(56);
            w.Write(1000000 / fps);          // microsecondes par image
            w.Write(0);                      // debit max
            w.Write(0);                      // granularite
            w.Write(0x10);                   // AVIF_HASINDEX
            avihFramesPos = s.Position; w.Write(0); // nombre d'images (complete a la fin)
            w.Write(0);                      // images initiales
            w.Write(1);                      // un flux
            bufferPos = s.Position; w.Write(0);     // taille de tampon suggeree
            w.Write(width); w.Write(height);
            w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            Four("LIST"); w.Write(4 + 8 + 56 + 8 + 40); Four("strl");
            Four("strh"); w.Write(56);
            Four("vids"); Four("MJPG");
            w.Write(0); w.Write((short)0); w.Write((short)0); w.Write(0);
            w.Write(1); w.Write(fps);        // echelle / cadence
            w.Write(0);                      // debut
            strhLengthPos = s.Position; w.Write(0); // longueur (images)
            strhBufferPos = s.Position; w.Write(0);
            w.Write(-1);                     // qualite
            w.Write(0);                      // taille d'echantillon
            w.Write((short)0); w.Write((short)0); w.Write((short)width); w.Write((short)height);
            Four("strf"); w.Write(40);
            w.Write(40); w.Write(width); w.Write(height); w.Write((short)1); w.Write((short)24);
            Four("MJPG"); w.Write(width * height * 3); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
            Four("LIST"); moviSizePos = s.Position; w.Write(0); moviStart = s.Position; Four("movi");
        }

        /// <summary>Ajoute une image JPEG (toutes les images doivent avoir la taille annoncee).</summary>
        public void AddFrame(byte[] jpeg)
        {
            if (closed) throw new InvalidOperationException("Vidéo déjà fermée.");
            if (jpeg == null || jpeg.Length == 0) return;
            long offset = s.Position - moviStart;
            Four("00dc"); w.Write(jpeg.Length); w.Write(jpeg);
            if ((jpeg.Length & 1) == 1) w.Write((byte)0);
            index.Add((offset, jpeg.Length));
            maxFrame = Math.Max(maxFrame, jpeg.Length);
        }

        /// <summary>Ecrit l'index et complete les tailles. Appele aussi par Dispose.</summary>
        public void Close()
        {
            if (closed) return;
            closed = true;
            long moviEnd = s.Position;
            Four("idx1"); w.Write(index.Count * 16);
            foreach (var (offset, size) in index)
            {
                Four("00dc"); w.Write(0x10); w.Write((int)offset); w.Write(size);
            }
            long end = s.Position;
            Patch(riffSizePos, (int)(end - 8));
            Patch(moviSizePos, (int)(moviEnd - moviStart));
            Patch(avihFramesPos, index.Count);
            Patch(strhLengthPos, index.Count);
            Patch(bufferPos, maxFrame + 8);
            Patch(strhBufferPos, maxFrame + 8);
            s.Position = end;
            w.Flush();
            w.Dispose();
        }

        private void Patch(long pos, int value)
        {
            s.Position = pos;
            w.Write(value);
        }

        public void Dispose() => Close();
    }
}
