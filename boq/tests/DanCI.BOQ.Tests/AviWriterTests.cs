using System.IO;
using System.Linq;
using DanCI.BOQ.Core.Video;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class AviWriterTests
    {
        private static byte[] Jpeg(int i)
        {
            DanCI.BOQ.Export.Pdf.BoqPdfWriter.EnsureConfigured();
            return Document.Create(c => c.Page(p =>
            {
                p.Size(320, 180);
                p.Margin(0);
                p.PageColor(i % 2 == 0 ? "#0B1F3A" : "#1F4E79");
                p.Content().AlignCenter().AlignMiddle().Text("Jour " + i).FontSize(28).FontColor(Colors.White);
            })).GenerateImages(new QuestPDF.Infrastructure.ImageGenerationSettings
            {
                ImageFormat = QuestPDF.Infrastructure.ImageFormat.Jpeg, RasterDpi = 72, ImageCompressionQuality = QuestPDF.Infrastructure.ImageCompressionQuality.High
            }).First();
        }

        [Fact]
        public void Video_AVI_MJPEG_lisible()
        {
            string path = System.Environment.GetEnvironmentVariable("DANCI_KEEP_AVI") ?? Path.Combine(Path.GetTempPath(), "danci_" + System.Guid.NewGuid().ToString("N") + ".avi");
            using (var avi = new AviWriter(path, 320, 180, 8))
            {
                for (int i = 0; i < 12; i++) avi.AddFrame(Jpeg(i));
                Assert.Equal(12, avi.Frames);
            }
            byte[] b = File.ReadAllBytes(path);
            string head = System.Text.Encoding.ASCII.GetString(b, 0, 12);
            Assert.StartsWith("RIFF", head);
            Assert.EndsWith("AVI ", head);
            Assert.Equal(b.Length - 8, System.BitConverter.ToInt32(b, 4));                       // taille RIFF completee
            string all = System.Text.Encoding.ASCII.GetString(b);
            Assert.Contains("MJPG", all);
            Assert.Contains("idx1", all);
            Assert.Equal(12, System.Text.RegularExpressions.Regex.Matches(all, "00dc").Count - 12); // 12 trames + 12 entrees d'index
            if (System.Environment.GetEnvironmentVariable("DANCI_KEEP_AVI") == null) File.Delete(path);
        }
    }
}
