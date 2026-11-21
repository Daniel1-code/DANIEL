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

        public PriceEntry Find(string articleCode) =>
            Prices.FirstOrDefault(p => string.Equals(p.Code, articleCode, StringComparison.Ordinal));
    }
}
