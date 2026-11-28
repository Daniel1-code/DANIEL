using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Planning;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;
using Xunit;

namespace DanCI.BOQ.Tests
{
    /// <summary>Gantt detaille : sous-taches (ouvrages) dans chaque tache.</summary>
    public class SubTaskTests
    {
        private static WorkPlan Plan() =>
            WorkPlanner.Build(WorkPlanTests.RealisticBoqPublic(), WorkPlanTests.LevelsPublic, "RDC", WorkPlanTests.SettingsPublic(), new DateTime(2027, 1, 4));

        [Fact]
        public void Sous_taches_dans_la_fenetre_de_leur_tache_et_dans_l_ordre_du_chantier()
        {
            WorkPlan plan = Plan();
            var withSubs = plan.Tasks.Where(t => t.SubTasks.Count > 0).ToList();
            Assert.NotEmpty(withSubs);
            foreach (PlanTask t in withSubs)
            {
                Assert.True(t.SubTasks.Count >= 2);
                Assert.All(t.SubTasks, s =>
                {
                    Assert.True(s.StartDay >= t.StartDay && s.EndDay <= t.EndDay, t.Name + " / " + s.Name);
                    Assert.True(s.DurationDays >= 1);
                    Assert.True(s.Start >= t.Start && s.Finish <= t.Finish);
                });
                Assert.Equal(t.StartDay, t.SubTasks.First().StartDay);
                Assert.Equal(t.EndDay, t.SubTasks.Max(s => s.EndDay));
                Assert.Equal(t.SubTasks.Select(s => s.Phase).OrderBy(p => p), t.SubTasks.Select(s => s.Phase));
                Assert.Equal(t.SubTasks.Sum(s => s.Amount), t.Amount);
            }
        }

        [Theory]
        [InlineData("Coffrage des poteaux", 1)]
        [InlineData("Armatures HA (acier)", 2)]
        [InlineData("Béton armé pour poteaux", 3)]
        [InlineData("Enduit au mortier de ciment", 4)]
        [InlineData("Carrelage grès cérame", 5)]
        [InlineData("Peinture vinylique", 6)]
        [InlineData("Fouilles en rigole", 0)]
        public void Ordre_du_chantier_d_apres_la_designation(string designation, int phase)
        {
            Assert.Equal(phase, WorkPlanner.SubTaskPhase(designation));
        }

        [Fact]
        public void Lignes_du_Gantt_avec_et_sans_sous_taches()
        {
            WorkPlan plan = Plan();
            var detailed = GanttRows.Build(plan);
            var compact = GanttRows.Build(plan, false);
            Assert.Equal(plan.Tasks.Sum(t => t.SubTasks.Count), detailed.Count(r => r.IsSubTask));
            Assert.DoesNotContain(compact, r => r.IsSubTask);
            // Chaque sous-tache suit sa tache.
            for (int i = 0; i < detailed.Count; i++)
                if (detailed[i].IsSubTask) Assert.True(detailed[i - 1].Task == detailed[i].Task);
        }

        [Fact]
        public void PDF_et_Excel_montrent_les_sous_taches()
        {
            WorkPlan plan = Plan();
            string svg = string.Concat(GanttChart.Pages(plan, "Test"));
            string first = plan.Tasks.First(t => t.SubTasks.Count > 0).SubTasks[0].Name;
            Assert.Contains("└", svg);
            ExportContext ctx = ExportTests.Context();
            ctx.Schedule = null;
            string dir = Path.Combine(Path.GetTempPath(), "danci-sub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string xlsx = Path.Combine(dir, "p.xlsx");
                BoqWorkbookWriter.Write(ctx, xlsx);
                using (var wb = new XLWorkbook(xlsx))
                {
                    if (wb.Worksheets.Any(w => w.Name == BoqWorkbookWriter.PlanningSheetName) && ctx.WorkPlan.Tasks.Any(t => t.SubTasks.Count > 0))
                        Assert.Contains(wb.Worksheet(BoqWorkbookWriter.PlanningSheetName).CellsUsed(), c => c.GetString().Contains("└"));
                }
            }
            finally { try { Directory.Delete(dir, true); } catch (IOException) { } }
            Assert.False(string.IsNullOrEmpty(first));
        }
    }
}
