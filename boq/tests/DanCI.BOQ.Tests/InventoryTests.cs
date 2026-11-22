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
    public class InventoryTests
    {
        private static ElementQuantity M(long id, string cat, string family, string type, string material, double area, double vol, string level,
            bool noMaterial = false)
        {
            var q = new ElementQuantity
            {
                ElementId = id, Category = cat, Family = family, TypeName = type, Material = material, Level = level,
                Kind = MaterialInventory.Kind, Unit = BoqUnit.CubicMeter, Value = vol,
                Parameters = new Dictionary<string, string> { [MaterialInventory.Area] = area.ToString(System.Globalization.CultureInfo.InvariantCulture) }
            };
            if (noMaterial) q.Parameters[MaterialInventory.NoMaterial] = "oui";
            return q;
        }

        internal static List<ElementQuantity> Sample() => new List<ElementQuantity>
        {
            M(10, "OST_StructuralFoundation", "Semelle isolée", "S1 120x120", "Béton C25/30", 5.76, 0.864, "Fondation"),
            M(11, "OST_StructuralFoundation", "Semelle isolée", "S1 120x120", "Béton C25/30", 5.76, 0.864, "Fondation"),
            M(20, "OST_StructuralColumns", "Poteau rectangulaire", "30x30", "Béton C25/30", 3.6, 0.27, "RDC"),
            M(30, "OST_Walls", "Mur de base", "Mur 25 isolé", "Bloc béton creux", 30, 4.5, "RDC"),
            M(30, "OST_Walls", "Mur de base", "Mur 25 isolé", "Laine de roche", 30, 1.8, "RDC"),
            M(31, "OST_Walls", "Mur de base", "Generic - 200mm", "Default Wall", 12, 2.4, "RDC"),
            M(40, "OST_GenericModel", "Bloc technique", "Type 1", "Béton C25/30", 2, 0.5, "RDC"),
            M(50, "OST_Furniture", "Comptoir", "Standard", MaterialInventory.NoMaterialLabel, 0, 0.8, "RDC", noMaterial: true),
            M(60, "OST_Floors", "Sol", "Dalle 20", MaterialInventory.ByCategoryLabel, 40, 2, "Étage 1"),
        };

        [Fact]
        public void Inventaire_par_materiau_tous_les_elements()
        {
            List<MaterialInventoryRow> rows = MaterialInventory.Build(Sample());
            MaterialInventoryRow concrete = rows.Single(r => r.Material == "Béton C25/30");
            Assert.Equal(4, concrete.ElementCount);
            Assert.Equal(2.498, concrete.VolumeM3, 9);
            Assert.Equal("Fondations, Modèles génériques, Poteaux", concrete.Categories);
            Assert.Equal("Fondation, RDC", concrete.Levels);
            Assert.False(concrete.IsGeneric);
            Assert.True(rows.Single(r => r.Material == "Default Wall").IsGeneric);
            Assert.True(rows.Single(r => r.Material == MaterialInventory.NoMaterialLabel).IsGeneric);
            Assert.True(rows.Single(r => r.Material == MaterialInventory.ByCategoryLabel).IsGeneric);
            Assert.Equal(Sample().Sum(q => q.Value), rows.Sum(r => r.VolumeM3), 9);
        }

        [Fact]
        public void Elements_generiques_tous_signales_avec_la_raison()
        {
            List<GenericFinding> f = MaterialInventory.FindGeneric(Sample());
            Assert.Contains(f, x => x.Code == "MODELE_GENERIQUE" && x.Element.ElementId == 40);
            Assert.Contains(f, x => x.Code == "TYPE_GENERIQUE" && x.Element.ElementId == 31);
            Assert.Contains(f, x => x.Code == "MATERIAU_GENERIQUE" && x.Element.ElementId == 31);
            Assert.Contains(f, x => x.Code == "SANS_MATERIAU" && x.Element.ElementId == 50);
            Assert.Contains(f, x => x.Code == "MATERIAU_PAR_CATEGORIE" && x.Element.ElementId == 60);
            Assert.DoesNotContain(f, x => x.Element.ElementId == 10 || x.Element.ElementId == 30 || x.Element.ElementId == 20);
            // Audit : un constat par raison et par type, avec ses elements.
            List<BoqIssue> issues = MaterialInventory.ToIssues(f);
            Assert.All(issues, i => Assert.StartsWith("ELEMENT_GENERIQUE_", i.Code));
            Assert.Contains(issues, i => i.Code == "ELEMENT_GENERIQUE_TYPE_GENERIQUE" && i.ElementIds.SequenceEqual(new long[] { 31 }));
        }

        [Fact]
        public void Motifs_generiques_lus_dans_la_configuration()
        {
            var settings = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction.Inventory;
            Assert.True(settings.Enabled);
            Assert.True(settings.GenericNames.IsMatch("Mur générique 200"));
            Assert.Contains("OST_GenericModel", settings.GenericCategories);
        }

        [Fact]
        public void Tous_les_materiaux_niveau_par_niveau()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            List<LevelBlock> blocks = LevelBreakdown.Build(ctx.Boq, new[] { "Fondation", "RDC", "Étage 1", "Toiture" }, null, Sample());
            Assert.Equal(new[] { "Béton C25/30" }, blocks.Single(b => b.Level == "Fondation").Materials.Select(m => m.Material));
            Assert.Contains(blocks.Single(b => b.Level == "RDC").Materials, m => m.Material == "Laine de roche");
        }

        [Fact]
        public void Feuilles_Excel_inventaire_et_generiques()
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Inventory = Sample();
            string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "danci_inv_" + System.Guid.NewGuid().ToString("N") + ".xlsx");
            BoqWorkbookWriter.Write(ctx, path);
            using (var wb = new XLWorkbook(path))
            {
                string inv = string.Join("|", wb.Worksheet(BoqWorkbookWriter.InventorySheetName).CellsUsed().Select(c => c.GetString()));
                Assert.Contains("Laine de roche", inv);
                Assert.Contains("⚠ à préciser", inv);
                string gen = string.Join("|", wb.Worksheet(BoqWorkbookWriter.GenericSheetName).CellsUsed().Select(c => c.GetString()));
                Assert.Contains("Modèle générique", gen);
                Assert.Contains("Generic - 200mm", gen);
                Assert.Contains("Aucun matériau", gen);
            }
            string keep = System.Environment.GetEnvironmentVariable("DANCI_KEEP_INV");
            if (!string.IsNullOrEmpty(keep)) System.IO.File.Copy(path, keep, true);
            System.IO.File.Delete(path);
        }
    }
}
