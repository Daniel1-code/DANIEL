using System;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class Sim4DCameraTests
    {
        [Fact]
        public void Cadrage_identique_a_tous_les_angles_de_rotation()
        {
            var min = new Sim4DCamera.Vec(0, 0, 0);
            var max = new Sim4DCamera.Vec(40, 15, 12); // batiment allonge : le cas ou l'ancien zoom sautait
            var c = (min + max) * 0.5;
            double? size = null;
            double maxFill = 0;
            for (double a = 0; a < 360; a += 6)
            {
                var o = Sim4DCamera.Orientation(a);
                Assert.Equal(0, o.Forward.Dot(o.Up), 9);                    // repere orthogonal
                Assert.Equal(1, o.Forward.Length, 9);
                var z = Sim4DCamera.ZoomCorners(min, max, o.Forward, o.Up);
                Assert.Equal(0, (z.A - c).Dot(o.Forward), 9);              // coins dans le plan de l'ecran
                Assert.Equal(0, (z.B - c).Dot(o.Forward), 9);
                double d = (z.B - z.A).Length;
                if (size == null) size = d; else Assert.Equal(size.Value, d, 9); // meme taille : pas de saut de zoom
                // Les 8 coins du batiment tiennent dans le cadre a cet angle (projection sur l'ecran).
                var right = o.Forward.Cross(o.Up);
                double halfW = Math.Abs((z.B - c).Dot(right)), halfH = Math.Abs((z.B - c).Dot(o.Up));
                double fillW = 0, fillH = 0;
                foreach (double x in new[] { min.X, max.X })
                foreach (double y in new[] { min.Y, max.Y })
                foreach (double h in new[] { min.Z, max.Z })
                {
                    var p = new Sim4DCamera.Vec(x, y, h) - c;
                    Assert.True(Math.Abs(p.Dot(right)) <= halfW + 1e-9);
                    Assert.True(Math.Abs(p.Dot(o.Up)) <= halfH + 1e-9);
                    fillW = Math.Max(fillW, Math.Abs(p.Dot(right)) / halfW);
                    fillH = Math.Max(fillH, Math.Abs(p.Dot(o.Up)) / halfH);
                }
                maxFill = Math.Max(maxFill, Math.Max(fillW, fillH));
                // L'oeil regarde le centre.
                var eye = Sim4DCamera.Eye(min, max, o.Forward);
                var toCenter = c - eye;
                Assert.Equal(toCenter.Length, toCenter.Dot(o.Forward), 6);
            }
            // A l'angle le plus large, le batiment remplit le cadre (marge 6 %) : il occupe l'ecran.
            Assert.True(maxFill > 0.9, "remplissage " + maxFill);
        }

        [Fact]
        public void Fin_toujours_sur_la_face_avant_du_ViewCube_en_douceur()
        {
            foreach (double start in new[] { 0.0, 45, 200, 300, 450, -120 })
            {
                var end = Sim4DCamera.Landing(start, 1);
                Assert.Equal(0, end.ElevationDeg, 9);                                  // a l'horizontale
                Assert.Equal(0, ((end.AngleDeg - Sim4DCamera.FrontAngleDeg) % 360 + 360) % 360, 6); // face Avant
                var o = Sim4DCamera.Orientation(end.AngleDeg, end.ElevationDeg);
                Assert.Equal(0, o.Forward.X, 6); Assert.Equal(1, o.Forward.Y, 6); Assert.Equal(0, o.Forward.Z, 6); // regard vers +Y
                Assert.Equal(1, o.Up.Z, 6);
                // Chemin le plus court (au plus un demi-tour) et mouvement doux, sans saut.
                Assert.True(System.Math.Abs(end.AngleDeg - start) <= 180 + 1e-9);
                double prev = start;
                for (int i = 1; i <= Sim4DCamera.LandingFrames; i++)
                {
                    var p = Sim4DCamera.Landing(start, i / (double)Sim4DCamera.LandingFrames);
                    Assert.True(System.Math.Abs(p.AngleDeg - prev) <= 12, "saut de " + (p.AngleDeg - prev));
                    prev = p.AngleDeg;
                }
                Assert.Equal(start, Sim4DCamera.Landing(start, 0).AngleDeg, 9);
            }
        }
    }
}
