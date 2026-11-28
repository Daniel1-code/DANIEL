using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Validation
{
    /// <summary>
    /// Empreinte du metre : SHA-256 d'une forme canonique des quantites (triees, valeurs a 6
    /// decimales). Deux analyses du meme fichier (Revit 2026 puis 2027) doivent donner la MEME
    /// empreinte ; sinon RunComparer dit quels elements different.
    /// 6 decimales = 1 mm³ sur un volume en m³, 0,001 g sur une masse en kg : au-dela, il ne
    /// s'agit plus de quantites mais du bruit de calcul en virgule flottante.
    /// </summary>
    public static class QuantityFingerprint
    {
        public const int Decimals = 6;

        public static IEnumerable<string> CanonicalLines(IEnumerable<ElementQuantity> quantities) =>
            quantities
                .Select(q => string.Join("|",
                    q.SourceDocument ?? string.Empty,
                    q.ElementId.ToString(CultureInfo.InvariantCulture),
                    q.Kind ?? string.Empty,
                    q.Material ?? string.Empty,
                    q.Unit.Symbol(),
                    Math.Round((decimal)q.Value, Decimals, MidpointRounding.AwayFromZero).ToString("0.000000", CultureInfo.InvariantCulture),
                    q.Reliability.ToString()))
                .OrderBy(l => l, StringComparer.Ordinal);

        public static string Compute(IEnumerable<ElementQuantity> quantities)
        {
            string canonical = string.Join("\n", CanonicalLines(quantities));
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonical));
                return string.Concat(hash.Select(b => b.ToString("x2", CultureInfo.InvariantCulture)));
            }
        }

        /// <summary>Forme courte lisible (12 caracteres), pour le PDF et la page de garde.</summary>
        public static string Short(string fingerprint) =>
            string.IsNullOrEmpty(fingerprint) ? string.Empty : fingerprint.Substring(0, Math.Min(12, fingerprint.Length)).ToUpperInvariant();
    }

    public sealed class RunDifference
    {
        public string Key { get; set; }
        public string Description { get; set; }
        public decimal? ValueA { get; set; }
        public decimal? ValueB { get; set; }
        public string Unit { get; set; }
        public string ReliabilityA { get; set; }
        public string ReliabilityB { get; set; }
        public string Nature { get; set; }
    }

    public sealed class RunComparisonReport
    {
        public string FingerprintA { get; set; }
        public string FingerprintB { get; set; }
        public int CountA { get; set; }
        public int CountB { get; set; }
        public bool Identical => FingerprintA == FingerprintB;
        public List<RunDifference> Differences { get; } = new List<RunDifference>();
    }

    /// <summary>Compare deux metres bruts du meme modele (ex. Revit 2026 / 2027), quantite par quantite.</summary>
    public static class RunComparer
    {
        public static RunComparisonReport Compare(IReadOnlyList<ElementQuantity> a, IReadOnlyList<ElementQuantity> b)
        {
            var report = new RunComparisonReport
            {
                FingerprintA = QuantityFingerprint.Compute(a),
                FingerprintB = QuantityFingerprint.Compute(b),
                CountA = a.Count,
                CountB = b.Count
            };
            Dictionary<string, List<ElementQuantity>> ia = Index(a), ib = Index(b);
            foreach (string key in ia.Keys.Union(ib.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                ia.TryGetValue(key, out List<ElementQuantity> qa);
                ib.TryGetValue(key, out List<ElementQuantity> qb);
                decimal? va = qa == null ? (decimal?)null : Round(qa.Sum(q => q.Value));
                decimal? vb = qb == null ? (decimal?)null : Round(qb.Sum(q => q.Value));
                string ra = qa == null ? null : string.Join(",", qa.Select(q => q.Reliability).Distinct());
                string rb = qb == null ? null : string.Join(",", qb.Select(q => q.Reliability).Distinct());
                if (va == vb && ra == rb)
                {
                    continue;
                }
                ElementQuantity any = (qa ?? qb).First();
                string nature;
                if (qa == null) nature = "Absent du métré A";
                else if (qb == null) nature = "Absent du métré B";
                else if (va != vb) nature = "Valeur différente";
                else nature = "Fiabilité différente";
                report.Differences.Add(new RunDifference
                {
                    Key = key,
                    Description = (any.CategoryLabel ?? any.Category) + " — " + any.TypeName + " — " + any.Kind,
                    ValueA = va,
                    ValueB = vb,
                    Unit = any.Unit.Symbol(),
                    ReliabilityA = ra,
                    ReliabilityB = rb,
                    Nature = nature
                });
            }
            return report;
        }

        private static decimal Round(double v) => Math.Round((decimal)v, QuantityFingerprint.Decimals, MidpointRounding.AwayFromZero);

        private static Dictionary<string, List<ElementQuantity>> Index(IEnumerable<ElementQuantity> qs) =>
            qs.GroupBy(q => (q.SourceDocument ?? string.Empty) + "|" + q.ElementId.ToString(CultureInfo.InvariantCulture) + "|" +
                            q.Kind + "|" + q.Material + "|" + q.Unit.Symbol())
              .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
    }
}
