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
                // Le batiment entier tient dans le cadre (sphere englobante).
                Assert.True(d / Math.Sqrt(2) >= (max - min).Length);
                // L'oeil regarde le centre.
                var eye = Sim4DCamera.Eye(min, max, o.Forward);
                var toCenter = c - eye;
                Assert.Equal(toCenter.Length, toCenter.Dot(o.Forward), 6);
            }
        }
    }
}
