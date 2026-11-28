using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Engine
{
    /// <summary>Un element modelise sur un niveau : ce qu'il est, ou il va dans le BOQ, ce qu'il mesure.</summary>
    public sealed class LevelElement
    {
        public ElementRef Ref { get; set; }
        /// <summary>Une des quantites de l'element (pour la categorie, le type, la famille, le lien).</summary>
        public ElementQuantity Sample { get; set; }
        public List<string> Articles { get; } = new List<string>();
        public List<string> Lots { get; } = new List<string>();
        /// <summary>Quantites nettes mesurees de l'element, cumulees par unite.</summary>
        public List<(BoqUnit Unit, decimal Value)> Quantities { get; } = new List<(BoqUnit, decimal)>();
        public List<string> Materials { get; } = new List<string>();
        public Reliability Reliability { get; set; }
        /// <summary>Faux : element modelise mais dans aucun article du BOQ (inventaire seulement).</summary>
        public bool InBoq => Articles.Count > 0;
    }

    /// <summary>
    /// Elements d'un niveau, un par ligne, pour les parcourir et les selectionner un a un dans Revit :
    /// ceux des articles du BOQ (eventuellement d'un seul article) et, sans filtre d'article, ceux de
    /// l'inventaire des materiaux qui ne sont dans aucun article. Ordre : categorie, type, ElementId.
    /// </summary>
    public static class LevelElements
    {
        public static List<LevelElement> Build(LevelBlock block, string articleCode = null)
        {
            var map = new Dictionary<ElementRef, LevelElement>();
            LevelElement Get(ElementQuantity q)
            {
                ElementRef r = ElementRef.Of(q);
                if (!map.TryGetValue(r, out LevelElement e)) map[r] = e = new LevelElement { Ref = r, Sample = q, Reliability = q.Reliability };
                return e;
            }
            if (block == null) return new List<LevelElement>();
            foreach (LevelLot lot in block.Lots)
            {
                foreach (LevelArticle a in lot.Articles.Where(x => articleCode == null || x.Code == articleCode))
                {
                    foreach (ElementQuantity q in a.Elements)
                    {
                        LevelElement e = Get(q);
                        if (!e.Articles.Contains(a.Code)) e.Articles.Add(a.Code);
                        if (!e.Lots.Contains(lot.Code)) e.Lots.Add(lot.Code);
                        if (!string.IsNullOrEmpty(q.Material) && !e.Materials.Contains(q.Material)) e.Materials.Add(q.Material);
                        int i = e.Quantities.FindIndex(x => x.Unit == q.Unit);
                        if (i < 0) e.Quantities.Add((q.Unit, (decimal)q.Value));
                        else e.Quantities[i] = (q.Unit, e.Quantities[i].Value + (decimal)q.Value);
                        if (q.Reliability > e.Reliability) e.Reliability = q.Reliability;
                    }
                }
            }
            if (articleCode == null)
            {
                foreach (var m in block.Materials)
                {
                    foreach (ElementQuantity q in m.Items)
                    {
                        LevelElement e = Get(q);
                        if (!string.IsNullOrEmpty(q.Material) && !e.Materials.Contains(q.Material)) e.Materials.Add(q.Material);
                    }
                }
            }
            return map.Values
                .OrderBy(e => e.Sample.Category, StringComparer.Ordinal)
                .ThenBy(e => e.Sample.TypeName, StringComparer.Ordinal)
                .ThenBy(e => e.Ref.ElementId)
                .ToList();
        }
    }
}
