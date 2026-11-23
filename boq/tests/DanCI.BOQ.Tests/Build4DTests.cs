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
            // Un evenement en double pour la meme date ne fait PAS monter une seconde fois (une montee par pas).
            double a = Build4D.Next(0, 1, Build4D.Follow.Advance);
            Assert.Equal(a, Build4D.Next(a, 1, Build4D.Follow.Hold), 9);
            Assert.Equal(1.0 / Build4D.MinFramesPerTask, a, 9);
            // Date deplacee a la main : recalage direct ; recul en Hold : on ne depasse jamais la date.
            Assert.Equal(0.7, Build4D.Next(0.1, 0.7, Build4D.Follow.Snap), 9);
            Assert.Equal(0.2, Build4D.Next(0.9, 0.2, Build4D.Follow.Hold), 9);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class Build4DStateTests
    {
        private static DanCI.BOQ.Core.Planning.PlanTask Task(string id, int start, params long[] ids)
        {
            var t = new DanCI.BOQ.Core.Planning.PlanTask { Id = id, StartDay = start, DurationDays = 10, Color = "#" + id };
            foreach (long i in ids) t.Elements.Add(new DanCI.BOQ.Core.Model.ElementRef(i));
            return t;
        }

        [Fact]
        public void Mur_construit_par_la_maconnerie_jamais_reconstruit_par_l_enduit()
        {
            var mason = Task("05", 0, 1, 2);
            var plaster = Task("08", 10, 1, 2, 3);       // l'enduit contient les memes murs (+ une piece 3)
            var tasks = new[] { plaster, mason };
            var owners = DanCI.BOQ.Core.Planning.Build4DState.Owners(tasks);
            Assert.Equal("05", owners[1]);
            Assert.Equal("08", owners[3]);
            // Maconnerie en cours : les murs montent (construits par 05), l'enduit n'a pas commence.
            var f1 = DanCI.BOQ.Core.Planning.Build4DState.Compute(tasks, new System.Collections.Generic.Dictionary<string, double> { ["05"] = 0.5, ["08"] = 0 }, owners);
            Assert.Equal(new long[] { 1, 2 }, f1.Building.Single().Elements.OrderBy(x => x));
            // Enduit en cours : les murs restent DEBOUT (colores), pas reconstruits ; seule la piece 3 se construit.
            var f2 = DanCI.BOQ.Core.Planning.Build4DState.Compute(tasks, new System.Collections.Generic.Dictionary<string, double> { ["05"] = 1, ["08"] = 0.3 }, owners);
            Assert.Equal("#08", f2.Running[1]);
            Assert.Equal(new long[] { 3 }, f2.Building.Single().Elements);
            Assert.DoesNotContain(f2.Building, b => b.Elements.Contains(1));
            // Tout fini : tout est construit, rien en cours.
            var f3 = DanCI.BOQ.Core.Planning.Build4DState.Compute(tasks, new System.Collections.Generic.Dictionary<string, double> { ["05"] = 1, ["08"] = 1 }, owners);
            Assert.Equal(new long[] { 1, 2, 3 }, f3.Built.OrderBy(x => x));
            Assert.Empty(f3.Running);
        }
    
        [Fact]
        public void Mur_monte_par_la_maconnerie_pas_par_le_terrassement_ni_l_enduit()
        {
            var earth = Task("01", 0, 1, 2);   earth.Lot = "01";   // fouilles sous murs mesurees SUR les murs
            var mason = Task("05@RDC", 40, 1, 2); mason.Lot = "05";
            var finish = Task("08@RDC", 60, 1, 2); finish.Lot = "08";
            var owners = DanCI.BOQ.Core.Planning.Build4DState.Owners(new[] { earth, mason, finish });
            Assert.Equal("05@RDC", owners[1]);
            Assert.Equal("05@RDC", owners[2]);
            // Pendant le terrassement : aucun mur ne se construit (et n'apparaitra donc pas d'un coup a sa fin).
            var f = DanCI.BOQ.Core.Planning.Build4DState.Compute(new[] { earth, mason, finish },
                new System.Collections.Generic.Dictionary<string, double> { ["01"] = 1, ["05@RDC"] = 0, ["08@RDC"] = 0 }, owners);
            Assert.Empty(f.Built);
            Assert.Empty(f.Building);
            // Poteau : monte par rangs de 20 cm comme les murs.
            var col = new DanCI.BOQ.Core.Planning.Build4DItem { Id = 9, Courses = true, Shape = { DanCI.BOQ.Core.Planning.Build4D.Box(0, 0, 0, 1, 1, 9.84) } };
            var half = DanCI.BOQ.Core.Planning.Build4D.Partial(col, 0.5);
            Assert.True(half.Count >= 7);
            Assert.All(half, p => Assert.True(p.Zt - p.Zb <= DanCI.BOQ.Core.Planning.Build4D.CourseFeet + 1e-6));
        }
}
}
