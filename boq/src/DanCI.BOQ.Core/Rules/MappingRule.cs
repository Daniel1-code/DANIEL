using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Rules
{
    /// <summary>Critères d'une regle : tous doivent etre satisfaits (ET) ; dans un critere, les motifs sont alternatifs (OU).</summary>
    public sealed class RuleCriteria
    {
        [JsonPropertyName("nature")] public PatternList Kind { get; set; }
        [JsonPropertyName("categorie")] public PatternList Category { get; set; }
        [JsonPropertyName("famille")] public PatternList Family { get; set; }
        [JsonPropertyName("type")] public PatternList TypeName { get; set; }
        [JsonPropertyName("materiau")] public PatternList Material { get; set; }
        [JsonPropertyName("niveau")] public PatternList Level { get; set; }

        /// <summary>Parametre nomme -&gt; motifs. Un parametre absent de l'element vaut chaine vide.</summary>
        [JsonPropertyName("parametres")] public Dictionary<string, PatternList> Parameters { get; set; }

        public bool IsEmpty =>
            IsNullOrEmpty(Kind) && IsNullOrEmpty(Category) && IsNullOrEmpty(Family) &&
            IsNullOrEmpty(TypeName) && IsNullOrEmpty(Material) && IsNullOrEmpty(Level) &&
            (Parameters == null || Parameters.Values.All(IsNullOrEmpty));

        public bool IsMatch(ElementQuantity q)
        {
            if (!Match(Kind, q.Kind) || !Match(Category, q.Category) || !Match(Family, q.Family) ||
                !Match(TypeName, q.TypeName) || !Match(Material, q.Material) || !Match(Level, q.Level))
            {
                return false;
            }
            if (Parameters != null)
            {
                foreach (KeyValuePair<string, PatternList> p in Parameters)
                {
                    string value = null;
                    q.Parameters?.TryGetValue(p.Key, out value);
                    if (!Match(p.Value, value))
                    {
                        return false;
                    }
                }
            }
            return true;
        }

        private static bool Match(PatternList list, string value) => list == null || list.IsMatch(value);

        private static bool IsNullOrEmpty(PatternList list) => list == null || list.IsEmpty;
    }

    /// <summary>
    /// Regle de mapping : critères -&gt; article (ou exclusion). Les regles sont evaluees dans
    /// l'ordre croissant de <see cref="Order"/>, qui doit etre explicite et unique ; la
    /// premiere qui correspond gagne.
    /// </summary>
    public sealed class MappingRule
    {
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("ordre")] public int Order { get; set; }
        [JsonPropertyName("si")] public RuleCriteria When { get; set; }

        /// <summary>Code de l'article cible (absent si la regle exclut).</summary>
        [JsonPropertyName("article")] public string Article { get; set; }

        /// <summary>La regle exclut l'element du metre (ex. elements provisoires). Motif obligatoire.</summary>
        [JsonPropertyName("exclure")] public bool Exclude { get; set; }

        [JsonPropertyName("motif")] public string Reason { get; set; }

        [JsonPropertyName("commentaire")] public string Comment { get; set; }
    }
}
