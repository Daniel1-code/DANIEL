using System.Collections.Generic;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class EstimatedFinishTests
    {
        private static ElementQuantity Q(long id, string cat, string kind, double v, string face = null, string level = "RDC", BoqUnit unit = BoqUnit.SquareMeter)
        {
            var q = new ElementQuantity
            {
                ElementId = id, Category = cat, Kind = kind, Value = v, Unit = unit, Level = level, Material = kind + (face ?? ""),
                MethodCode = "T", Reliability = face == null ? Reliability.Measured : Reliability.Estimated, Assumption = face == null ? null : "ESTIMÉ"
            };
            if (face != null) q.Parameters[EstimatedFinishes.Parameter] = face;
            return q;
        }

        [Fact]
        public void Finitions_estimees_vers_les_articles_du_lot_08()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            Assert.True(config.Extraction.EstimatedFinishes.Enabled);
            var list = new List<ElementQuantity>
            {
                Q(1, "OST_Walls", QuantityKinds.Plaster, 20, EstimatedFinishes.Exterior),
                Q(1, "OST_Walls", QuantityKinds.Plaster, 18, EstimatedFinishes.Interior),
                Q(1, "OST_Walls", QuantityKinds.Paint, 20, EstimatedFinishes.Exterior),
                Q(1, "OST_Walls", QuantityKinds.Paint, 18, EstimatedFinishes.Interior),
                Q(2, "OST_StructuralColumns", QuantityKinds.Plaster, 3, EstimatedFinishes.Interior),
                Q(3, "OST_Rooms", QuantityKinds.FloorFinish, 15, EstimatedFinishes.Floor),
                Q(3, "OST_Rooms", QuantityKinds.Plaster, 15, EstimatedFinishes.Ceiling),
                Q(3, "OST_Rooms", QuantityKinds.Paint, 15, EstimatedFinishes.Ceiling),
                Q(4, "OST_Rooms", QuantityKinds.FloorFinish, 12, EstimatedFinishes.WallTiles),
            };
            BoqResult boq = new BoqEngine().Build(list, config);
            decimal Qty(string code) => boq.AllLines.Where(l => l.Code == code).Sum(l => l.NetQuantity);
            Assert.Equal(20m, Qty("08.09"));   // crepissage facade
            Assert.Equal(21m, Qty("08.08"));   // enduit interieur murs + poteau
            Assert.Equal(15m, Qty("08.10"));   // enduit sous plafond
            Assert.Equal(18m, Qty("08.11"));
            Assert.Equal(20m, Qty("08.12"));
            Assert.Equal(15m, Qty("08.13"));
            Assert.Equal(15m, Qty("08.14"));   // carrelage de sol
            Assert.Equal(12m, Qty("08.15"));   // faience
            Assert.All(boq.AllLines.Where(l => l.Code.StartsWith("08.")), l => Assert.Equal(Reliability.Estimated, l.Reliability));
            Assert.Contains("10 cm", config.Mapping.Articles.Single(a => a.Code == "08.04").Designation);
        }

        [Fact]
        public void Pas_de_double_compte_avec_les_finitions_modelisees()
        {
            var list = new List<ElementQuantity>
            {
                Q(1, "OST_Walls", QuantityKinds.Plaster, 30),                                 // enduit modelise sur le mur 1
                Q(1, "OST_Walls", QuantityKinds.Plaster, 20, EstimatedFinishes.Interior),     // -> retire
                Q(1, "OST_Walls", QuantityKinds.Paint, 20, EstimatedFinishes.Interior),       // garde (pas de peinture modelisee)
                Q(2, "OST_Walls", QuantityKinds.Plaster, 10, EstimatedFinishes.Interior),     // garde
                Q(5, "OST_Floors", QuantityKinds.FloorFinish, 40, null, "Étage 1"),           // carrelage modelise a l'etage
                Q(6, "OST_Rooms", QuantityKinds.FloorFinish, 35, EstimatedFinishes.Floor, "Étage 1"), // -> retire
                Q(7, "OST_Rooms", QuantityKinds.FloorFinish, 25, EstimatedFinishes.Floor, "RDC"),     // garde
            };
            var notes = new List<BoqIssue>();
            int removed = EstimatedFinishes.Reconcile(list, notes);
            Assert.Equal(2, removed);
            Assert.DoesNotContain(list, q => q.ElementId == 1 && q.Kind == QuantityKinds.Plaster && EstimatedFinishes.IsEstimated(q));
            Assert.Contains(list, q => q.ElementId == 1 && q.Kind == QuantityKinds.Paint);
            Assert.DoesNotContain(list, q => q.ElementId == 6);
            Assert.Contains(list, q => q.ElementId == 7);
            Assert.All(notes, n => Assert.Equal("FINITION_ESTIMEE_NON_RETENUE", n.Code));
        }

        [Fact]
        public void Faience_perimetre_moins_portes_fois_hauteur()
        {
            Assert.Equal((10.0 - 0.8) * 2.10, EstimatedFinishes.WallTilingArea(10, new[] { 0.8 }, 2.10), 6);
            Assert.Equal(0, EstimatedFinishes.WallTilingArea(1, new[] { 2.0 }, 2.10));
            var wall = EstimatedFinishes.WallFaces(true).ToList();
            Assert.Equal(EstimatedFinishes.Exterior, wall[0].Face);
            Assert.Equal(EstimatedFinishes.Interior, wall[1].Face);
            Assert.All(EstimatedFinishes.WallFaces(false), f => Assert.Equal(EstimatedFinishes.Interior, f.Face));
            var s = new EstimatedFinishSettings();
            Assert.True(EstimatedFinishSettings.Matches(s.WetRooms, "Salle de bain 2"));
            Assert.True(EstimatedFinishSettings.Matches(s.NoFinishLevels, "Niveau 1 fondation"));
            Assert.False(EstimatedFinishSettings.Matches(s.NoFinishLevels, "RDC"));
        }

        [Fact]
        public void Corps_d_etat_gros_oeuvre_puis_second_oeuvre_dans_chaque_niveau()
        {
            Assert.Equal(Trades.Structure, Trades.Of("03"));
            Assert.Equal(Trades.Structure, Trades.Of("05"));
            Assert.Equal(Trades.Finishing, Trades.Of("08"));
            Assert.Equal(Trades.External, Trades.Of("11"));
            Assert.Equal("Autre", Trades.Of(new Lot { Code = "03", Trade = "Autre" }));
            ExportContext ctx = LevelBreakdownTests.Context();
            foreach (LevelBlock b in LevelBreakdown.Build(ctx.Boq, new[] { "Fondation", "RDC", "Étage 1", "Toiture" }))
            {
                var orders = b.Lots.Select(l => Trades.Order(l.Trade)).ToList();
                Assert.Equal(orders.OrderBy(x => x).ToList(), orders);
            }
        }

        [Fact]
        public void Excel_sous_totaux_par_corps_d_etat_egaux_au_total()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            ctx.LevelOrder = new[] { "Fondation", "RDC", "Étage 1", "Toiture" };
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "danci_trade_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                var ws = wb.Worksheet("BOQ");
                var cells = ws.CellsUsed().ToList();
                Assert.Contains(cells, c => c.GetString() == "GROS ŒUVRE");
                double trades = cells.Where(c => c.GetString().StartsWith("SOUS-TOTAL ")).Sum(c => ws.Cell(c.Address.RowNumber, 6).GetDouble());
                double levels = cells.Where(c => c.GetString().StartsWith("TOTAL NIVEAU ")).Sum(c => ws.Cell(c.Address.RowNumber, 6).GetDouble());
                Assert.Equal((double)ctx.Boq.WorksTotal, levels, 2);
                Assert.Equal(levels, trades, 2);
            }
            System.IO.File.Delete(path);
        }
    }
}
