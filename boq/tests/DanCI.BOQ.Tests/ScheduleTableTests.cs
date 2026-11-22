using System;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ScheduleTableTests
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        [Fact]
        public void Nomenclature_BOQ_toutes_les_lignes_et_les_totaux()
        {
            ExportContext ctx = ExportTests.Context();
            var rows = BoqScheduleTable.Boq(ctx.Boq, new DateTime(2026, 11, 22, 10, 0, 0));
            Assert.All(rows, r => Assert.Equal(BoqScheduleTable.Columns.Length, r.Cells.Length));
            Assert.Equal(BoqScheduleTable.Columns.Length, BoqScheduleTable.WidthsMm.Length);
            Assert.Equal(ScheduleRowKind.Header, rows[0].Kind);
            Assert.Equal(ctx.Boq.AllLines.Count(), rows.Count(r => r.Kind == ScheduleRowKind.Article));
            Assert.Equal(ctx.Boq.Lots.Count, rows.Count(r => r.Kind == ScheduleRowKind.Lot));
            Assert.Contains(rows, r => r.Cells[1] == "TOTAL TTC" && r.Cells[5].StartsWith(ctx.Boq.TotalIncludingVat.ToString("N2", Fr)));
            Assert.Contains(rows, r => r.Kind == ScheduleRowKind.Article && r.Cells[5] == "sans prix");
            Assert.Equal(ScheduleRowKind.Note, rows.Last().Kind);
            Assert.Contains("22/11/2026", rows.Last().Cells[1]);
        }

        [Fact]
        public void Nomenclature_par_niveau_un_bloc_par_niveau()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            var rows = BoqScheduleTable.ByLevel(ctx.Boq, new[] { "Fondation", "RDC", "Étage 1", "Toiture" }, DateTime.Now);
            Assert.Equal(new[] { "Fondation", "RDC", "Étage 1", "Toiture", LevelBreakdown.NoLevel },
                rows.Where(r => r.Kind == ScheduleRowKind.Level).Select(r => r.Cells[1]));
            Assert.Contains(rows, r => r.Cells[1] == "TOTAL TRAVAUX HT (tous niveaux)" && r.Cells[5].StartsWith(ctx.Boq.WorksTotal.ToString("N2", Fr)));
        }
    }
}
