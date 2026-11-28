using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Planning;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Pdf;
using Xunit;

namespace DanCI.BOQ.Tests
{
    /// <summary>Rapport de simulation 4D (PDF).</summary>
    public class Sim4DReportTests
    {
        private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");

        internal static Sim4DReport Report(CoverTheme theme, int shots)
        {
            WorkPlan plan = WorkPlanner.Build(WorkPlanTests.RealisticBoqPublic(), WorkPlanTests.LevelsPublic, "RDC", WorkPlanTests.SettingsPublic(), new DateTime(2027, 1, 4));
            ExportContext ctx = ExportTests.Context();
            ctx.Cover = new CoverSettings { Theme = theme };
            return new Sim4DReport
            {
                Context = ctx,
                Plan = plan,
                Snapshots = Enumerable.Range(0, shots).Select(i => new Sim4DSnapshot { Caption = "lundi 04 janvier 2027 — jour " + i * 10, Progress = i / 10.0, Jpeg = Png }).ToList(),
                Settings = new List<KeyValuePair<string, string>> { new KeyValuePair<string, string>("Pas de lecture", "2 jours par image") },
                Frames = 420, AverageFrameMs = 180, PlayTime = TimeSpan.FromSeconds(95), Completed = true, SimulatedElements = 26,
                Warnings = new List<string> { "3 élément(s) à la forme illisible" }
            };
        }

        [Theory]
        [InlineData(CoverTheme.Futuriste, 11)]
        [InlineData(CoverTheme.Classique, 0)]
        public void Rapport_synthese_captures_Gantt_taches(CoverTheme theme, int shots)
        {
            Sim4DReport r = Report(theme, shots);
            byte[] pdf = Sim4DReportPdf.Generate(r);
            Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(pdf, 0, 4));
            int pages = Sim4DReportPdf.PageImages(r, 30).Count;
            int gantt = GanttChart.Pages(r.Plan, "x").Count;
            Assert.True(pages >= 1 + (shots + 1) / 2 + gantt + 1, pages + " page(s)");
        }

        [Fact]
        public void Points_d_attention_reprennent_les_avertissements_et_les_conflits()
        {
            Sim4DReport r = Report(CoverTheme.Futuriste, 0);
            r.Plan.Tasks[0].Conflict = "début imposé avant la fin des tâches précédentes";
            var w = Sim4DReportPdf.Attention(r).ToList();
            Assert.Contains(w, s => s.Contains("forme illisible"));
            Assert.Contains(w, s => s.Contains("début imposé"));
        }

        [Fact]
        public void Sans_planning_refus_explicite()
        {
            Assert.Throws<ArgumentException>(() => Sim4DReportPdf.Generate(new Sim4DReport()));
        }
    }
}
