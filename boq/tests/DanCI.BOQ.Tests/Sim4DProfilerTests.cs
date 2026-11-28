using System.Linq;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class Sim4DProfilerTests
    {
        /// <summary>Attente active precise (Thread.Sleep a une granularite de ~15 ms sous Windows).</summary>
        private static void Spin(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.Elapsed.TotalMilliseconds < ms) { }
        }

        [Fact]
        public void Releve_par_etape_moyenne_et_resume()
        {
            var p = new Sim4DProfiler();
            for (int i = 0; i < 3; i++)
            {
                p.Start();
                Spin(2);
                p.Mark("préparation");
                Spin(40);
                p.Mark("rotation");
                p.End();
            }
            Assert.Equal(3, p.Frames);
            var avg = p.Averages();
            Assert.Equal(new[] { "préparation", "rotation" }, avg.Select(a => a.Key));
            Assert.True(avg[1].Value > avg[0].Value);
            Assert.StartsWith("Revit : rotation", p.Summary());
            p.Reset();
            Assert.Empty(p.Averages());
        }
    }
}
