using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class Excavation3DTests
    {
        [Fact]
        public void Rigole_le_long_d_un_mur_droit_volume_et_position()
        {
            var pts = new List<(double X, double Y)> { (0, 0), (10, 0) };
            List<Prism> p = Excavation3D.AlongPolyline(pts, 2, -3, 0, 0, 0);
            Assert.Single(p);
            Assert.Equal(10 * 2 * 3, Excavation3D.StraightVolume(p[0]), 9);
            Assert.Equal(-1, p[0].Bottom.Where((v, i) => i % 2 == 1).Min(), 9);   // largeur centree sur le trace
            Assert.Equal(1, p[0].Bottom.Where((v, i) => i % 2 == 1).Max(), 9);
        }

        [Fact]
        public void Croisements_deduits_aux_extremites_jointes_et_trace_brise()
        {
            var pts = new List<(double X, double Y)> { (0, 0), (10, 0), (10, 10) };
            List<Prism> p = Excavation3D.AlongPolyline(pts, 2, -1, 0, 1, 1);
            Assert.Equal(2, p.Count);
            Assert.Equal((9 + 9) * 2 * 1, p.Sum(Excavation3D.StraightVolume), 9);   // 20 m de trace − 2 × 1
        }

        [Fact]
        public void Fosse_talutee_autour_de_la_semelle()
        {
            Prism p = Excavation3D.Pit(0, 0, 2, 2, 0.5, 1, -2, 0);
            Assert.Equal(-0.5, p.Bottom[0], 9);
            Assert.Equal(-1.5, p.Top[0], 9);                                       // talus : plus large en tete
            Assert.Equal(-2, p.Zb, 9);
            Assert.Equal(0, p.Zt, 9);
        }

        [Fact]
        public void Encodage_aller_retour_et_texte_invalide_ignore()
        {
            List<Prism> p = Excavation3D.AlongPolyline(new List<(double X, double Y)> { (1.5, 2), (4, 6) }, 1.2, -2.6, 0.1, 0, 0);
            p.Add(Excavation3D.Pit(0, 0, 1, 1, 0.3, 0, -1, 0));
            List<Prism> back = Excavation3D.Decode(Excavation3D.Encode(p));
            Assert.Equal(2, back.Count);
            Assert.Equal(p[0].Bottom, back[0].Bottom.Select(v => v), new DoubleEq());
            Assert.Empty(Excavation3D.Decode("n'importe quoi;1,2,3"));
        }

        [Fact]
        public void VRD_range_au_niveau_bas_par_niveau()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            // Une ligne VRD dont les elements sont en etage.
            var levels = new[] { "Fondation", "RDC", "Étage 1", "Toiture" };
            var lot = new BoqLotResult { Code = "11", Designation = "VRD" };
            var line = new BoqLine { Code = "11.01", LotCode = "11", Designation = "Voirie", Unit = BoqUnit.SquareMeter, NetQuantity = 50, Quantity = 50, Amount = 500 };
            line.Contributions.Add(new LineContribution(new ElementQuantity { ElementId = 99, Level = "Étage 1", Value = 50, Unit = BoqUnit.SquareMeter }, "VRD"));
            lot.Lines.Add(line);
            ctx.Boq.Lots.Add(lot);
            var settings = new ExtractionSettings();
            settings.Excavation.NaturalGroundLevel = "RDC";
            string ground = LevelBreakdown.GroundLevel(levels, settings);
            Assert.Equal("RDC", ground);
            Assert.Equal("Fondation", LevelBreakdown.GroundLevel(levels, new ExtractionSettings()));
            var blocks = LevelBreakdown.Build(ctx.Boq, levels, null, null, null, ground);
            Assert.Contains(blocks.Single(b => b.Level == "RDC").Lots, l => l.Code == "11");
            Assert.DoesNotContain(blocks.Single(b => b.Level == "Étage 1").Lots, l => l.Code == "11");
        }

        private sealed class DoubleEq : IEqualityComparer<double>
        {
            public bool Equals(double a, double b) => System.Math.Abs(a - b) < 1e-3;
            public int GetHashCode(double d) => 0;
        }
    }
}
