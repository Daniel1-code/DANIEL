using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;
using Xunit;

namespace DanCI.BOQ.Tests
{
    /// <summary>Parametres du projet > Prix unitaires : meme liste que le BPU (ce qui est dans le modele).</summary>
    public class ProjectPriceRowsTests
    {
        private static ElementQuantity Concrete(long id, string material, double m3, string type = "Poteau 30x30") => new ElementQuantity
        {
            ElementId = id, Category = "OST_StructuralColumns", Kind = QuantityKinds.ConcreteVolume, Unit = BoqUnit.CubicMeter,
            Value = m3, Material = material, Family = "Poteau béton", TypeName = type, Level = "RDC", MethodCode = "T"
        };

        private static PriceEntry P(string code, decimal pu, string material = null) => new PriceEntry
        {
            Code = code, UnitPrice = pu, Currency = Currency.USD, Unit = BoqUnit.CubicMeter, Source = "test",
            Material = material == null ? null : new PatternList(new[] { material })
        };

        private static (BoqResult Boq, BoqConfiguration Config) Model(params PriceEntry[] prices)
        {
            BoqConfiguration c = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            c.Prices = new PriceLibrary { SchemaVersion = 1, Name = "test", Prices = prices.ToList() };
            BoqResult boq = new BoqEngine().Build(new[]
            {
                Concrete(1, "Béton C25/30", 10), Concrete(2, "Béton C25/30 hydrofuge", 5), Concrete(3, "Béton C25/30", 1, "Poteau 40x40")
            }, c);
            return (boq, c);
        }

        [Fact]
        public void Seuls_les_articles_et_variantes_du_modele_sont_dans_le_modele()
        {
            var (boq, c) = Model(P("03.01", 100m), P("03.01", 130m, "Béton C25/30 hydrofuge"));
            List<PriceEdit> rows = PriceEditor.RowsFromModel(boq, c.Mapping, c.Prices);
            var model = rows.Where(r => r.InModel).ToList();
            Assert.Equal(model.Select(r => r.Code).Distinct(), new[] { "03.01" });
            PriceEdit article = model.Single(r => !r.IsVariant);
            Assert.Equal(100m, article.UnitPrice);
            Assert.Equal(16m, article.ModelQuantity);
            var variants = model.Where(r => r.IsVariant).ToList();
            Assert.Equal(3, variants.Count);
            // Prix par materiau seul (vaut pour plusieurs variantes) : indique dans « Prix appliqué », pas recopie.
            PriceEdit hydro = variants.Single(v => v.Material == "Béton C25/30 hydrofuge");
            Assert.Null(hydro.UnitPrice);
            Assert.Contains("130", hydro.Applied);
            Assert.Contains("pour ce type / matériau", hydro.Applied);
            PriceEdit follows = variants.Single(v => v.TypeName == "Poteau 40x40");
            Assert.Null(follows.UnitPrice);
            Assert.Contains("prix général de l'article", follows.Applied);
            // Le reste du catalogue est present (prix a l'avance) mais HORS modele.
            Assert.True(rows.Count(r => !r.InModel) > 10);
            Assert.DoesNotContain(rows.Where(r => !r.InModel), r => r.Code == "03.01" && !r.IsVariant);
            Assert.Equal(rows.Count, rows.Select(r => r.Key).Distinct().Count());
        }

        [Fact]
        public void Prix_saisi_sur_une_variante_est_relu_dans_sa_ligne()
        {
            var exact = new PriceEntry
            {
                Code = "03.01", UnitPrice = 150m, Currency = Currency.USD, Unit = BoqUnit.CubicMeter, Source = "saisie",
                Family = new PatternList(new[] { "Poteau béton" }), TypeName = new PatternList(new[] { "Poteau 40x40" }),
                Material = new PatternList(new[] { "Béton C25/30" })
            };
            var (boq, c) = Model(P("03.01", 100m), exact);
            PriceEdit v = PriceEditor.RowsFromModel(boq, c.Mapping, c.Prices).Single(r => r.TypeName == "Poteau 40x40");
            Assert.Equal(150m, v.UnitPrice);
            Assert.Null(v.Applied);
        }

        [Fact]
        public void Sans_analyse_tout_est_hors_modele()
        {
            var (_, c) = Model();
            List<PriceEdit> rows = PriceEditor.RowsFromModel(null, c.Mapping, c.Prices);
            Assert.NotEmpty(rows);
            Assert.All(rows, r => Assert.False(r.InModel));
        }

        [Fact]
        public void Enregistrer_sans_rien_changer_ne_modifie_pas_les_prix_et_un_prix_de_variante_est_ajoute()
        {
            var (boq, c) = Model(P("03.01", 100m), P("03.01", 130m, "*hydrofuge*"));
            List<PriceEdit> rows = PriceEditor.RowsFromModel(boq, c.Mapping, c.Prices);
            var issues = new List<BoqIssue>();
            PriceLibrary same = PriceEditor.Apply(c.Prices, rows, issues);
            Assert.Empty(issues);
            Assert.Equal(2, same.Prices.Count); // le prix « *hydrofuge* » (motif) n'est pas duplique

            rows.Single(r => r.TypeName == "Poteau 40x40" && r.Material == "Béton C25/30").UnitPrice = 140m;
            PriceLibrary updated = PriceEditor.Apply(c.Prices, rows, issues);
            Assert.Empty(issues);
            Assert.Equal(3, updated.Prices.Count);
            Assert.Equal(140m, updated.Find("03.01", "Poteau béton", "Poteau 40x40", null, "Béton C25/30").UnitPrice);
            Assert.Equal(100m, updated.Find("03.01", "Poteau béton", "Poteau 30x30", null, "Béton C25/30").UnitPrice);
        }
    }
}
