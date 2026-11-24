using System;
using System.Linq;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class MeshClipTests
    {
        /// <summary>Boite fermee, faces orientees vers l'exterieur.</summary>
        internal static TriMesh Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var c = new[]
            {
                new V3(x0, y0, z0), new V3(x1, y0, z0), new V3(x1, y1, z0), new V3(x0, y1, z0),
                new V3(x0, y0, z1), new V3(x1, y0, z1), new V3(x1, y1, z1), new V3(x0, y1, z1)
            };
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            var m = new TriMesh();
            foreach (var q in f) { m.Add(c[q[0]], c[q[1]], c[q[2]]); m.Add(c[q[0]], c[q[2]], c[q[3]]); }
            return m;
        }

        /// <summary>Volume d'un maillage ferme (theoreme de la divergence).</summary>
        internal static double Volume(TriMesh m)
        {
            double v = 0;
            for (int i = 0; i + 2 < m.Points.Count; i += 3) v += m.Points[i].Dot(m.Points[i + 1].Cross(m.Points[i + 2])) / 6;
            return v;
        }

        [Fact]
        public void Coupe_horizontale_fermee_garde_le_bon_volume()
        {
            TriMesh wall = Box(0, 0, 0, 10, 0.66, 9.84); // mur 3,05 m x 0,20 m x 3,00 m
            Assert.Equal(10 * 0.66 * 9.84, Volume(wall), 6);
            TriMesh lower = MeshClip.Keep(wall, new V3(0, 0, 1), 4.0);
            Assert.Equal(10 * 0.66 * 4.0, Volume(lower), 6); // couvercle present : volume exact
            Assert.True(lower.Points.All(p => p.Z <= 4.0 + 1e-9));
        }

        [Fact]
        public void Coupe_oblique_et_en_longueur()
        {
            TriMesh slab = Box(0, 0, 0, 20, 10, 0.66);
            TriMesh half = MeshClip.Keep(slab, new V3(1, 0, 0), 5);
            Assert.Equal(5 * 10 * 0.66, Volume(half), 6);
            TriMesh none = MeshClip.Keep(slab, new V3(0, 0, 1), -1);
            Assert.True(none.IsEmpty);
            TriMesh all = MeshClip.Keep(slab, new V3(0, 0, 1), 5);
            Assert.Equal(slab.TriangleCount, all.TriangleCount);
        }

        [Fact]
        public void Mur_avec_ouverture_coupe_a_hauteur_de_fenetre()
        {
            // Deux trumeaux separes par une ouverture : la coupe donne deux boucles, deux couvercles.
            var wall = new TriMesh();
            foreach (var b in new[] { Box(0, 0, 0, 4, 0.66, 9.84), Box(6, 0, 0, 10, 0.66, 9.84) }) wall.Points.AddRange(b.Points);
            TriMesh lower = MeshClip.Keep(wall, new V3(0, 0, 1), 5);
            Assert.Equal(8 * 0.66 * 5, Volume(lower), 6);
        }

        [Fact]
        public void Montee_par_rangs_vraie_geometrie()
        {
            TriMesh wall = Box(0, 0, 0, 10, 0.66, 15 * Build4D.CourseFeet); // 15 rangs
            var item = new Build4DItem { Id = 1, Courses = true, Shape = { Build4D.Box(0, 0, 0, 10, 0.66, 15 * Build4D.CourseFeet) } };
            Assert.Empty(Build4DMesh.Partial(item, wall, 0));
            var parts = Build4DMesh.Partial(item, wall, (5 + 0.5) / 15.0); // 5 rangs + moitie du 6e
            Assert.Equal(2, parts.Count);
            Assert.Equal(10 * 0.66 * 5 * Build4D.CourseFeet, Volume(parts[0].Mesh), 6);
            Assert.True(parts[1].Fresh);
            Assert.Equal(5 * 0.66 * Build4D.CourseFeet, Volume(parts[1].Mesh), 6); // rang en cours pose sur la moitie de la longueur
            var done = Build4DMesh.Partial(item, wall, 1);
            Assert.Single(done);
            Assert.Equal(Volume(wall), Volume(done[0].Mesh), 6);
        }

        [Fact]
        public void Dalle_coulee_en_longueur()
        {
            TriMesh slab = Box(0, 0, 0, 20, 10, 0.66);
            var item = new Build4DItem { Id = 2, Courses = true, Shape = { Build4D.Box(0, 0, 0, 20, 10, 0.66) } };
            var parts = Build4DMesh.Partial(item, slab, 0.25);
            Assert.Single(parts);
            Assert.Equal(5 * 10 * 0.66, Volume(parts[0].Mesh), 6);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    using DanCI.BOQ.Core.Planning;
    using Xunit;

    public class MeshClipCollinearTests
    {
        [Fact]
        public void Faces_decoupees_en_plusieurs_triangles_la_coupe_reste_fermee()
        {
            // Mur dont la grande face avant est decoupee en deux (point milieu sur les aretes, comme Revit
            // autour d'une jonction) : la section porte un point aligne au milieu d'un cote.
            TriMesh box = MeshClipTests.Box(0, 0, 0, 10, 0.66, 9.84);
            var m = new TriMesh();
            for (int i = 0; i + 2 < box.Points.Count; i += 3)
            {
                V3 a = box.Points[i], b = box.Points[i + 1], c = box.Points[i + 2];
                bool front = a.Y == 0 && b.Y == 0 && c.Y == 0;
                if (!front) { m.Add(a, b, c); continue; }
            }
            // Face avant (y = 0) refaite en 4 triangles avec les milieux x = 5.
            V3 p0 = new V3(0, 0, 0), p1 = new V3(5, 0, 0), p2 = new V3(10, 0, 0), q0 = new V3(0, 0, 9.84), q1 = new V3(5, 0, 9.84), q2 = new V3(10, 0, 9.84);
            m.Add(p0, p1, q1); m.Add(p0, q1, q0); m.Add(p1, p2, q2); m.Add(p1, q2, q1);
            Assert.Equal(10 * 0.66 * 9.84, MeshClipTests.Volume(m), 6);
            TriMesh lower = MeshClip.Keep(m, new V3(0, 0, 1), 3);
            Assert.Equal(10 * 0.66 * 3, MeshClipTests.Volume(lower), 6);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    using System;
    using DanCI.BOQ.Core.Planning;
    using Xunit;

    public class MeshClipSpeedTests
    {
        /// <summary>Poteau rond (ou mur courbe) a <paramref name="sides"/> facettes, ferme.</summary>
        private static TriMesh Cylinder(int sides, double r, double h)
        {
            var m = new TriMesh();
            var c0 = new V3(0, 0, 0); var c1 = new V3(0, 0, h);
            for (int i = 0; i < sides; i++)
            {
                double a = 2 * Math.PI * i / sides, b = 2 * Math.PI * (i + 1) / sides;
                V3 p = new V3(r * Math.Cos(a), r * Math.Sin(a), 0), q = new V3(r * Math.Cos(b), r * Math.Sin(b), 0);
                V3 P = new V3(p.X, p.Y, h), Q = new V3(q.X, q.Y, h);
                m.Add(c0, q, p); m.Add(c1, P, Q); m.Add(p, q, Q); m.Add(p, Q, P);
            }
            return m;
        }

        [Fact]
        public void Section_ronde_de_360_cotes_fermee_et_rapide()
        {
            TriMesh col = Cylinder(360, 1, 10);
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TriMesh lower = MeshClip.Keep(col, new V3(0, 0, 1), 4);
            Assert.True(sw.ElapsedMilliseconds < 200, sw.ElapsedMilliseconds + " ms");
            Assert.Equal(MeshClipTests.Volume(col) * 0.4, MeshClipTests.Volume(lower), 6);
        }

        [Fact]
        public void Section_tres_decoupee_jamais_de_blocage()
        {
            TriMesh col = Cylinder(2000, 1, 10); // au-dela de MaxCapPoints : coupe laissee ouverte, sans attente
            var sw = System.Diagnostics.Stopwatch.StartNew();
            TriMesh lower = MeshClip.Keep(col, new V3(0, 0, 1), 4);
            Assert.True(sw.ElapsedMilliseconds < 500, sw.ElapsedMilliseconds + " ms");
            Assert.False(lower.IsEmpty);
        }
    }
}
