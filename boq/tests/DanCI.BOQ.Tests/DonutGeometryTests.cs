using System.Linq;
using DanCI.BOQ.Core.Dashboard;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class DonutGeometryTests
    {
        [Fact]
        public void Les_secteurs_couvrent_le_tour_complet_dans_l_ordre()
        {
            var arcs = DonutGeometry.Split(new[] { 0.5m, 0.25m, 0.25m });
            Assert.Equal(0.0, arcs[0].Start, 6);
            Assert.Equal(0.5, arcs[1].Start, 6);
            Assert.Equal(0.75, arcs[2].Start, 6);
            Assert.Equal(1.0, arcs.Sum(a => a.Sweep), 6);
        }

        [Fact]
        public void Parts_negatives_ou_nulles_ignorees_et_total_nul_sans_secteur()
        {
            var arcs = DonutGeometry.Split(new[] { 2m, -1m, 0m, 2m });
            Assert.Equal(0.5, arcs[0].Sweep, 6);
            Assert.Equal(0.0, arcs[1].Sweep, 6);
            Assert.Equal(0.5, arcs[3].Sweep, 6);
            Assert.All(DonutGeometry.Split(new[] { 0m, 0m }), a => Assert.Equal(0.0, a.Sweep));
        }

        [Fact]
        public void Premier_quart_part_de_midi_et_finit_a_trois_heures()
        {
            string path = DonutGeometry.Segment(100, 100, 90, 60, 0, 0.25);
            Assert.StartsWith("M 100,10 A 90,90 0 0 1 190,100 L 160,100 A 60,60 0 0 0 100,40 Z", path);
        }

        [Fact]
        public void Grand_arc_et_anneau_complet()
        {
            Assert.Contains(" 0 1 1 ", DonutGeometry.Segment(100, 100, 90, 60, 0, 0.75));
            string full = DonutGeometry.Segment(100, 100, 90, 60, 0, 1);
            Assert.Equal(2, full.Split('M').Length - 1);
            Assert.Equal(string.Empty, DonutGeometry.Segment(100, 100, 90, 60, 0, 0));
        }

        [Fact]
        public void Nombres_ecrits_avec_un_point_quelle_que_soit_la_culture()
        {
            var old = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("fr-FR");
                Assert.DoesNotContain(";", DonutGeometry.Segment(85, 85, 83, 55, 0.1, 0.33));
                Assert.Matches(@"^[MALZ0-9 .,\-]+$", DonutGeometry.Segment(85, 85, 83, 55, 0.1, 0.33));
            }
            finally
            {
                System.Globalization.CultureInfo.CurrentCulture = old;
            }
        }
    }
}
