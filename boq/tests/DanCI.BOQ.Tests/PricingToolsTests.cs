using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Export.Excel;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class DerivedQuantityTests
    {
        [Fact]
        public void Nombre_de_blocs_sur_la_quantite_brute_arrondi_au_superieur()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var wall = new ElementQuantity { ElementId = 1, Kind = QuantityKinds.Masonry, Category = "OST_Walls", TypeName = "Bloc creux 20 cm", Value = 37.45, Unit = BoqUnit.SquareMeter };
            BoqLine line = new BoqEngine().Build(new[] { wall }, config).AllLines.Single();

            Assert.Equal("05.01", line.Code);
            DerivedQuantity blocks = line.Derived.Single();
            // 37,45 m² x 1,05 (casse, coefficient livre) x 12,5 = 491,53 -> 492 blocs
            Assert.Equal(492m, blocks.Value);
            Assert.Contains("soit ≈ 492 U (blocs 40×20×20, 12,5 U/m²)", line.DesignationWithDerived);
            Assert.Null(line.Amount); // jamais valorisee
        }

        [Fact]
        public void Quantite_derivee_invalide_refusee()
        {
            var config = TestData.Config();
            config.Mapping.Articles[0].Derived.Add(new DerivedQuantityRule { Designation = "x", PerUnit = 0m });
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "DERIVEE_INVALIDE");
        }
    }

    public class PriceWorkbookTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-boq-prix-" + Guid.NewGuid().ToString("N"));

        public PriceWorkbookTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, true);

        [Fact]
        public void Modele_rempli_puis_importe_donne_une_bibliotheque_valide()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            string path = Path.Combine(dir, "prix.xlsx");
            PriceLibraryWorkbook.WriteTemplate(path, config.Mapping, config.Prices);

            using (var wb = new XLWorkbook(path))
            {
                IXLWorksheet ws = wb.Worksheet(PriceLibraryWorkbook.Sheet);
                Assert.Equal(config.Mapping.Articles.Count, ws.RowsUsed().Count(r => r.RowNumber() > 7));
                ws.Cell(2, 2).Value = "Kinshasa 2026 — client test";
                ws.Cell(5, 2).Value = new DateTime(2026, 11, 1);
                IXLRow Row(string code) => ws.RowsUsed().First(r => r.Cell(1).GetString() == code);
                IXLRow c = Row("03.01");
                c.Cell(6).Value = 245.5; c.Cell(7).Value = "USD"; c.Cell(8).Value = new DateTime(2026, 11, 1); c.Cell(9).Value = "Sous-détail n° 3";
                c.Cell(10).Value = 150; c.Cell(11).Value = 70; c.Cell(12).Value = 25.5;
                IXLRow d = Row("07.01");
                d.Cell(4).Value = "P90*"; d.Cell(6).Value = "250000"; d.Cell(7).Value = "CDF"; d.Cell(8).Value = "15/10/2026"; d.Cell(9).Value = "Devis menuisier";
                wb.Save();
            }

            var issues = new List<BoqIssue>();
            PriceLibrary lib = PriceLibraryWorkbook.Read(path, issues);
            Assert.Empty(issues);
            Assert.Equal(2, lib.Prices.Count); // les lignes sans PU ne sont pas importees
            Assert.Equal("Kinshasa 2026 — client test", lib.Name);
            PriceEntry concrete = lib.Find("03.01");
            Assert.Equal(245.5m, concrete.UnitPrice);
            Assert.Equal(245.5m, concrete.Breakdown.Total);
            PriceEntry door = lib.Find("07.01", "Porte", "P90 isoplane");
            Assert.Equal(Currency.CDF, door.Currency);
            Assert.Equal(new DateTime(2026, 10, 15), door.Date);

            // Bibliotheque JSON ecrite puis relue, validee contre le catalogue livre.
            string json = Path.Combine(dir, "prix.json");
            PriceLibraryStore.Save(json, lib);
            config.Prices = PriceLibraryStore.Load(json, issues);
            Assert.Empty(issues);
            Assert.DoesNotContain(ConfigurationValidator.Validate(config), i => i.Severity == IssueSeverity.Error && i.File == BoqConfiguration.PricesFileName);
        }

        [Fact]
        public void Devise_ou_unite_invalide_signalee_avec_la_ligne()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            string path = Path.Combine(dir, "prix.xlsx");
            PriceLibraryWorkbook.WriteTemplate(path, config.Mapping, null);
            using (var wb = new XLWorkbook(path))
            {
                IXLRow row = wb.Worksheet(PriceLibraryWorkbook.Sheet).Row(8);
                row.Cell(6).Value = 10; row.Cell(7).Value = "EUR";
                wb.Save();
            }
            var issues = new List<BoqIssue>();
            PriceLibraryWorkbook.Read(path, issues);
            Assert.Contains(issues, i => i.Code == "PRIX_DEVISE" && i.Message.Contains("ligne 8"));
        }

        [Fact]
        public void Chemin_de_bibliotheque_relatif_au_fichier_projet()
        {
            string projectPath = Path.Combine(dir, "Immeuble.danciboq.json");
            var file = new ProjectFile { PriceLibrary = Path.Combine("prix", "kinshasa.json") };
            Assert.Equal(Path.Combine(dir, "prix", "kinshasa.json"), file.ResolvePriceLibrary(projectPath));
            Assert.Null(new ProjectFile().ResolvePriceLibrary(projectPath));
        }
    }
}
