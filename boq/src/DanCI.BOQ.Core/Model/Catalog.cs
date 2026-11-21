using System.Text.Json.Serialization;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Lot du devis (ex. « 03 – Gros œuvre / Superstructure béton armé »).</summary>
    public sealed class Lot
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
    }

    public enum ArticleSplit
    {
        Aucune,
        Type
    }

    /// <summary>Article du BOQ : ce qui apparait sur une ligne du devis.</summary>
    public sealed class Article
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public BoqUnit Unit { get; set; }

        /// <summary>Precisions de la designation (prescriptions, dosage...).</summary>
        [JsonPropertyName("description")] public string Description { get; set; }

        /// <summary>
        /// Ventilation de l'article en sous-lignes. "type" : une sous-ligne par famille et type
        /// Revit (menuiseries : chaque type de porte a sa ligne, ses dimensions et son prix).
        /// </summary>
        [JsonPropertyName("ventilerPar")] public ArticleSplit SplitBy { get; set; } = ArticleSplit.Aucune;
    }
}
