using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Ligne saisie a la main (installation de chantier, etudes, essais...) : elle ne vient pas
    /// du modele et n'est donc ni mesuree ni estimee. Conservee dans le fichier projet
    /// .danciboq.json (etape 7) pour survivre a une reanalyse.
    /// </summary>
    public sealed class ManualLine
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public BoqUnit Unit { get; set; } = BoqUnit.LumpSum;
        [JsonPropertyName("quantite")] public decimal Quantity { get; set; } = 1m;
        [JsonPropertyName("pu")] public decimal? UnitPrice { get; set; }
        [JsonPropertyName("devise")] public Currency Currency { get; set; } = Currency.USD;
        [JsonPropertyName("justification")] public string Justification { get; set; }
    }
}
