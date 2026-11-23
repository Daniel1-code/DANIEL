using System.Linq;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class Build4DTests
    {
        private static Build4DItem Wall(long id, double z = 0) => new Build4DItem
        {
            Id = id, Rank = Build4D.Rank("OST_Walls"), Zb = z, Courses = true,
            Shape = { Build4D.Box(0, 0, z, 10, 0.66, z + 9.84) } // mur 3,05 m x 0,20 m, 3 m de haut
        };

        [Fact]
        public void Mur_monte_rang_par_rang_puis_brique_par_brique()
        {
            Build4DItem w = Wall(1);
            int n = (int)System.Math.Round(9.84 / Build4D.CourseFeet); // 15 assises
            Assert.Empty(Build4D.Partial(w, 0));
            var half = Build4D.Partial(w, 0.5);
            // 7 assises completes + l'assise en cours, posee sur une partie de la longueur
            Assert.Equal(n / 2 + 1, half.Count);
            var last = half.Last();
            double len = last.Bottom.Where((v, i) => i % 2 == 0).Max() - last.Bottom.Where((v, i) => i % 2 == 0).Min();
            Assert.True(len > 0 && len < 10, "assise partielle : " + len);
            Assert.True(half.Take(half.Count - 1).All(p => System.Math.Abs((p.Bottom[2] - p.Bottom[0]) - 10) < 1e-9));
            // Fini : toutes les assises, jusqu'en haut (moins le joint)
            var done = Build4D.Partial(w, 1);
            Assert.Equal(n, done.Count);
            Assert.True(done.Max(p => p.Zt) > 9.84 - 0.05);
            // Les assises montent : chaque rang au-dessus du precedent
            for (int i = 1; i < done.Count; i++) Assert.True(done[i].Zb > done[i - 1].Zb);
        }

        [Fact]
        public void Dalle_coulee_en_longueur_et_poteau_qui_monte()
        {
            var slab = new Build4DItem { Id = 2, Rank = Build4D.Rank("OST_Floors"), Shape = { Build4D.Box(0, 0, 10, 30, 12, 10.66) } };
            var p = Build4D.Partial(slab, 0.25).Single();
            Assert.Equal(7.5, p.Bottom.Where((v, i) => i % 2 == 0).Max(), 6);      // 25 % de la plus grande longueur (30)
            Assert.Equal(12, p.Bottom.Where((v, i) => i % 2 == 1).Max(), 6);        // toute la largeur
            var col = new Build4DItem { Id = 3, Rank = Build4D.Rank("OST_StructuralColumns"), Shape = { Build4D.Box(0, 0, 0, 1, 1, 10) } };
            Assert.Equal(4, Build4D.Partial(col, 0.4).Single().Zt, 6);
        }

        [Fact]
        public void Ordre_de_chantier_dans_une_tache_bas_vers_haut_puis_categorie()
        {
            var col = new Build4DItem { Id = 10, Rank = Build4D.Rank("OST_StructuralColumns"), Zb = 0 };
            var beam = new Build4DItem { Id = 11, Rank = Build4D.Rank("OST_StructuralFraming"), Zb = 0 };
            var slab = new Build4DItem { Id = 12, Rank = Build4D.Rank("OST_Floors"), Zb = 0 };
            var upper = new Build4DItem { Id = 13, Rank = Build4D.Rank("OST_StructuralColumns"), Zb = 10 };
            var items = new[] { slab, upper, beam, col };
            var p0 = Build4D.Schedule(items, 0.1);
            Assert.True(p0[10] > 0 && p0[11] == 0 && p0[12] == 0 && p0[13] == 0);   // les poteaux d'abord
            var p1 = Build4D.Schedule(items, 0.5);
            Assert.Equal(1, p1[10]); Assert.Equal(1, p1[11]); Assert.Equal(0, p1[12]);
            var p2 = Build4D.Schedule(items, 1);
            Assert.All(p2.Values, v => Assert.Equal(1, v));
            // Tous les murs d'un niveau montent ensemble (meme groupe).
            var walls = Enumerable.Range(1, 8).Select(i => Wall(i)).ToList();
            var pw = Build4D.Schedule(walls, 0.3);
            Assert.All(pw.Values, v => Assert.Equal(0.3, v, 9));
        }
    }
}
