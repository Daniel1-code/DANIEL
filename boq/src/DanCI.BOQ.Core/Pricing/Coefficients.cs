using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>
    /// Coefficient de majoration de quantite (pertes, chutes, recouvrements, casse,
    /// foisonnement) : quantite brute = quantite nette x (1 + valeur).
    /// Plusieurs coefficients applicables a un meme article se COMBINENT PAR PRODUIT :
    /// (1 + a)(1 + b). Chacun reste affiche dans la ligne et dans les hypotheses.
    /// </summary>
    public sealed class Coefficient
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }

        /// <summary>Majoration relative : 0,05 = +5 %.</summary>
        [JsonPropertyName("valeur")] public decimal Value { get; set; }

        /// <summary>Motifs sur le code article (ex. "03.*", "02.03").</summary>
        [JsonPropertyName("appliqueA")] public PatternList AppliesTo { get; set; }

        [JsonPropertyName("source")] public string Source { get; set; }

        /// <summary>
        /// Vrai tant que la valeur n'a pas ete validee pour le projet par le metreur. Un
        /// coefficient non valide reste applique, mais chaque utilisation est signalee.
        /// </summary>
        [JsonPropertyName("aValider")] public bool NeedsValidation { get; set; }

        public bool AppliesToArticle(string articleCode) =>
            AppliesTo != null && !AppliesTo.IsEmpty && AppliesTo.IsMatch(articleCode);
    }

    /// <summary>Contenu de coefficients.json.</summary>
    public sealed class CoefficientSet
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("coefficients")] public List<Coefficient> Items { get; set; } = new List<Coefficient>();

        public IReadOnlyList<Coefficient> For(string articleCode) =>
            Items.Where(c => c.AppliesToArticle(articleCode)).ToArray();
    }
}
