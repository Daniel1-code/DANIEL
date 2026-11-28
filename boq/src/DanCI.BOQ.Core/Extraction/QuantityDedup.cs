using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Garde-fou contre le double comptage PAR L'EXTRACTION : deux quantites identiques (meme
    /// document, meme element, meme nature, meme unite, meme materiau, meme methode, meme ouvrage,
    /// meme couche, meme valeur) ne peuvent venir que d'un element parcouru deux fois. La seconde
    /// est retiree et signalee. Deux mesures differentes d'un meme element restent (beton + coffrage...).
    /// </summary>
    public static class QuantityDedup
    {
        public static int Apply(List<ElementQuantity> quantities, List<BoqIssue> notes)
        {
            if (quantities == null || quantities.Count == 0) return 0;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var removed = new List<ElementQuantity>();
            var keep = new List<ElementQuantity>(quantities.Count);
            foreach (ElementQuantity q in quantities)
            {
                if (seen.Add(Key(q))) keep.Add(q);
                else removed.Add(q);
            }
            if (removed.Count == 0) return 0;
            quantities.Clear();
            quantities.AddRange(keep);
            foreach (var g in removed.GroupBy(q => (q.SourceDocument ?? string.Empty, q.Kind)))
            {
                var issue = new BoqIssue(IssueSeverity.Warning, "QUANTITE_EN_DOUBLE",
                    g.Count() + " quantité(s) « " + g.Key.Item2 + " » relevée(s) deux fois pour le même élément" +
                    (g.Key.Item1.Length > 0 ? " [lien " + g.Key.Item1 + "]" : string.Empty) + " : le doublon a été retiré du métré (comptée une seule fois).")
                {
                    LinkInstanceId = ElementRef.Of(g.First()).LinkInstanceId
                };
                issue.ElementIds.AddRange(g.Select(q => q.ElementId).Distinct());
                notes?.Add(issue);
            }
            return removed.Count;
        }

        private static string Key(ElementQuantity q)
        {
            string P(string k) => q.Parameters != null && q.Parameters.TryGetValue(k, out string v) ? v : string.Empty;
            return string.Join("|", q.SourceDocument ?? string.Empty, q.ElementId.ToString(CultureInfo.InvariantCulture), q.Kind ?? string.Empty,
                q.Unit.ToString(), q.Material ?? string.Empty, q.MethodCode ?? string.Empty, P("Ouvrage"), P(LayerSummary.Index), P(ElementRef.LinkInstanceParameter),
                Math.Round(q.Value, 9).ToString("R", CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Superposition de deux elements de meme categorie (boites englobantes), types differents compris.</summary>
    public static class OverlapCheck
    {
        /// <summary>Part commune minimale (du plus petit volume) pour signaler un doublon probable.</summary>
        public const double Threshold = 0.90;
        /// <summary>Rapport minimal des volumes (petit / grand) : un doublon a la taille de l'original.</summary>
        public const double SizeRatio = 0.80;

        private static readonly HashSet<string> Ignored = new HashSet<string>(StringComparer.Ordinal)
        {
            "OST_Rooms", "OST_Areas", "OST_MEPSpaces", "OST_Rebar", "OST_Parts", "OST_Topography", "OST_Toposolid"
        };

        /// <summary>« x0,y0,z0;x1,y1,z1 » (mm) vers une boite ; null si illisible.</summary>
        public static double[] Parse(string position)
        {
            if (string.IsNullOrEmpty(position)) return null;
            var parts = position.Split(';');
            if (parts.Length != 2) return null;
            var a = parts[0].Split(',');
            var b = parts[1].Split(',');
            if (a.Length != 3 || b.Length != 3) return null;
            var v = new double[6];
            for (int i = 0; i < 3; i++)
            {
                if (!double.TryParse(a[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i]) ||
                    !double.TryParse(b[i], NumberStyles.Float, CultureInfo.InvariantCulture, out v[i + 3])) return null;
            }
            return v;
        }

        /// <summary>Volume commun / plus petit des deux volumes (0..1).</summary>
        public static double Overlap(double[] a, double[] b)
        {
            double dx = Math.Min(a[3], b[3]) - Math.Max(a[0], b[0]);
            double dy = Math.Min(a[4], b[4]) - Math.Max(a[1], b[1]);
            double dz = Math.Min(a[5], b[5]) - Math.Max(a[2], b[2]);
            if (dx <= 0 || dy <= 0 || dz <= 0) return 0;
            double va = (a[3] - a[0]) * (a[4] - a[1]) * (a[5] - a[2]);
            double vb = (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]);
            double small = Math.Min(va, vb);
            return small <= 0 ? 0 : dx * dy * dz / small;
        }

        private static double Ratio(double[] a, double[] b)
        {
            double va = (a[3] - a[0]) * (a[4] - a[1]) * (a[5] - a[2]);
            double vb = (b[3] - b[0]) * (b[4] - b[1]) * (b[5] - b[2]);
            return Math.Max(va, vb) <= 0 ? 0 : Math.Min(va, vb) / Math.Max(va, vb);
        }

        /// <summary>Paires d'elements de meme document et meme categorie qui se superposent a plus de 90 % (le plus petit dans le plus grand).</summary>
        public static List<(ElementQuantity A, ElementQuantity B, double Share)> Find(IEnumerable<ElementQuantity> quantities)
        {
            var elements = (quantities ?? Enumerable.Empty<ElementQuantity>())
                .Where(q => q.Category != null && !Ignored.Contains(q.Category) && q.Parameters != null && q.Parameters.ContainsKey("Position"))
                .GroupBy(q => (q.SourceDocument ?? string.Empty, q.ElementId)).Select(g => g.First())
                .Select(q => (Q: q, Box: Parse(q.Parameters["Position"])))
                .Where(x => x.Box != null && x.Box[3] > x.Box[0] && x.Box[4] > x.Box[1] && x.Box[5] > x.Box[2])
                .ToList();
            var result = new List<(ElementQuantity, ElementQuantity, double)>();
            foreach (var group in elements.GroupBy(x => (x.Q.SourceDocument ?? string.Empty, x.Q.Category)))
            {
                var list = group.OrderBy(x => x.Box[0]).ToList();
                for (int i = 0; i < list.Count; i++)
                {
                    for (int j = i + 1; j < list.Count && list[j].Box[0] < list[i].Box[3]; j++)
                    {
                        // Meme type et meme boite : deja signale par l'audit (DOUBLON_POSITION).
                        if (list[i].Q.TypeName == list[j].Q.TypeName && list[i].Q.Parameters["Position"] == list[j].Q.Parameters["Position"]) continue;
                        double share = Overlap(list[i].Box, list[j].Box);
                        if (share >= Threshold && Ratio(list[i].Box, list[j].Box) >= SizeRatio) result.Add((list[i].Q, list[j].Q, share));
                    }
                }
            }
            return result;
        }
    }
}
