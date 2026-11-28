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
    public class UnitPriceScheduleTests
    {
        private static ElementQuantity Concrete(long id, string material, double m3, string type = "Poteau 30x30") => new ElementQuantity
        {
            ElementId = id, Category = "OST_StructuralColumns", Kind = QuantityKinds.ConcreteVolume, Unit = BoqUnit.CubicMeter,
            Value = m3, Material = material, Family = "Poteau béton", TypeName = type, Level = "RDC", MethodCode = "T"
        };

        private static BoqConfiguration Config(params PriceEntry[] prices)
        {
            BoqConfiguration c = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            c.Prices = new PriceLibrary { SchemaVersion = 1, Name = "test", Prices = prices.ToList() };
            return c;
        }

        private static PriceEntry P(string code, decimal pu, string material = null, string type = null) => new PriceEntry
        {
            Code = code, UnitPrice = pu, Currency = Currency.USD, Unit = BoqUnit.CubicMeter, Source = "test",
            Material = material == null ? null : new PatternList(new[] { material }),
            TypeName = type == null ? null : new PatternList(new[] { type })
        };

        [Fact]
        public void Prix_selon_le_materiau_du_modele_PU_pondere()
        {
            var config = Config(P("03.01", 100m), P("03.01", 130m, "*C30/37*"));
            config.Settings.Currency = Currency.USD;
            var q = new[] { Concrete(1, "Béton C25/30", 10), Concrete(2, "Béton C30/37 — C25/30", 5) };
            // Le second materiau contient les deux motifs : le prix specifique s'applique (le plus precis gagne).
            BoqResult boq = new BoqEngine().Build(q, config);
            BoqLine line = boq.AllLines.Single(l => l.ArticleCode == "03.01");
            Assert.NotNull(line.PriceParts);
            Assert.Equal(2, line.PriceParts.Count);
            Assert.Equal(110m, line.UnitPrice);                      // (10 x 100 + 5 x 130) / 15
            Assert.Contains("pondéré", line.PriceSource);
        }

        [Fact]
        public void Variante_sans_prix_ni_prix_general_ligne_sans_montant()
        {
            var config = Config(P("03.01", 130m, "*hydrofuge*"));
            BoqResult boq = new BoqEngine().Build(new[] { Concrete(1, "Béton C25/30", 10), Concrete(2, "Béton C25/30 hydrofuge", 5) }, config);
            BoqLine line = boq.AllLines.Single(l => l.ArticleCode == "03.01");
            Assert.Null(line.Amount);
            Assert.Contains(boq.Issues, i => i.Code == "PRIX_ABSENT" && i.Message.Contains("C25/30"));
        }

        [Fact]
        public void Le_prix_le_plus_precis_gagne()
        {
            var lib = new PriceLibrary { Prices = { P("05.03", 10m), P("05.03", 12m, "*brique*"), P("05.03", 15m, "*brique*", "*20*") } };
            Assert.Equal(15m, lib.Find("05.03", null, "Mur 20", null, "Brique pleine").UnitPrice);
            Assert.Equal(12m, lib.Find("05.03", null, "Mur 15", null, "Brique pleine").UnitPrice);
            Assert.Equal(10m, lib.Find("05.03", null, "Mur 15", null, "Parpaing").UnitPrice);
            Assert.True(lib.HasSpecific("05.03"));
        }

        [Fact]
        public void Bordereau_dynamique_variantes_et_prix_en_lettres()
        {
            var config = Config(P("03.01", 100m), P("03.01", 130m, "*hydrofuge*"));
            BoqResult boq = new BoqEngine().Build(new[] { Concrete(1, "Béton C25/30", 10), Concrete(2, "Béton C25/30 hydrofuge", 5), Concrete(3, "Béton C25/30", 1, "Poteau 40x40") }, config);
            var rows = UnitPriceSchedule.Build(boq, config.Prices);
            BpuRow article = rows.Single(r => !r.IsVariant && r.ArticleCode == "03.01");
            Assert.Contains("dollar", article.UnitPriceInWords);
            var variants = rows.Where(r => r.IsVariant && r.ArticleCode == "03.01").ToList();
            Assert.Equal(3, variants.Count);
            Assert.Contains(variants, v => v.Material == "Béton C25/30 hydrofuge" && v.HasOwnPrice && v.UnitPrice == 130m);
            Assert.Contains(variants, v => v.TypeName == "Poteau 40x40" && !v.HasOwnPrice && v.UnitPrice == 100m);
            // Un nouveau materiau sans prix specifique apparait au prix general ; sans prix general : « à chiffrer ».
            var bare = Config(P("03.01", 130m, "*hydrofuge*"));
            var rows2 = UnitPriceSchedule.Build(new BoqEngine().Build(new[] { Concrete(1, "Béton C25/30", 10), Concrete(2, "Béton C25/30 hydrofuge", 5) }, bare), bare.Prices);
            Assert.Contains(rows2, r => r.IsVariant && r.Material == "Béton C25/30" && !r.HasPrice && r.Source == "À CHIFFRER");
        }

        [Fact]
        public void Saisie_d_un_prix_de_variante_ecrit_dans_la_bibliotheque()
        {
            var lib = new PriceLibrary { Prices = { P("03.01", 100m) } };
            var issues = new List<BoqIssue>();
            var edit = new PriceEdit { Code = "03.01", Unit = BoqUnit.CubicMeter, UnitPrice = 140m, Family = "Poteau béton", TypeName = "Poteau 30x30", Material = "Béton C30/37" };
            PriceLibrary after = PriceEditor.Apply(lib, new[] { edit }, issues);
            Assert.Empty(issues);
            Assert.Equal(2, after.Prices.Count);
            Assert.Equal(140m, after.Find("03.01", "Poteau béton", "Poteau 30x30", null, "Béton C30/37").UnitPrice);
            Assert.Equal(100m, after.Find("03.01", "Poteau béton", "Poteau 30x30", null, "Béton C25/30").UnitPrice);
            // Nouvelle saisie de la meme variante : remplacee, pas dupliquee ; PU vide : retiree.
            edit.UnitPrice = 150m;
            PriceLibrary again = PriceEditor.Apply(after, new[] { edit }, issues);
            Assert.Equal(2, again.Prices.Count);
            Assert.Equal(150m, again.Find("03.01", "Poteau béton", "Poteau 30x30", null, "Béton C30/37").UnitPrice);
            edit.UnitPrice = null;
            PriceLibrary removed = PriceEditor.Apply(again, new[] { edit }, issues);
            Assert.Single(removed.Prices);
            // Prix general et prix de variante saisis ensemble : pas de « doublon ».
            var both = PriceEditor.Apply(lib, new[] { new PriceEdit { Code = "03.01", UnitPrice = 105m }, new PriceEdit { Code = "03.01", UnitPrice = 135m, Material = "Béton C30/37" } }, issues);
            Assert.Empty(issues);
            Assert.Equal(105m, both.Find("03.01").UnitPrice);
        }
    }
}
