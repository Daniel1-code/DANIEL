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
            var p1 = Build4D.Schedule(items, 0.6);
            Assert.Equal(1, p1[10]); Assert.Equal(1, p1[11]);
            Assert.True(p1[12] > 0 && p1[12] < 1);                                      // dalle en cours (fenetres chevauchees)
            Assert.Equal(0, p1[13]);
            // Chaque groupe dure au moins 40 % de la tache : jamais construit d'un coup.
            foreach (var it in items)
            {
                double first = -1, last = -1;
                for (int k = 0; k <= 1000; k++)
                {
                    double v = Build4D.Schedule(items, k / 1000.0)[it.Id];
                    if (v > 0 && first < 0) first = k / 1000.0;
                    if (v >= 1 && last < 0) last = k / 1000.0;
                }
                Assert.True(last - first >= Build4D.MinGroupWindow - 0.01, it.Id + " : " + first + " -> " + last);
            }
            var p2 = Build4D.Schedule(items, 1);
            Assert.All(p2.Values, v => Assert.Equal(1, v));
            // Tous les murs d'un niveau montent ensemble (meme groupe).
            var walls = Enumerable.Range(1, 8).Select(i => Wall(i)).ToList();
            var pw = Build4D.Schedule(walls, 0.3);
            Assert.All(pw.Values, v => Assert.Equal(0.3, v, 9));
        }

        [Fact]
        public void Aucun_saut_en_lecture_rapide_recalage_quand_on_recule()
        {
            // Tache finie entre deux images (pas trop grand) : l'affichage monte quand meme sur 12 images.
            double shown = 0;
            int frames = 0;
            while (shown < 1) { shown = Build4D.Ease(shown, 1, playing: true); frames++; }
            Assert.Equal(Build4D.MinFramesPerTask, frames);
            // Jamais plus que l'avancement reel.
            Assert.Equal(0.05, Build4D.Ease(0, 0.05, true), 9);
            // Recul ou curseur deplace a la main : recalage direct.
            Assert.Equal(0.2, Build4D.Ease(0.9, 0.2, true), 9);
            Assert.Equal(1, Build4D.Ease(0, 1, false), 9);
        }
    }
}
