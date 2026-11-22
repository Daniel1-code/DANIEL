using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Engine
{
    /// <summary>Un article du BOQ sur UN niveau.</summary>
    public sealed class LevelArticle
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }
        /// <summary>Quantite nette mesuree sur ce niveau (somme des elements du niveau).</summary>
        public decimal NetQuantity { get; set; }
        /// <summary>Quantite du devis (coefficients compris) attribuee a ce niveau.</summary>
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
        public int ElementCount { get; set; }
        public Reliability Reliability { get; set; }
        public BoqLine Line { get; set; }
        public List<ElementQuantity> Elements { get; } = new List<ElementQuantity>();
    }

    public sealed class LevelLot
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public List<LevelArticle> Articles { get; } = new List<LevelArticle>();
        public decimal Total => Articles.Sum(a => a.Amount ?? 0m);
        public bool IsComplete => Articles.All(a => a.Amount.HasValue);
    }

    /// <summary>Tout ce qui est modelise sur un niveau : lots, articles, materiaux des couches.</summary>
    public sealed class LevelBlock
    {
        public string Level { get; set; }
        public List<LevelLot> Lots { get; } = new List<LevelLot>();
        public List<LayerSummaryRow> Layers { get; } = new List<LayerSummaryRow>();
        public decimal Total => Lots.Sum(l => l.Total);
        public int ElementCount => Lots.SelectMany(l => l.Articles).SelectMany(a => a.Elements).Select(ElementRef.Of).Distinct().Count();
        public int ArticleCount => Lots.Sum(l => l.Articles.Count);
        public bool IsComplete => Lots.All(l => l.IsComplete);
        public bool IsWithoutLevel => Level == LevelBreakdown.NoLevel;
    }

    /// <summary>
    /// BOQ niveau par niveau. Chaque ligne du BOQ est repartie entre les niveaux de ses elements au
    /// prorata de leurs quantites NETTES (mesurees) : quantite du devis et montant suivent la meme
    /// part. Les arrondis (0,01) sont absorbes par la plus grosse part : la somme des niveaux
    /// redonne EXACTEMENT la ligne du BOQ. Les forfaits et les elements sans niveau vont dans
    /// « (sans niveau) ». Aucune quantite n'est creee ni perdue.
    /// </summary>
    public static class LevelBreakdown
    {
        public const string NoLevel = "(sans niveau / forfaits)";

        public static List<LevelBlock> Build(BoqResult boq, IEnumerable<string> levelOrder = null, IEnumerable<ElementQuantity> layers = null)
        {
            var blocks = new Dictionary<string, LevelBlock>(StringComparer.Ordinal);
            LevelBlock Block(string level)
            {
                if (!blocks.TryGetValue(level, out LevelBlock b)) blocks[level] = b = new LevelBlock { Level = level };
                return b;
            }

            foreach (BoqLotResult lot in boq?.Lots ?? new List<BoqLotResult>())
            {
                foreach (BoqLine line in lot.Lines)
                {
                    var byLevel = line.Contributions
                        .GroupBy(c => string.IsNullOrWhiteSpace(c.Element.Level) ? NoLevel : c.Element.Level)
                        .Select(g => (Level: g.Key, Net: g.Sum(c => (decimal)c.Element.Value), Items: g.Select(c => c.Element).ToList()))
                        .ToList();
                    if (byLevel.Count == 0)
                    {
                        byLevel.Add((NoLevel, line.NetQuantity, new List<ElementQuantity>()));
                    }
                    decimal netTotal = byLevel.Sum(x => x.Net);
                    List<decimal> shares = byLevel.Select(x => netTotal != 0m ? x.Net / netTotal : 1m / byLevel.Count).ToList();
                    List<decimal> quantities = Allocate(line.Quantity, shares, 2);
                    List<decimal> amounts = line.Amount.HasValue ? Allocate(line.Amount.Value, shares, 2) : null;
                    for (int i = 0; i < byLevel.Count; i++)
                    {
                        LevelBlock block = Block(byLevel[i].Level);
                        LevelLot l = block.Lots.FirstOrDefault(x => x.Code == lot.Code);
                        if (l == null) block.Lots.Add(l = new LevelLot { Code = lot.Code, Designation = lot.Designation });
                        var a = new LevelArticle
                        {
                            Code = line.Code,
                            Designation = line.Designation,
                            Unit = line.Unit,
                            NetQuantity = byLevel[i].Net,
                            Quantity = quantities[i],
                            UnitPrice = line.UnitPrice,
                            Amount = amounts?[i],
                            ElementCount = byLevel[i].Items.Select(ElementRef.Of).Distinct().Count(),
                            Reliability = byLevel[i].Items.Count == 0 ? line.Reliability : byLevel[i].Items.Max(e => e.Reliability),
                            Line = line
                        };
                        a.Elements.AddRange(byLevel[i].Items);
                        l.Articles.Add(a);
                    }
                }
            }

            if (layers != null)
            {
                foreach (var g in layers.Where(q => q.Kind == QuantityKinds.Layer)
                                        .GroupBy(q => string.IsNullOrWhiteSpace(q.Level) ? NoLevel : q.Level))
                {
                    Block(g.Key).Layers.AddRange(LayerSummary.Build(g));
                }
            }

            List<string> order = (levelOrder ?? Enumerable.Empty<string>()).ToList();
            return blocks.Values
                .OrderBy(b => b.IsWithoutLevel ? 2 : order.Contains(b.Level) ? 0 : 1)
                .ThenBy(b => order.IndexOf(b.Level))
                .ThenBy(b => b.Level, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>Repartit un total selon des parts, arrondi a « decimals » ; l'ecart va a la plus grosse part.</summary>
        public static List<decimal> Allocate(decimal total, IReadOnlyList<decimal> shares, int decimals)
        {
            var result = shares.Select(s => Math.Round(total * s, decimals, MidpointRounding.AwayFromZero)).ToList();
            if (result.Count == 0) return result;
            decimal gap = total - result.Sum();
            if (gap != 0m)
            {
                int biggest = 0;
                for (int i = 1; i < shares.Count; i++) if (Math.Abs(shares[i]) > Math.Abs(shares[biggest])) biggest = i;
                result[biggest] += gap;
            }
            return result;
        }

        /// <summary>Matrice lots x niveaux (montants), pour le recapitulatif.</summary>
        public static decimal Amount(LevelBlock block, string lotCode) =>
            block.Lots.Where(l => l.Code == lotCode).Sum(l => l.Total);
    }
}
