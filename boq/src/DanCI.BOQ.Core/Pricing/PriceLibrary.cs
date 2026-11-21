using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Pricing
{
    public enum Currency
    {
        USD,
        CDF
    }

    /// <summary>Decomposition optionnelle d'un prix unitaire.</summary>
    public sealed class PriceBreakdown
    {
        [JsonPropertyName("materiaux")] public decimal Materials { get; set; }
        [JsonPropertyName("mainOeuvre")] public decimal Labour { get; set; }
        [JsonPropertyName("materiel")] public decimal Equipment { get; set; }

        public decimal Total => Materials + Labour + Equipment;
    }

    /// <summary>Prix unitaire d'un article dans une bibliotheque.</summary>
    public sealed class PriceEntry
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public BoqUnit Unit { get; set; }
        [JsonPropertyName("pu")] public decimal UnitPrice { get; set; }
        [JsonPropertyName("devise")] public Currency Currency { get; set; }
        [JsonPropertyName("date")] public DateTime? Date { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("decomposition")] public PriceBreakdown Breakdown { get; set; }

        /// <summary>
        /// Pour un article ventile par type : motif sur le nom du type Revit (et, si besoin, de
        /// la famille). Un prix avec critere de type passe avant le prix general de l'article.
        /// </summary>
        [JsonPropertyName("type")] public DanCI.BOQ.Core.Rules.PatternList TypeName { get; set; }
        [JsonPropertyName("famille")] public DanCI.BOQ.Core.Rules.PatternList Family { get; set; }

        public bool IsTypeSpecific =>
            (TypeName != null && !TypeName.IsEmpty) || (Family != null && !Family.IsEmpty);

        /// <summary>Cle d'unicite dans la bibliotheque.</summary>
        public string Key => Code + "|" + Family + "|" + TypeName;
    }

    /// <summary>
    /// Contenu de prix_unitaires.json : une bibliotheque (par client, par ville...).
    /// Aucun prix n'est code en dur dans l'extension : sans entree ici, la ligne reste sans
    /// montant et elle est signalee.
    /// </summary>
    public sealed class PriceLibrary
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("nom")] public string Name { get; set; }
        [JsonPropertyName("ville")] public string City { get; set; }
        [JsonPropertyName("client")] public string Client { get; set; }
        [JsonPropertyName("date")] public DateTime? Date { get; set; }
        [JsonPropertyName("prix")] public List<PriceEntry> Prices { get; set; } = new List<PriceEntry>();

        public PriceEntry Find(string articleCode) => Find(articleCode, null, null);

        /// <summary>
        /// Prix d'un article ; pour une sous-ligne par type, le premier prix dont les criteres de
        /// type et de famille correspondent, a defaut le prix general (sans critere).
        /// </summary>
        public PriceEntry Find(string articleCode, string family, string typeName)
        {
            var candidates = Prices.Where(p => string.Equals(p.Code, articleCode, StringComparison.Ordinal)).ToList();
            if (typeName != null || family != null)
            {
                PriceEntry specific = candidates.FirstOrDefault(p => p.IsTypeSpecific &&
                    (p.TypeName == null || p.TypeName.IsMatch(typeName)) &&
                    (p.Family == null || p.Family.IsMatch(family)));
                if (specific != null)
                {
                    return specific;
                }
            }
            return candidates.FirstOrDefault(p => !p.IsTypeSpecific);
        }
    }
}
