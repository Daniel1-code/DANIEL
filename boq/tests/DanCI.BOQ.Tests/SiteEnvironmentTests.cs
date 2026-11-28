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

namespace DanCI.BOQ.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using DanCI.BOQ.Core.Extraction;
    using DanCI.BOQ.Core.Planning;
    using Xunit;

    public class DigSceneTests
    {
        private static double Area(TriMesh m)
        {
            double a = 0;
            for (int i = 0; i + 2 < m.Points.Count; i += 3) a += (m.Points[i + 1] - m.Points[i]).Cross(m.Points[i + 2] - m.Points[i]).Length / 2;
            return a;
        }

        [Fact]
        public void Plateforme_trouee_aux_fouilles_ouvertes()
        {
            var holes = new List<(double, double, double, double)> { (2, 2, 4, 6), (3, 5, 8, 7), (20, 20, 30, 30) };
            TriMesh m = SiteEnvironment.PlaneWithHoles(0, 0, 10, 10, 0, holes);
            // 100 - (2x4 + 5x2 - recouvrement 1x1) ; le 3e trou est hors de la plateforme.
            Assert.Equal(100 - (8 + 10 - 1), Area(m), 6);
        }

        private static DigScene Scene(int dug, bool excavating, bool open = true)
        {
            var pits = new List<Prism>();
            for (int i = 0; i < 6; i++) pits.Add(Excavation3D.Pit(i * 10, 0, i * 10 + 4, 4, 1, 1, -5, 0));
            return new DigScene { GroundZ = 0, Pits = pits, Dug = dug, Progress = dug / 6.0, Open = open, Excavating = excavating };
        }

        [Fact]
        public void Pelle_et_camion_pendant_le_terrassement_seulement_fouilles_et_deblais()
        {
            var min = new V3(0, 0, -5);
            var max = new V3(60, 20, 30);
            var digging = SiteEnvironment.Build(min, max, new SiteState(), Scene(3, true));
            var after = SiteEnvironment.Build(min, max, new SiteState(), Scene(6, false));
            var filled = SiteEnvironment.Build(min, max, new SiteState(), Scene(0, false, false));
            bool Has(List<(TriMesh Mesh, string Color)> p, string c) => p.Any(x => x.Color == c);
            Assert.True(Has(digging, "#F4B400"));   // pelle
            Assert.True(Has(digging, "#C8462E"));   // benne du camion
            Assert.True(Has(digging, "#5E4430"));   // fond des fouilles ouvertes
            Assert.False(Has(after, "#F4B400"));    // terrassement fini : la pelle est partie
            Assert.True(Has(after, "#8B6B47"));     // deblais encore la (avant remblai)
            Assert.False(Has(filled, "#5E4430"));   // remblai : plus de fouilles
            // Le terrain est au niveau du terrain naturel, pas au fond des fondations.
            Assert.True(filled.First(p => p.Color == "#6F8F4E").Mesh.MinZ > -1);
            // Le godet descend dans la fouille en cours quand il creuse.
            var deep = SiteEnvironment.Build(min, max, new SiteState { Phase = 0 }, Scene(3, true));
            Assert.True(deep.Where(p => p.Color == "#4A4A4A").Min(p => p.Mesh.MinZ) < -2);
        }
    }
}

namespace DanCI.BOQ.Tests
{
    using System.Collections.Generic;
    using System.Linq;
    using DanCI.BOQ.Core.Planning;
    using Xunit;

    public class DigPlannerTests
    {
        private const double M = 3.28084;

        [Fact]
        public void Fouilles_deduites_des_elements_enterres_a_leur_emplacement_et_profondeur()
        {
            var elements = new List<(double, double, double, double, double)>
            {
                (10, 10, 14, 14, -1.5 * M),     // semelle a 1,50 m sous le terrain
                (30, 10, 34, 14, -1.0 * M),     // semelle a 1,00 m
                (0, 0, 60, 40, -0.2 * M),       // dallage 20 cm sous le sol : PAS une fouille
                (50, 50, 54, 54, 0.5 * M)       // au-dessus du terrain : pas de fouille
            };
            var pits = DigPlanner.FromElements(elements, 0);
            Assert.Equal(2, pits.Count);
            DanCI.BOQ.Core.Extraction.Prism first = pits[0];
            // A l'emplacement de l'element (surlargeur de travail 30 cm), fond a la vraie profondeur, tete au terrain naturel.
            Assert.Equal(-1.5 * M, first.Zb, 6);
            Assert.Equal(0, first.Zt, 6);
            Assert.True(first.Bottom.Where((v, i) => i % 2 == 0).Min() < 10 && first.Bottom.Where((v, i) => i % 2 == 0).Max() > 14);
            Assert.True(first.Bottom.Where((v, i) => i % 2 == 0).Min() > 8.5);
            Assert.Empty(DigPlanner.FromElements(new List<(double, double, double, double, double)>(), 0));
        }

        private static PlanTask T(string id, string lot, int start, int days) => new PlanTask { Id = id, Lot = lot, StartDay = start, DurationDays = days };

        [Fact]
        public void Ouverture_et_remblai_des_fouilles()
        {
            var with01 = new List<PlanTask> { T("01", "01", 0, 10), T("02", "02", 10, 10) };
            Assert.Equal(0.5, DigPlanner.Earthwork(with01, new Dictionary<string, double> { ["01"] = 0.5, ["02"] = 0 }), 6);
            Assert.Equal(1, DigPlanner.Earthwork(with01, new Dictionary<string, double> { ["01"] = 1, ["02"] = 0.5 }), 6);
            Assert.Equal(-1, DigPlanner.Earthwork(with01, new Dictionary<string, double> { ["01"] = 1, ["02"] = 1 }), 6); // remblai
            // Sans lot 01 : creusees pendant le premier tiers des fondations, remblayees a la fin.
            var only02 = new List<PlanTask> { T("02", "02", 0, 10) };
            Assert.Equal(0, DigPlanner.Earthwork(only02, new Dictionary<string, double> { ["02"] = 0 }), 6);
            Assert.Equal(1, DigPlanner.Earthwork(only02, new Dictionary<string, double> { ["02"] = 0.5 }), 6);
            Assert.Equal(-1, DigPlanner.Earthwork(only02, new Dictionary<string, double> { ["02"] = 1 }), 6);
            Assert.Equal(-1, DigPlanner.Earthwork(new List<PlanTask> { T("03", "03", 0, 5) }, new Dictionary<string, double>()), 6);
        }

        [Fact]
        public void Sans_fouille_reelle_aucun_engin_invente()
        {
            var scene = new DigScene { GroundZ = 0, Pits = new List<DanCI.BOQ.Core.Extraction.Prism>(), Dug = 0, Progress = 0.5, Open = true, Excavating = true };
            var parts = SiteEnvironment.Build(new V3(0, 0, -3), new V3(40, 30, 20), new SiteState(), scene);
            Assert.DoesNotContain(parts, p => p.Color == "#F4B400" || p.Color == "#C8462E");
        }
    }
}
