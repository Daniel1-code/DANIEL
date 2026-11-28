using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Revisions
{
    public sealed class SnapshotLine
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public string Unit { get; set; }
        [JsonPropertyName("quantite")] public decimal Quantity { get; set; }
        [JsonPropertyName("pu")] public decimal? UnitPrice { get; set; }
        [JsonPropertyName("montant")] public decimal? Amount { get; set; }
        [JsonPropertyName("statut")] public string Reliability { get; set; }
    }

    public sealed class SnapshotLot
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("total")] public decimal Total { get; set; }
    }

    /// <summary>
    /// Instantane d'un devis tel qu'il a ete exporte (une « revision ») : de quoi le comparer
    /// plus tard a une nouvelle version, sans le modele de l'epoque.
    /// </summary>
    public sealed class BoqSnapshot
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("projet")] public string Project { get; set; }
        [JsonPropertyName("indice")] public string Revision { get; set; }
        [JsonPropertyName("date")] public DateTime Date { get; set; }
        [JsonPropertyName("version")] public string ApplicationVersion { get; set; }
        [JsonPropertyName("devise")] public string Currency { get; set; }
        [JsonPropertyName("empreinte")] public string Fingerprint { get; set; }
        [JsonPropertyName("totalTravaux")] public decimal WorksTotal { get; set; }
        [JsonPropertyName("totalHT")] public decimal TotalExcludingVat { get; set; }
        [JsonPropertyName("tva")] public decimal Vat { get; set; }
        [JsonPropertyName("totalTTC")] public decimal TotalIncludingVat { get; set; }
        [JsonPropertyName("complet")] public bool IsComplete { get; set; }
        [JsonPropertyName("lots")] public List<SnapshotLot> Lots { get; set; } = new List<SnapshotLot>();
        [JsonPropertyName("lignes")] public List<SnapshotLine> Lines { get; set; } = new List<SnapshotLine>();

        /// <summary>Libelle dans les listes : « Indice B — 22/11/2026 14:05 — 9 709,11 USD ».</summary>
        [JsonIgnore]
        public string Label => "Indice " + (string.IsNullOrWhiteSpace(Revision) ? "—" : Revision) + " — " +
                               Date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture) + " — " +
                               TotalIncludingVat.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " " + Currency +
                               (IsComplete ? string.Empty : " (incomplet)");

        [JsonIgnore] public string FilePath { get; set; }

        public static BoqSnapshot From(BoqResult boq, ProjectInfo project, string fingerprint, DateTime date) => new BoqSnapshot
        {
            Project = project?.Name,
            Revision = project?.Revision,
            Date = date,
            ApplicationVersion = ProductInfo.ApplicationVersion,
            Currency = boq.Currency.ToString(),
            Fingerprint = fingerprint,
            WorksTotal = boq.WorksTotal,
            TotalExcludingVat = boq.TotalExcludingVat,
            Vat = boq.Vat,
            TotalIncludingVat = boq.TotalIncludingVat,
            IsComplete = boq.IsComplete,
            Lots = boq.Lots.Select(l => new SnapshotLot { Code = l.Code, Designation = l.Designation, Total = l.Total }).ToList(),
            Lines = boq.AllLines.Select(l => new SnapshotLine
            {
                Code = l.Code, Lot = l.LotCode, Designation = l.Designation, Unit = l.Unit.Symbol(), Quantity = l.Quantity,
                UnitPrice = l.UnitPrice, Amount = l.Amount, Reliability = l.Reliability.ToString()
            }).ToList()
        };
    }

    /// <summary>Instantanes dans « &lt;projet&gt;_DanCI_revisions », a cote du fichier projet.</summary>
    public static class RevisionStore
    {
        public static string FolderFor(string projectFilePath)
        {
            string stem = Path.GetFileName(projectFilePath ?? "projet");
            if (stem.EndsWith(ProjectFile.Extension, StringComparison.OrdinalIgnoreCase)) stem = stem.Substring(0, stem.Length - ProjectFile.Extension.Length);
            return Path.Combine(Path.GetDirectoryName(projectFilePath ?? string.Empty) ?? string.Empty, stem + "_DanCI_revisions");
        }

        public static string Save(string folder, BoqSnapshot snapshot)
        {
            Directory.CreateDirectory(folder);
            string rev = string.IsNullOrWhiteSpace(snapshot.Revision) ? "sans-indice" : snapshot.Revision.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) rev = rev.Replace(c, '_');
            string path = Path.Combine(folder, "indice-" + rev + "_" + snapshot.Date.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture) + ".json");
            File.WriteAllText(path, JsonSerializer.Serialize(snapshot, BoqJson.Options), new UTF8Encoding(false));
            snapshot.FilePath = path;
            return path;
        }

        /// <summary>Instantanes lisibles, du plus recent au plus ancien.</summary>
        public static List<BoqSnapshot> List(string folder, List<BoqIssue> issues)
        {
            var list = new List<BoqSnapshot>();
            if (!Directory.Exists(folder)) return list;
            foreach (string f in Directory.GetFiles(folder, "*.json"))
            {
                BoqSnapshot s = ConfigurationLoader.ReadText<BoqSnapshot>(File.ReadAllText(f), Path.GetFileName(f), issues);
                if (s != null)
                {
                    s.FilePath = f;
                    list.Add(s);
                }
            }
            return list.OrderByDescending(s => s.Date).ToList();
        }
    }

    public enum ChangeKind
    {
        Unchanged,
        Modified,
        Added,
        Removed
    }

    public sealed class LineChange
    {
        public string Code { get; set; }
        public string Lot { get; set; }
        public string Designation { get; set; }
        public string Unit { get; set; }
        public decimal? QuantityA { get; set; }
        public decimal? QuantityB { get; set; }
        public decimal? UnitPriceA { get; set; }
        public decimal? UnitPriceB { get; set; }
        public decimal AmountA { get; set; }
        public decimal AmountB { get; set; }
        public decimal Delta => AmountB - AmountA;
        public ChangeKind Kind { get; set; }

        /// <summary>Ce qui a change : quantite, prix, les deux, ligne ajoutee ou supprimee.</summary>
        public string Cause
        {
            get
            {
                switch (Kind)
                {
                    case ChangeKind.Added: return "Ligne ajoutée";
                    case ChangeKind.Removed: return "Ligne supprimée";
                    case ChangeKind.Unchanged: return "—";
                }
                bool q = QuantityA != QuantityB, p = UnitPriceA != UnitPriceB;
                return q && p ? "Quantité et prix" : q ? "Quantité" : p ? "Prix" : "Désignation";
            }
        }
    }

    public sealed class LotChange
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal TotalA { get; set; }
        public decimal TotalB { get; set; }
        public decimal Delta => TotalB - TotalA;
    }

    public sealed class RevisionComparison
    {
        public BoqSnapshot A { get; set; }
        public BoqSnapshot B { get; set; }
        public List<LineChange> Lines { get; } = new List<LineChange>();
        public List<LotChange> Lots { get; } = new List<LotChange>();
        public decimal DeltaTtc => B.TotalIncludingVat - A.TotalIncludingVat;
        public decimal? DeltaPercent => A.TotalIncludingVat == 0m ? (decimal?)null : DeltaTtc / A.TotalIncludingVat;
        public bool SameCurrency => A.Currency == B.Currency;
        public bool SameModel => A.Fingerprint == B.Fingerprint;

        public string Summary()
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            string sign = DeltaTtc >= 0 ? "+" : string.Empty;
            return "Indice " + (A.Revision ?? "—") + " → " + (B.Revision ?? "—") + " : TTC " + sign + DeltaTtc.ToString("N2", fr) + " " + B.Currency +
                   (DeltaPercent.HasValue ? " (" + sign + (DeltaPercent.Value * 100m).ToString("0.0", fr) + " %)" : string.Empty) + " ; " +
                   Lines.Count(l => l.Kind == ChangeKind.Modified) + " ligne(s) modifiée(s), " +
                   Lines.Count(l => l.Kind == ChangeKind.Added) + " ajoutée(s), " + Lines.Count(l => l.Kind == ChangeKind.Removed) + " supprimée(s)" +
                   (SameModel ? " ; métré IDENTIQUE (seuls les prix ou paramètres ont changé)." : " ; métré différent (modèle modifié).") +
                   (SameCurrency ? string.Empty : " ATTENTION : devises différentes, écarts non significatifs.");
        }
    }

    /// <summary>Compare deux revisions, ligne par ligne (par code d'article) et lot par lot.</summary>
    public static class RevisionComparer
    {
        public static RevisionComparison Compare(BoqSnapshot a, BoqSnapshot b)
        {
            if (a == null) throw new ArgumentNullException(nameof(a));
            if (b == null) throw new ArgumentNullException(nameof(b));
            var result = new RevisionComparison { A = a, B = b };
            var linesA = a.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            var linesB = b.Lines.GroupBy(l => l.Code).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
            foreach (string code in linesA.Keys.Union(linesB.Keys).OrderBy(c => c, StringComparer.Ordinal))
            {
                linesA.TryGetValue(code, out SnapshotLine la);
                linesB.TryGetValue(code, out SnapshotLine lb);
                SnapshotLine any = lb ?? la;
                var change = new LineChange
                {
                    Code = code, Lot = any.Lot, Designation = any.Designation, Unit = any.Unit,
                    QuantityA = la?.Quantity, QuantityB = lb?.Quantity, UnitPriceA = la?.UnitPrice, UnitPriceB = lb?.UnitPrice,
                    AmountA = la?.Amount ?? 0m, AmountB = lb?.Amount ?? 0m
                };
                change.Kind = la == null ? ChangeKind.Added : lb == null ? ChangeKind.Removed :
                    la.Quantity != lb.Quantity || la.UnitPrice != lb.UnitPrice || la.Amount != lb.Amount || la.Designation != lb.Designation
                        ? ChangeKind.Modified : ChangeKind.Unchanged;
                result.Lines.Add(change);
            }
            var lotsA = a.Lots.ToDictionary(l => l.Code, StringComparer.Ordinal);
            var lotsB = b.Lots.ToDictionary(l => l.Code, StringComparer.Ordinal);
            foreach (string code in lotsA.Keys.Union(lotsB.Keys).OrderBy(c => c, StringComparer.Ordinal))
            {
                lotsA.TryGetValue(code, out SnapshotLot la);
                lotsB.TryGetValue(code, out SnapshotLot lb);
                result.Lots.Add(new LotChange { Code = code, Designation = (lb ?? la).Designation, TotalA = la?.Total ?? 0m, TotalB = lb?.Total ?? 0m });
            }
            return result;
        }
    }
}
