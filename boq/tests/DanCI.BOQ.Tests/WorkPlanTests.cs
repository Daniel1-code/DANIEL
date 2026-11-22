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
            if (!string.IsNullOrEmpty(keep)) System.IO.File.WriteAllText(keep, pages[0]);
        }

        [Fact]
        public void PDF_et_Excel_avec_le_planning()
        {
            var ctx = ExportTests.Context();
            ctx.Boq = RealisticBoq();
            ctx.LevelOrder = Levels;
            ctx.Schedule = new ProjectSchedule { StartDate = new DateTime(2027, 1, 4) };
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
    }
}
