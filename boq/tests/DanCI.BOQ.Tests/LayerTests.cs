using System.Collections.Generic;
using System.IO;
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
    public class LayerTests
    {
        private static LayerDefinition L(int i, string f, string mat, double mm) =>
            new LayerDefinition { Index = i, Function = f, MaterialKey = mat, ThicknessMm = mm };

        [Fact]
        public void Materiau_dans_une_seule_couche_quantites_Revit_telles_quelles()
        {
            var s = LayerSplit.Apply(new[] { L(2, LayerFunctions.Insulation, "12", 60) }, 25.4, 1.524).Single();
            Assert.Equal(25.4, s.AreaM2, 9);
            Assert.Equal(1.524, s.VolumeM3, 9);
            Assert.False(s.Split);
            Assert.False(s.AreaFromVolume);
        }

        [Fact]
        public void Materiau_dans_deux_couches_volume_au_prorata_des_epaisseurs()
        {
            var s = LayerSplit.Apply(new[] { L(1, LayerFunctions.Finish1, "7", 15), L(5, LayerFunctions.Finish2, "7", 5) }, 40, 0.8);
            Assert.Equal(0.6, s[0].VolumeM3, 9);
            Assert.Equal(0.2, s[1].VolumeM3, 9);
            Assert.Equal(20, s[0].AreaM2, 9);
            Assert.All(s, x => Assert.True(x.Split));
            Assert.Equal(0.8, s.Sum(x => x.VolumeM3), 9);   // rien n'est cree ni perdu
        }

        [Fact]
        public void Surface_nulle_deduite_du_volume_et_de_l_epaisseur()
        {
            var s = LayerSplit.Apply(new[] { L(3, LayerFunctions.Structure, "9", 200) }, 0, 3.0).Single();
            Assert.Equal(15.0, s.AreaM2, 9);
            Assert.True(s.AreaFromVolume);
        }

        [Fact]
        public void Fonctions_Revit_traduites()
        {
            Assert.Equal(LayerFunctions.Structure, LayerFunctions.FromRevit("Structure"));
            Assert.Equal(LayerFunctions.Structure, LayerFunctions.FromRevit("StructuralDeck"));
            Assert.Equal(LayerFunctions.Air, LayerFunctions.FromRevit("ThermalOrAir"));
            Assert.Equal(LayerFunctions.Insulation, LayerFunctions.FromRevit("Insulation"));
            Assert.Equal(LayerFunctions.Other, LayerFunctions.FromRevit("Inconnu"));
        }

        internal static List<ElementQuantity> SampleLayers()
        {
            ElementQuantity Q(long id, string cat, string type, string mat, string fn, double mm, double area, double vol, string idx,
                Reliability r = Reliability.Measured) => new ElementQuantity
            {
                ElementId = id, Category = cat, TypeName = type, Material = mat, Level = "00-RDC", Kind = QuantityKinds.Layer,
                Unit = BoqUnit.CubicMeter, Value = vol, Reliability = r, MethodDescription = "test",
                Parameters = new Dictionary<string, string>
                {
                    [LayerFunctions.Parameter] = fn,
                    [LayerSummary.Thickness] = mm.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [LayerSummary.Area] = area.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    [LayerSummary.Index] = idx
                }
            };
            return new List<ElementQuantity>
            {
                Q(1, "OST_Walls", "Mur 25 isolé", "Enduit ciment", LayerFunctions.Finish1, 15, 30, 0.45, "1/4"),
                Q(1, "OST_Walls", "Mur 25 isolé", "Bloc béton creux", LayerFunctions.Structure, 150, 30, 4.5, "2/4"),
                Q(1, "OST_Walls", "Mur 25 isolé", "Laine de roche", LayerFunctions.Insulation, 60, 30, 1.8, "3/4"),
                Q(1, "OST_Walls", "Mur 25 isolé", "Plaque de plâtre", LayerFunctions.Finish2, 13, 30, 0.39, "4/4"),
                Q(2, "OST_Walls", "Mur 25 isolé", "Laine de roche", LayerFunctions.Insulation, 60, 12, 0.72, "3/4"),
                Q(3, "OST_Floors", "Dalle 20 + chape", "Béton C25/30", LayerFunctions.Structure, 200, 80, 16, "1/2"),
                Q(3, "OST_Floors", "Dalle 20 + chape", "Chape ciment", LayerFunctions.Substrate, 50, 80, 4, "2/2", Reliability.Estimated),
            };
        }

        [Fact]
        public void Recapitulatif_par_materiau_et_fonction()
        {
            List<LayerSummaryRow> rows = LayerSummary.Build(SampleLayers());
            Assert.Equal(6, rows.Count);
            Assert.Equal(LayerFunctions.Structure, rows[0].Function);                    // porteur d'abord
            LayerSummaryRow wool = rows.Single(r => r.Material == "Laine de roche");
            Assert.Equal(42, wool.AreaM2, 9);
            Assert.Equal(2.52, wool.VolumeM3, 9);
            Assert.Equal(2, wool.ElementCount);
            Assert.Equal("60 mm", wool.Thicknesses);
            Assert.Equal("Murs", wool.Categories);
            Assert.Equal(Reliability.Estimated, rows.Single(r => r.Material == "Chape ciment").Reliability);
            Assert.Equal(SampleLayers().Sum(q => q.Value), rows.Sum(r => r.VolumeM3), 9);
        }

        [Fact]
        public void Feuille_Excel_materiaux_par_couche()
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Layers = SampleLayers();
            string path = Path.Combine(Path.GetTempPath(), "danci_couches_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                Assert.True(wb.TryGetWorksheet(BoqWorkbookWriter.LayersSheetName, out IXLWorksheet ws));
                string all = string.Join("|", ws.CellsUsed().Select(c => c.GetString()));
                Assert.Contains("Laine de roche", all);
                Assert.Contains("RÉCAPITULATIF PAR MATÉRIAU", all);
                Assert.Contains("DÉTAIL PAR ÉLÉMENT ET PAR COUCHE", all);
            }
            // Sans couches : pas de feuille (classeur inchange).
            ctx.Layers = new ElementQuantity[0];
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                Assert.False(wb.TryGetWorksheet(BoqWorkbookWriter.LayersSheetName, out _));
            }
            string keep = System.Environment.GetEnvironmentVariable("DANCI_KEEP_XLSX");
            if (!string.IsNullOrEmpty(keep)) { ctx.Layers = SampleLayers(); BoqWorkbookWriter.Write(ctx, keep); }
            File.Delete(path);
        }

        [Fact]
        public void Isolation_au_devis_article_08_07_ventile_par_isolant()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            Assert.True(config.Extraction.Layers.Enabled);
            Assert.True(config.Extraction.Layers.InsulationMaterials.IsMatch("Laine de verre"));
            Assert.False(config.Extraction.Layers.InsulationMaterials.IsMatch("Bloc béton creux"));
            var q = new List<ElementQuantity>
            {
                new ElementQuantity { ElementId = 1, Kind = QuantityKinds.Insulation, Category = "OST_Walls", Material = "Laine de roche", Unit = BoqUnit.SquareMeter, Value = 30,
                    Parameters = new Dictionary<string, string> { ["Isolant"] = "Laine de roche — 60 mm" } },
                new ElementQuantity { ElementId = 4, Kind = QuantityKinds.Insulation, Category = "OST_Roofs", Material = "Polystyrène XPS", Unit = BoqUnit.SquareMeter, Value = 95,
                    Parameters = new Dictionary<string, string> { ["Isolant"] = "Polystyrène XPS — 80 mm" } },
            };
            BoqResult boq = new BoqEngine().Build(q, config);
            List<BoqLine> lines = boq.AllLines.Where(l => l.ArticleCode == "08.07").ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(125m, lines.Sum(l => l.NetQuantity));
            Assert.DoesNotContain(boq.AllLines, l => l.IsUnclassified);
        }
    }
}
