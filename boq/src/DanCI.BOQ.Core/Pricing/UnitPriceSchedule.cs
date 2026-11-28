using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>Ligne du bordereau des prix unitaires : un article, ou une variante (famille / type / materiau) du modele.</summary>
    public sealed class BpuRow
    {
        public string Code { get; set; }
        /// <summary>Code de l'article du catalogue (une ligne ventilee par type a un code derive).</summary>
        public string ArticleCode { get; set; }
        public string LotCode { get; set; }
        public string LotDesignation { get; set; }
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }
        /// <summary>Quantite du devis (article) ou quantite nette (variante).</summary>
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        /// <summary>Prix unitaire en toutes lettres (vide si sans prix).</summary>
        public string UnitPriceInWords { get; set; }
        public string Source { get; set; }
        public bool IsVariant { get; set; }
        /// <summary>Variante avec SON prix dans la bibliotheque (sinon : prix general de l'article).</summary>
        public bool HasOwnPrice { get; set; }
        public string Family { get; set; }
        public string TypeName { get; set; }
        public string Material { get; set; }
        public int ElementCount { get; set; }
        public bool HasPrice => UnitPrice.HasValue;
        public Reliability Reliability { get; set; }
    }

    /// <summary>
    /// Bordereau des prix unitaires (BPU) DYNAMIQUE : construit a chaque analyse a partir de ce
    /// qui est REELLEMENT dans le modele. Un article apparait s'il a une quantite ; sous lui, une
    /// ligne par variante presente (famille / type / materiau Revit), avec le prix qui s'y applique
    /// (prix de la variante dans la bibliotheque, a defaut prix general de l'article). Un nouveau
    /// type ou materiau modelise apparait tout seul a l'analyse suivante, « à chiffrer » s'il n'a
    /// pas de prix. Prix en chiffres et en lettres.
    /// </summary>
    public static class UnitPriceSchedule
    {
        public static List<BpuRow> Build(BoqResult boq, PriceLibrary library, int maxVariants = 60)
        {
            var rows = new List<BpuRow>();
            if (boq == null) return rows;
            Currency cur = boq.Currency;
            foreach (BoqLotResult lot in boq.Lots)
            {
                foreach (BoqLine line in lot.Lines.Where(l => !l.IsUnclassified && l.Quantity != 0m))
                {
                    rows.Add(new BpuRow
                    {
                        Code = line.Code,
                        ArticleCode = line.ArticleCode,
                        LotCode = lot.Code,
                        LotDesignation = lot.Designation,
                        Designation = line.Designation,
                        Unit = line.Unit,
                        Quantity = line.Quantity,
                        UnitPrice = line.UnitPrice,
                        UnitPriceInWords = line.UnitPrice.HasValue ? AmountInWords.Format(line.UnitPrice.Value, cur) : string.Empty,
                        Source = line.UnitPrice.HasValue ? line.PriceSource : "À CHIFFRER : aucun prix dans la bibliothèque",
                        ElementCount = line.ElementRefs.Count,
                        Reliability = line.Reliability
                    });
                    if (line.IsManual) continue;
                    List<PricePart> parts = line.PriceParts ?? Variants(line, library, cur);
                    bool splitLine = line.ArticleCode != null && line.Code != line.ArticleCode;
                    // Une seule variante au prix general d'un article non ventile (ou sans nom) : rien a detailler.
                    if (parts.Count == 0) continue;
                    if (parts.Count == 1 && !parts[0].IsSpecific &&
                        (!splitLine || (string.IsNullOrEmpty(parts[0].Family) && string.IsNullOrEmpty(parts[0].TypeName) && string.IsNullOrEmpty(parts[0].Material)))) continue;
                    foreach (PricePart p in parts.Take(maxVariants))
                    {
                        rows.Add(new BpuRow
                        {
                            Code = line.Code,
                            ArticleCode = line.ArticleCode,
                            LotCode = lot.Code,
                            LotDesignation = lot.Designation,
                            Designation = "   – " + p.Label,
                            Unit = line.Unit,
                            Quantity = Math.Round(p.Quantity, 2),
                            UnitPrice = p.UnitPrice.HasValue ? Math.Round(p.UnitPrice.Value, 2) : (decimal?)null,
                            UnitPriceInWords = p.UnitPrice.HasValue ? AmountInWords.Format(Math.Round(p.UnitPrice.Value, 2), cur) : string.Empty,
                            Source = !p.UnitPrice.HasValue ? "À CHIFFRER" : p.IsSpecific ? p.Source + " (prix de cette variante)" : "Prix général de l'article",
                            IsVariant = true,
                            HasOwnPrice = p.IsSpecific,
                            Family = p.Family,
                            TypeName = p.TypeName,
                            Material = p.Material,
                            ElementCount = line.Contributions.Where(c => (c.Element.Family ?? string.Empty) == (p.Family ?? string.Empty) &&
                                                                         (c.Element.TypeName ?? string.Empty) == (p.TypeName ?? string.Empty) &&
                                                                         (c.Element.Material ?? string.Empty) == (p.Material ?? string.Empty))
                                                             .Select(c => ElementRef.Of(c.Element)).Distinct().Count(),
                            Reliability = line.Reliability
                        });
                    }
                }
            }
            return rows;
        }

        /// <summary>Variantes presentes dans une ligne sans prix specifiques : toutes au prix general (ou sans prix).</summary>
        private static List<PricePart> Variants(BoqLine line, PriceLibrary library, Currency cur) =>
            line.Contributions
                .GroupBy(c => (F: c.Element.Family ?? string.Empty, T: c.Element.TypeName ?? string.Empty, M: c.Element.Material ?? string.Empty))
                .OrderBy(g => g.Key.F, StringComparer.Ordinal).ThenBy(g => g.Key.T, StringComparer.Ordinal).ThenBy(g => g.Key.M, StringComparer.Ordinal)
                .Select(g => new PricePart
                {
                    Family = g.Key.F,
                    TypeName = g.Key.T,
                    Material = g.Key.M,
                    Quantity = g.Sum(c => (decimal)c.Element.Value),
                    UnitPrice = line.UnitPrice,
                    Source = line.PriceSource,
                    IsSpecific = false
                })
                .ToList();
    }
}
