using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Planning;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class WorkPlanTests
    {
        private static readonly string[] Levels = { "Fondation", "RDC", "Étage 1", "Toiture" };
        internal static IReadOnlyList<string> LevelsPublic => Levels;
        internal static PlanningSettings SettingsPublic() => Settings();
        internal static BoqResult RealisticBoqPublic() => RealisticBoq();

        private static BoqResult Boq()
        {
            var boq = new BoqResult { Currency = DanCI.BOQ.Core.Pricing.Currency.USD };
            int id = 1;
            void Line(string lot, string code, BoqUnit unit, decimal qty, string level, decimal? amount = null)
            {
                var l = boq.Lots.FirstOrDefault(x => x.Code == lot);
                if (l == null) boq.Lots.Add(l = new BoqLotResult { Code = lot, Designation = "Lot " + lot });
                var line = new BoqLine { Code = code, LotCode = lot, Designation = code, Unit = unit, NetQuantity = qty, Quantity = qty, Amount = amount };
                line.Contributions.Add(new LineContribution(new ElementQuantity { ElementId = id++, Level = level, Value = (double)qty, Unit = unit }, "R"));
                l.Lines.Add(line);
            }
            Line("01", "01.01", BoqUnit.CubicMeter, 60, "Fondation");            // 60/12 = 5 j
            Line("02", "02.03", BoqUnit.CubicMeter, 30, "Fondation");            // 30/6 = 5 j
            foreach (string lvl in new[] { "RDC", "Étage 1" })
            {
                Line("03", "03.03", BoqUnit.CubicMeter, 24, lvl);                // 24/6 = 4 j, 2 equipes -> 2 j
                Line("05", "05.01", BoqUnit.SquareMeter, 200, lvl);              // 200/10 = 20 j, 2 equipes -> 10 j
                Line("08", "08.02", BoqUnit.SquareMeter, 600, lvl);              // 600/60 = 10 j, 2 equipes -> 5 j
                Line("09", "09.01", BoqUnit.LinearMeter, 60, lvl);               // 60/30 = 2 j
            }
            Line("06", "06.01", BoqUnit.SquareMeter, 120, "Toiture");            // 120/40 = 3 j
            Line("11", "11.01", BoqUnit.SquareMeter, 200, "Étage 1");            // VRD : niveau bas
            return boq;
        }

        private static PlanningSettings Settings() =>
            ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Planning;

        [Fact]
        public void Rendements_lus_dans_planning_json()
        {
            PlanningSettings s = Settings();
            Assert.Equal(6, s.WorkingDaysPerWeek);
            Assert.Equal(2, s.CrewsFor("05"));
            Assert.Equal(25, s.RateFor("08.01", BoqUnit.SquareMeter).PerDay);
            Assert.Equal(60, s.RateFor("08.02", BoqUnit.SquareMeter).PerDay);
            Assert.Equal(6, s.RateFor("03.03", BoqUnit.CubicMeter).PerDay);
            Assert.Equal(5, s.RateFor("99.01", BoqUnit.CubicMeter).PerDay);   // repli
        }

        [Fact]
        public void Du_niveau_bas_vers_le_niveau_haut_avec_enchainements_realistes()
        {
            WorkPlan plan = WorkPlanner.Build(Boq(), Levels, "Fondation", Settings(), new DateTime(2027, 1, 5));
            PlanTask T(string id) => plan.Tasks.Single(t => t.Id == id);
            Assert.Equal(5, T("01").DurationDays);
            Assert.Equal(5, T("02").DurationDays);
            Assert.Equal(2, T("03@RDC").DurationDays);
            Assert.Equal(10, T("05@RDC").DurationDays);
            // Fondations apres terrassements, gros oeuvre RDC apres fondations.
            Assert.True(T("02").StartDay >= T("01").EndDay);
            Assert.True(T("03@RDC").StartDay >= T("02").EndDay);
            // Etage 1 apres RDC + delai de decoffrage (7 j).
            Assert.Equal(T("03@RDC").EndDay + 7, T("03@Étage 1").StartDay);
            // Maconnerie apres le gros oeuvre du niveau + 3 j ; une seule equipe de maconnerie par niveau a la fois.
            Assert.Equal(T("03@RDC").EndDay + 3, T("05@RDC").StartDay);
            Assert.True(T("05@Étage 1").StartDay >= T("05@RDC").EndDay);
            // Finitions apres maconnerie et reseaux du niveau.
            Assert.True(T("08@RDC").StartDay >= T("05@RDC").EndDay && T("08@RDC").StartDay >= T("09@RDC").EndDay);
            // Toiture apres le gros oeuvre du dernier niveau ; VRD au niveau bas, apres la maconnerie.
            Assert.True(T("06").StartDay >= T("03@Étage 1").EndDay);
            Assert.Equal("Fondation", T("11").Level);
            Assert.True(T("11").StartDay >= T("05@Étage 1").EndDay);
            // Chemin critique : au moins une tache, la derniere tache finit le projet.
            Assert.Contains(plan.Tasks, t => t.IsCritical);
            Assert.Equal(plan.Tasks.Max(t => t.EndDay), plan.TotalDays);
            Assert.True(plan.Tasks.Where(t => t.EndDay == plan.TotalDays).All(t => t.IsCritical));
        }

        [Fact]
        public void Calendrier_dimanches_et_feries_chomes()
        {
            var cal = new WorkCalendar(new DateTime(2026, 12, 24), 6, new[] { new DateTime(2026, 12, 25) });
            Assert.Equal(new DateTime(2026, 12, 24), cal.DateOf(0));     // jeudi
            Assert.Equal(new DateTime(2026, 12, 26), cal.DateOf(1));     // vendredi 25 ferie -> samedi
            Assert.Equal(new DateTime(2026, 12, 28), cal.DateOf(2));     // dimanche chome -> lundi
            var five = new WorkCalendar(new DateTime(2027, 1, 1), 5, null);   // vendredi
            Assert.Equal(new DateTime(2027, 1, 4), five.DateOf(1));       // samedi et dimanche chomes
        }

        [Fact]
        public void Dates_et_marges_coherentes()
        {
            WorkPlan plan = WorkPlanner.Build(Boq(), Levels, "Fondation", Settings(), new DateTime(2027, 1, 5));
            Assert.All(plan.Tasks, t =>
            {
                Assert.True(t.Finish >= t.Start);
                Assert.True(t.FloatDays >= 0);
                Assert.All(t.Predecessors, p => Assert.True(t.StartDay >= plan.Tasks.Single(x => x.Id == p).EndDay));
                Assert.Contains("j", t.Basis);
            });
            Assert.True(plan.FinishDate > plan.StartDate);
        }

        /// <summary>Devis realiste R+1 (fondation, RDC, etage, toiture) pour les rendus.</summary>
        internal static BoqResult RealisticBoq()
        {
            var boq = new BoqResult { Currency = DanCI.BOQ.Core.Pricing.Currency.USD };
            int id = 1;
            void Line(string lot, string des, string code, BoqUnit unit, decimal qty, string level, decimal pu)
            {
                var l = boq.Lots.FirstOrDefault(x => x.Code == lot);
                if (l == null) boq.Lots.Add(l = new BoqLotResult { Code = lot, Designation = des });
                var line = new BoqLine { Code = code, LotCode = lot, Designation = code, Unit = unit, NetQuantity = qty, Quantity = qty, UnitPrice = pu, Amount = qty * pu };
                line.Contributions.Add(new LineContribution(new ElementQuantity { ElementId = id++, Level = level, Value = (double)qty, Unit = unit }, "R"));
                l.Lines.Add(line);
            }
            Line("01", "Terrassements", "01.01", BoqUnit.CubicMeter, 96, "Fondation", 12);
            Line("01", "Terrassements", "01.06", BoqUnit.CubicMeter, 48, "Fondation", 12);
            Line("02", "Fondations / Infrastructure", "02.03", BoqUnit.CubicMeter, 38, "Fondation", 180);
            Line("02", "Fondations / Infrastructure", "02.05", BoqUnit.Kilogram, 3200, "Fondation", 2.1m);
            foreach (string lvl in new[] { "RDC", "Étage 1" })
            {
                Line("03", "Gros œuvre / Superstructure", "03.03", BoqUnit.CubicMeter, 42, lvl, 210);
                Line("03", "Gros œuvre / Superstructure", "03.07", BoqUnit.SquareMeter, 260, lvl, 14);
                Line("03", "Gros œuvre / Superstructure", "03.08", BoqUnit.Kilogram, 4100, lvl, 2.1m);
                Line("05", "Maçonnerie", "05.01", BoqUnit.SquareMeter, 310, lvl, 18);
                Line("09", "Plomberie – Sanitaire", "09.01", BoqUnit.LinearMeter, 85, lvl, 9);
                Line("10", "Électricité", "10.01", BoqUnit.LinearMeter, 420, lvl, 3);
                Line("07", "Menuiseries", "07.01", BoqUnit.Each, 18, lvl, 160);
                Line("08", "Revêtements – Enduits – Peinture", "08.01", BoqUnit.SquareMeter, 620, lvl, 6);
                Line("08", "Revêtements – Enduits – Peinture", "08.03", BoqUnit.SquareMeter, 140, lvl, 22);
                Line("08", "Revêtements – Enduits – Peinture", "08.02", BoqUnit.SquareMeter, 900, lvl, 3);
            }
            Line("06", "Toiture – Couverture – Étanchéité", "06.01", BoqUnit.SquareMeter, 160, "Toiture", 25);
            Line("11", "VRD", "11.01", BoqUnit.SquareMeter, 220, "RDC", 30);
            return boq;
        }

        [Fact]
        public void Gantt_SVG_une_ou_plusieurs_pages()
        {
            WorkPlan plan = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4));
            var pages = DanCI.BOQ.Export.Pdf.GanttChart.Pages(plan, "Villa R+1 — Kinshasa");
            Assert.NotEmpty(pages);
            Assert.All(pages, p => Assert.StartsWith("<svg", p));
            Assert.Contains("NIVEAU RDC", pages[0]);
            Assert.Contains("Réception des travaux", pages.Last());
            string keep = Environment.GetEnvironmentVariable("DANCI_KEEP_GANTT");
            if (!string.IsNullOrEmpty(keep))
            {
                var o = new PlanOverrides();
                plan.SaveBaselineTo(o, new DateTime(2027, 1, 2));
                o.Delays["02"] = 8;
                o.Progress["00"] = 100; o.Progress["01"] = 100; o.Progress["02"] = 60;
                WorkPlan tracked = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4), o);
                System.IO.File.WriteAllText(keep, DanCI.BOQ.Export.Pdf.GanttChart.Pages(tracked, "Villa R+1 — Kinshasa (suivi)")[0]);
            }
        }

        [Fact]
        public void PDF_et_Excel_avec_le_planning()
        {
            var ctx = ExportTests.Context();
            ctx.Boq = RealisticBoq();
            ctx.LevelOrder = Levels;
            ctx.Schedule = new ProjectSchedule { StartDate = new DateTime(2027, 1, 4) };
            ctx.LevelElevations = new Dictionary<string, double> { ["Fondation"] = -1.5, ["RDC"] = 0, ["Étage 1"] = 3.2, ["Toiture"] = 6.4 };
            Assert.Equal(new DateTime(2027, 1, 4), ctx.WorkPlan.StartDate);
            byte[] pdf = DanCI.BOQ.Export.Pdf.BoqPdfWriter.Generate(ctx);
            Assert.True(pdf.Length > 1000);
            string xlsx = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "danci_plan_" + Guid.NewGuid().ToString("N") + ".xlsx");
            DanCI.BOQ.Export.Excel.BoqWorkbookWriter.Write(ctx, xlsx);
            using (var wb = new ClosedXML.Excel.XLWorkbook(xlsx))
            {
                string all = string.Join("|", wb.Worksheet(DanCI.BOQ.Export.Excel.BoqWorkbookWriter.PlanningSheetName).CellsUsed().Select(c => c.GetString()));
                Assert.Contains("NIVEAU RDC", all);
                Assert.Contains("Réception des travaux", all);
            }
            string keep = Environment.GetEnvironmentVariable("DANCI_KEEP_PLAN");
            if (!string.IsNullOrEmpty(keep))
            {
                System.IO.File.WriteAllBytes(keep + ".pdf", pdf);
                System.IO.File.Copy(xlsx, keep + ".xlsx", true);
            }
            System.IO.File.Delete(xlsx);
        }

        [Fact]
        public void Montants_hebdomadaires_pour_courbe_en_S_et_histogramme_du_Studio()
        {
            WorkPlan plan = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4));
            var weeks = plan.WeeklyAmounts();
            Assert.Equal(plan.TotalAmount, decimal.Round(weeks.Sum(w => w.Amount), 6));
            Assert.Equal(1m, decimal.Round(weeks.Last().CumulativeShare, 6));
            for (int i = 1; i < weeks.Count; i++) Assert.True(weeks[i].CumulativeShare >= weeks[i - 1].CumulativeShare);
            Assert.True(weeks.Last().FirstDay < plan.TotalDays);
        }

        [Fact]
        public void Tresorerie_mensuelle_somme_egale_au_total_et_courbe_en_S()
        {
            WorkPlan plan = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4));
            var months = plan.CashFlow();
            Assert.Equal(plan.TotalAmount, months.Sum(m => m.Amount));
            Assert.Equal(1m, months.Last().CumulativeShare);
            Assert.True(months.Zip(months.Skip(1), (a, b) => a.Cumulative <= b.Cumulative).All(x => x));
            Assert.Equal(new DateTime(2027, 1, 1), months.First().Month);
        }

        [Fact]
        public void Simulation_4D_etats_et_elements_construits_a_une_date()
        {
            WorkPlan plan = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4));
            PlanTask rdc = plan.Tasks.Single(t => t.Id == "03@RDC");
            PlanTask etage = plan.Tasks.Single(t => t.Id == "03@Étage 1");
            Assert.NotEmpty(rdc.Elements);
            int day = rdc.EndDay;                                              // gros oeuvre RDC fini
            Assert.Equal(TaskState.Done, WorkPlan.StateAt(rdc, day));
            Assert.Equal(TaskState.NotStarted, WorkPlan.StateAt(etage, day));
            var built = plan.BuiltAt(day);
            Assert.True(rdc.Elements.All(built.Contains));
            Assert.False(etage.Elements.Any(built.Contains));
            Assert.Equal(0, plan.DayOf(new DateTime(2026, 12, 1)));
            Assert.Equal(plan.TotalDays, plan.DayOf(plan.FinishDate.AddDays(30)));
            Assert.Equal(0.5, WorkPlan.ProgressAt(new PlanTask { StartDay = 0, DurationDays = 4 }, 1), 9);
        }

        [Fact]
        public void Modifications_du_Gantt_duree_decalage_equipes_recalculent_tout()
        {
            var start = new DateTime(2027, 1, 4);
            WorkPlan before = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start);
            PlanTask mason = before.Tasks.Single(t => t.Id == "05@RDC");
            var o = new PlanOverrides();
            o.Durations["05@RDC"] = mason.DurationDays + 10;          // maconnerie RDC plus longue
            o.Delays["02"] = 5;                                        // fondations decalees de 5 j
            o.Crews["08"] = 4;                                         // 4 equipes de finitions
            WorkPlan after = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start, o);
            PlanTask T(WorkPlan p, string id) => p.Tasks.Single(t => t.Id == id);
            Assert.True(T(after, "05@RDC").IsDurationImposed);
            Assert.Equal(mason.DurationDays + 10, T(after, "05@RDC").DurationDays);
            Assert.Equal(mason.ComputedDays, T(after, "05@RDC").ComputedDays);
            Assert.Equal(T(after, "01").EndDay + 5, T(after, "02").StartDay);
            Assert.True(T(after, "08@RDC").DurationDays < T(before, "08@RDC").DurationDays);
            Assert.Equal(4, T(after, "08@RDC").Crews);
            // Les successeurs suivent : le gros oeuvre RDC demarre 5 j plus tard.
            Assert.Equal(T(before, "03@RDC").StartDay + 5, T(after, "03@RDC").StartDay);
            // Marges toujours coherentes.
            Assert.All(after.Tasks, t => Assert.True(t.FloatDays >= 0));
            Assert.True(T(after, "02").IsCritical);
        }

        [Fact]
        public void Dates_de_debut_et_de_fin_imposees_par_l_utilisateur()
        {
            var start = new DateTime(2027, 1, 4); // lundi
            WorkPlan before = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start);
            PlanTask T(WorkPlan p, string id) => p.Tasks.Single(t => t.Id == id);
            var o = new PlanOverrides();
            DateTime wanted = T(before, "05@RDC").Start.AddDays(21);
            while (wanted.DayOfWeek == DayOfWeek.Sunday) wanted = wanted.AddDays(1);
            o.Starts["05@RDC"] = wanted;                                   // maconnerie RDC : debut impose
            o.Finishes["05@RDC"] = wanted.AddDays(13);                     // et fin imposee
            WorkPlan after = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start, o);
            PlanTask m = T(after, "05@RDC");
            Assert.True(m.IsStartImposed);
            Assert.True(m.IsFinishImposed);
            Assert.Equal(wanted, m.Start);
            DateTime f = wanted.AddDays(13);
            while (f.DayOfWeek == DayOfWeek.Sunday) f = f.AddDays(-1);
            Assert.Equal(f, m.Finish);
            Assert.Null(m.Conflict);
            // Les successeurs suivent la nouvelle fin.
            Assert.All(after.Tasks.Where(t => t.Predecessors.Contains("05@RDC")), t => Assert.True(t.StartDay >= m.EndDay));

            // Debut impose AVANT la fin des taches precedentes : garde la date, incoherence signalee.
            var bad = new PlanOverrides();
            bad.Starts["05@RDC"] = start;
            WorkPlan conflict = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start, bad);
            // (4 janvier ferie dans ce calendrier : premier jour ouvre)
            Assert.Equal(new WorkCalendar(start, conflict.WorkingDaysPerWeek, conflict.Holidays).DateOf(0), T(conflict, "05@RDC").Start);
            Assert.NotNull(T(conflict, "05@RDC").Conflict);
            Assert.Contains(conflict.Notes, n => n.Contains("début imposé avant la fin des tâches précédentes"));

            // Fin imposee avant le debut : duree conservee, signale.
            var bad2 = new PlanOverrides();
            bad2.Finishes["05@RDC"] = start;
            PlanTask b2 = T(WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start, bad2), "05@RDC");
            Assert.Equal(T(before, "05@RDC").DurationDays, b2.DurationDays);
            Assert.Contains("avant le début", b2.Conflict);
            Assert.False(o.IsEmpty);
            string json = System.Text.Json.JsonSerializer.Serialize(o);
            Assert.Contains("\"debuts\"", json);
            Assert.Contains("\"fins\"", json);
        }

        [Fact]
        public void Elements_hors_BOQ_rattaches_a_la_tache_de_leur_niveau_pour_la_4D()
        {
            var others = new List<ElementQuantity>
            {
                new ElementQuantity { ElementId = 9001, Category = "OST_GenericModel", Level = "RDC", Kind = "Materiau" },
                new ElementQuantity { ElementId = 9002, Category = "OST_Furniture", Level = "Étage 1", Kind = "Materiau" },
                new ElementQuantity { ElementId = 9003, Category = "OST_Doors", Level = "RDC", Kind = "Materiau" },
                new ElementQuantity { ElementId = 9004, Category = "OST_Furniture", Level = null, Kind = "Materiau" },
                new ElementQuantity { ElementId = 1, Category = "OST_Floors", Level = "Fondation", Kind = "Materiau" },   // deja dans une tache
            };
            WorkPlan plan = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), new DateTime(2027, 1, 4), null, others);
            PlanTask Of(long id) => plan.Tasks.Single(t => t.Elements.Contains(new ElementRef(id)));
            Assert.Equal("03@RDC", Of(9001).Id);           // modele generique -> gros oeuvre du niveau
            Assert.Equal("08@Étage 1", Of(9002).Id);       // mobilier -> finitions du niveau
            Assert.Equal("07@RDC", Of(9003).Id);           // porte -> menuiseries du niveau
            Assert.NotNull(Of(9004));                       // sans niveau : rattache quand meme
            Assert.Single(plan.Tasks.Where(t => t.Elements.Contains(new ElementRef(1))));   // jamais en double
            Assert.Contains(plan.Notes, n => n.Contains("rattaché"));
            // Tout est construit a la fin de la simulation.
            var all = plan.BuiltAt(plan.TotalDays);
            Assert.All(new long[] { 9001, 9002, 9003, 9004 }, id => Assert.Contains(new ElementRef(id), all));
        }

        [Fact]
        public void Jalons_reference_et_avancement_reel()
        {
            var start = new DateTime(2027, 1, 4);
            WorkPlan first = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start);
            Assert.Contains(first.Milestones, m => m.Name == "Fin gros œuvre — RDC");
            Assert.Contains(first.Milestones, m => m.Name.StartsWith("Hors d'eau"));
            Assert.Contains(first.Milestones, m => m.Name.StartsWith("Hors d'air"));
            Assert.Equal(first.FinishDate, first.Milestones.Single(m => m.Name == "Réception des travaux").Date);

            var o = new PlanOverrides();
            first.SaveBaselineTo(o, new DateTime(2027, 1, 1));
            o.Delays["02"] = 10;                                   // retard des fondations
            o.Progress["01"] = 40;                                 // terrassements a 40 %
            WorkPlan now = WorkPlanner.Build(RealisticBoq(), Levels, "RDC", Settings(), start, o);
            PlanTask T(string id) => now.Tasks.Single(t => t.Id == id);
            Assert.True(now.HasBaseline);
            Assert.True(T("03@RDC").SlipDays > 0);                 // glissement visible par rapport a la reference
            Assert.Equal(0, T("01").SlipDays);
            Assert.Equal(40, T("01").ActualPercent);
            Assert.True(T("01").IsLateAt(T("01").EndDay));        // prevu 100 %, reel 40 % : en retard
            Assert.False(T("01").IsLateAt(T("01").StartDay));
        }
    }
}
