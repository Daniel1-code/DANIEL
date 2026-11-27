using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class SiteEnvironmentTests
    {
        private static WorkPlan Plan() =>
            WorkPlanner.Build(WorkPlanTests.RealisticBoqPublic(), WorkPlanTests.LevelsPublic, "RDC", WorkPlanTests.SettingsPublic(), new DateTime(2027, 1, 4));

        private static Dictionary<string, double> At(WorkPlan plan, int day) => plan.Tasks.ToDictionary(t => t.Id, t => WorkPlan.ProgressAt(t, day));

        [Fact]
        public void Grue_des_les_fondations_demontee_apres_le_gros_oeuvre_echafaudages_retires_aux_finitions()
        {
            WorkPlan plan = Plan();
            var start = SiteState.At(plan.Tasks, At(plan, 0), 0);
            Assert.False(start.Crane);           // installation de chantier : pas encore de grue
            Assert.False(start.Scaffold);
            int structureDay = plan.Tasks.Where(t => t.Lot == "03").Min(t => t.StartDay) + 3;
            var during = SiteState.At(plan.Tasks, At(plan, structureDay), 0);
            Assert.True(during.Crane);
            Assert.True(during.Structure > 0 && during.Structure < 1);
            var end = SiteState.At(plan.Tasks, At(plan, plan.TotalDays), 0);
            Assert.False(end.Crane);
            Assert.False(end.Scaffold);
            Assert.Equal(1, end.Structure, 6);
        }

        [Fact]
        public void Environnement_terrain_palissade_grue_echafaudages()
        {
            var min = new V3(0, 0, 0);
            var max = new V3(60, 40, 30);
            var none = SiteEnvironment.Build(min, max, new SiteState { Crane = false, Scaffold = false });
            var full = SiteEnvironment.Build(min, max, new SiteState { Crane = true, Scaffold = true, Structure = 0.5, SlewDeg = 30 });
            Assert.True(full.Count > none.Count);
            Assert.All(full, p => Assert.False(p.Mesh.IsEmpty));
            int tris = full.Sum(p => p.Mesh.TriangleCount);
            Assert.True(tris < 20000, tris + " triangles");
            // La grue est plus haute que le batiment ; les echafaudages montent avec le gros oeuvre.
            Assert.True(full.Max(p => p.Mesh.MaxZ) > max.Z);
            double Scaffold(double s) => SiteEnvironment.Build(min, max, new SiteState { Scaffold = true, Structure = s }).Where(p => p.Color == "#A9B1BA").Max(p => p.Mesh.MaxZ);
            Assert.True(Scaffold(0.8) > Scaffold(0.3));
            // La fleche pivote.
            double Tip(double deg) => SiteEnvironment.Build(min, max, new SiteState { Crane = true, SlewDeg = deg }).First(p => p.Color == "#F2B705").Mesh.Points.Max(q => q.X);
            Assert.NotEqual(Tip(0), Tip(90), 3);
        }
    }
}
