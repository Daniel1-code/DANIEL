using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Planning;
using Xunit;
using Xunit.Abstractions;

namespace DanCI.BOQ.Tests
{
    /// <summary>
    /// Lecture COMPLETE d'un vrai planning, image par image, comme le Studio 4D (avancement affiche,
    /// proprietaires, groupes, rangs) : aucun element ne doit apparaitre entier sans etre passe par
    /// des rangs partiels.
    /// </summary>
    public class Studio4DPipelineTests
    {
        private readonly ITestOutputHelper output;
        public Studio4DPipelineTests(ITestOutputHelper output) { this.output = output; }

        [Fact]
        public void Lecture_complete_aucun_element_n_apparait_entier_d_un_coup()
        {
            WorkPlan plan = WorkPlanner.Build(WorkPlanTests.RealisticBoqPublic(), WorkPlanTests.LevelsPublic, "RDC", WorkPlanTests.SettingsPublic(), new DateTime(2027, 1, 4));
            var owners = Build4DState.Owners(plan.Tasks);
            // Formes : un « mur » de 3 m par element, a la hauteur de son niveau.
            var items = new Dictionary<long, Build4DItem>();
            foreach (var t in plan.Tasks)
                foreach (var r in t.Elements.Where(x => !x.IsLinked))
                    if (!items.ContainsKey(r.ElementId))
                    {
                        int lvl = Math.Max(0, Array.IndexOf(WorkPlanTests.LevelsPublic.ToArray(), t.Level));
                        items[r.ElementId] = new Build4DItem { Id = r.ElementId, Rank = 1, Zb = lvl * 10, Courses = true, Shape = { Build4D.Box(r.ElementId, 0, lvl * 10, r.ElementId + 10, 0.66, lvl * 10 + 9.84) } };
                    }
            var shown = new Dictionary<string, double>();
            var everPartial = new HashSet<long>();
            var visible = new HashSet<long>();
            var popped = new List<string>();
            int day = 0, lastDay = -1, frames = 0;
            while (frames < 100000)
            {
                bool advance = true;
                var mode = advance && day >= lastDay ? Build4D.Follow.Advance : day == lastDay ? Build4D.Follow.Hold : Build4D.Follow.Snap;
                lastDay = day;
                var progress = new Dictionary<string, double>();
                foreach (var t in plan.Tasks)
                {
                    double now = Build4D.Next(shown.TryGetValue(t.Id, out double v) ? v : 0, WorkPlan.ProgressAt(t, day), mode);
                    shown[t.Id] = now;
                    progress[t.Id] = now;
                }
                var f = Build4DState.Compute(plan.Tasks, progress, owners);
                // Cote Revit : elements en construction (rangs dessines) et elements reels visibles.
                var inProgress = new HashSet<long>();
                foreach (var b in f.Building)
                {
                    var its = b.Elements.Select(id => items[id]).ToList();
                    var prog = Build4D.Schedule(its, b.Progress);
                    foreach (var it in its)
                    {
                        double p = prog[it.Id];
                        if (p >= 0.999) continue;
                        inProgress.Add(it.Id);
                        if (p > 0 && Build4D.Partial(it, p).Count > 0) everPartial.Add(it.Id);
                    }
                }
                var nowVisible = new HashSet<long>(f.Built.Concat(f.Running.Keys).Where(id => !inProgress.Contains(id)));
                foreach (long id in nowVisible.Where(id => !visible.Contains(id) && !everPartial.Contains(id)))
                {
                    var task = plan.Tasks.First(t => t.Id == owners[id]);
                    popped.Add(id + " (tache " + task.Id + ", " + task.DurationDays + " j, image " + frames + ", jour " + day + ")");
                }
                visible = nowVisible;
                frames++;
                bool catching = plan.Tasks.Any(t => shown[t.Id] < WorkPlan.ProgressAt(t, day) - 1e-9);
                if (day >= plan.TotalDays && !catching) break;
                if (day < plan.TotalDays) day++;
            }
            output.WriteLine(frames + " images, " + items.Count + " elements, " + popped.Count + " apparus d'un coup");
            foreach (string p in popped.Take(20)) output.WriteLine("  " + p);
            Assert.Empty(popped);
        }

