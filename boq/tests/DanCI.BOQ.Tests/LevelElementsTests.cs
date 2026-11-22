using System.Linq;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Export;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class LevelElementsTests
    {
        private static readonly string[] Levels = { "Fondation", "RDC", "Étage 1", "Toiture" };

        [Fact]
        public void Un_element_par_ligne_avec_ses_articles_et_ses_quantites()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            LevelBlock rdc = LevelBreakdown.Build(ctx.Boq, Levels, null, InventoryTests.Sample()).Single(b => b.Level == "RDC");
            var all = LevelElements.Build(rdc);
            Assert.Equal(all.Count, all.Select(e => e.Ref).Distinct().Count());                      // chaque element une fois
            Assert.All(all.Where(e => e.InBoq), e => Assert.All(e.Articles, a => Assert.Contains(rdc.Lots.SelectMany(l => l.Articles), x => x.Code == a)));
            Assert.Contains(all, e => !e.InBoq);                                                          // inventaire hors BOQ (mur, comptoir...)
            // Quantites de l'element = somme de ses contributions au niveau.
            foreach (LevelElement e in all.Where(x => x.InBoq))
            {
                double expected = rdc.Lots.SelectMany(l => l.Articles).SelectMany(a => a.Elements)
                    .Where(q => DanCI.BOQ.Core.Model.ElementRef.Of(q).Equals(e.Ref)).Sum(q => q.Value);
                Assert.Equal(expected, (double)e.Quantities.Sum(q => q.Value), 6);
            }
        }

        [Fact]
        public void Filtre_par_article()
        {
            ExportContext ctx = LevelBreakdownTests.Context();
            LevelBlock rdc = LevelBreakdown.Build(ctx.Boq, Levels).Single(b => b.Level == "RDC");
            string code = rdc.Lots.SelectMany(l => l.Articles).First().Code;
            var only = LevelElements.Build(rdc, code);
            Assert.NotEmpty(only);
            Assert.All(only, e => Assert.Equal(new[] { code }, e.Articles));
            Assert.Empty(LevelElements.Build(null));
        }
    }
}
