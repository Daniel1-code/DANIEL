using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Rules
{
    /// <summary>
    /// Contenu de regles_mapping.json : structure du devis (lots, articles, dans l'ordre
    /// d'affichage) et regles de rattachement des quantites aux articles.
    /// </summary>
    public sealed class MappingCatalog
    {
        /// <summary>Lot reserve aux quantites qu'aucune regle ne rattache.</summary>
        public const string UnclassifiedLotCode = "NC";

        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }
        [JsonPropertyName("nom")] public string Name { get; set; }
        [JsonPropertyName("lots")] public List<Lot> Lots { get; set; } = new List<Lot>();
        [JsonPropertyName("articles")] public List<Article> Articles { get; set; } = new List<Article>();
        [JsonPropertyName("regles")] public List<MappingRule> Rules { get; set; } = new List<MappingRule>();

        private IReadOnlyList<MappingRule> orderedRules;
        private Dictionary<string, Article> articleIndex;

        /// <summary>Premiere regle (par ordre croissant) dont les criteres correspondent ; null sinon.</summary>
        public MappingRule Match(ElementQuantity quantity)
        {
            if (orderedRules == null)
            {
                orderedRules = Rules.OrderBy(r => r.Order).ToArray();
            }
            return orderedRules.FirstOrDefault(r => r.When != null && r.When.IsMatch(quantity));
        }

        public Article FindArticle(string code)
        {
            if (articleIndex == null)
            {
                articleIndex = new Dictionary<string, Article>();
                foreach (Article a in Articles.Where(a => a.Code != null))
                {
                    articleIndex[a.Code] = a; // doublons signales par la validation
                }
            }
            return code != null && articleIndex.TryGetValue(code, out Article article) ? article : null;
        }
    }
}