        /// <summary>
        /// Version 0.53.0 : cadence PAR ELEMENT (rangs de 20 cm, rattrapage borne a 1/5 par image), la date ne s'arrete jamais, pas de 2 jours,
        /// pour les 3 reglages de montee : la lecture se termine, aucun element ne monte de plus de N rangs
        /// par image, aucun n'apparait entier, et a la fin tout est construit.
        /// </summary>
        [Theory]
        [InlineData(1, 24)]
        [InlineData(2, 14)]
        [InlineData(3, 8)]
        [InlineData(5, 4)]
        public void Lecture_synchronisee_rangs_de_20_cm_par_element(int courses, int minFrames)
        {
            WorkPlan plan = WorkPlanner.Build(WorkPlanTests.RealisticBoqPublic(), WorkPlanTests.LevelsPublic, "RDC", WorkPlanTests.SettingsPublic(), new DateTime(2027, 1, 4));
            var owners = Build4DState.Owners(plan.Tasks);
            var builders = new HashSet<string>(owners.Values);
            var items = new Dictionary<long, Build4DItem>();
            foreach (var t in plan.Tasks)
                foreach (var r in t.Elements.Where(x => !x.IsLinked))
                    if (!items.ContainsKey(r.ElementId))
                    {
                        int lvl = Math.Max(0, Array.IndexOf(WorkPlanTests.LevelsPublic.ToArray(), t.Level));
                        items[r.ElementId] = new Build4DItem { Id = r.ElementId, Rank = 1, Zb = lvl * 10, Courses = true, Shape = { Build4D.Box(r.ElementId, 0, lvl * 10, r.ElementId + 10, 0.66, lvl * 10 + 9.84) } };
                    }
            const int step = 2;
            var shown = new Dictionary<string, double>();
            var elem = new Dictionary<long, double>();
            var everPartial = new HashSet<long>();
            var visible = new HashSet<long>();
            int popped = 0, tooFast = 0, day = 0, frames = 0, holds = 0;
            while (frames < 200000)
            {
                var progress = new Dictionary<string, double>();
                foreach (var t in plan.Tasks)
                {
                    double now = Build4D.Next(shown.TryGetValue(t.Id, out double v) ? v : 0, WorkPlan.ProgressAt(t, day),
                                              builders.Contains(t.Id) ? Build4D.Follow.Advance : Build4D.Follow.Snap, minFrames);
                    shown[t.Id] = now;
                    progress[t.Id] = now;
                }
                var f = Build4DState.Compute(plan.Tasks, progress, owners);
                // Cote Revit (comme BoqHost) : avancement affiche par element, au plus N rangs par image.
                var inProgress = new HashSet<long>();
                var seen = new HashSet<long>();
                int lagging = 0;
                void Draw(Build4DItem it, double target)
                {
                    seen.Add(it.Id);
                    double? prev = elem.TryGetValue(it.Id, out double pv) ? pv : (double?)null;
                    double p = Build4D.NextElement(it, prev, target, Build4D.Follow.Advance, courses);
                    if (p - (prev ?? 0) > Math.Max(Build4D.RiseStep(it, courses), Build4D.MaxCatchStep) + 1e-9) tooFast++;
                    elem[it.Id] = p;
                    if (p < target - 1e-6) lagging++;
                    if (p >= 0.999) return;
                    inProgress.Add(it.Id);
                    if (p > 0 && Build4D.Partial(it, p).Count > 0) everPartial.Add(it.Id);
                }
                foreach (var b in f.Building)
                {
                    var its = b.Elements.Select(id => items[id]).ToList();
                    var prog = Build4D.Schedule(its, b.Progress);
                    foreach (var it in its) Draw(it, prog[it.Id]);
                }
                var built = new HashSet<long>(f.Built);
                foreach (long id in elem.Keys.ToList())
                    if (!seen.Contains(id) && elem[id] < 0.999 && (built.Contains(id) || f.Running.ContainsKey(id))) Draw(items[id], 1);
                var nowVisible = new HashSet<long>(f.Built.Concat(f.Running.Keys).Where(id => items.ContainsKey(id) && !inProgress.Contains(id)));
                popped += nowVisible.Count(id => !visible.Contains(id) && !everPartial.Contains(id));
                visible = nowVisible;
                frames++;
                bool catching = lagging > 0 || plan.Tasks.Any(t => builders.Contains(t.Id) && shown[t.Id] < WorkPlan.ProgressAt(t, day) - 1e-9);
                if (day >= plan.TotalDays && !catching) break;
                if (day >= plan.TotalDays) { holds++; continue; } // fin : la 3D finit de monter
                day = Math.Min(plan.TotalDays, day + step); // la date ne s'arrete JAMAIS en cours de chantier
            }
            output.WriteLine("montee " + courses + " rang(s)/image : " + frames + " images (" + holds + " d'attente), " + items.Count + " elements");
            Assert.True(frames < 200000, "la lecture doit se terminer");
            Assert.Equal(0, tooFast);
            Assert.True(holds <= 25, holds + " images d'attente en fin de chantier");
            Assert.Equal(0, popped);
            Assert.All(items.Keys, id => Assert.True(visible.Contains(id), "element " + id + " construit a la fin"));
        }
    }
}
