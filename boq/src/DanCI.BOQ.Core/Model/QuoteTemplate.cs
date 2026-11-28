using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Conditions de l'offre reprises par un modele.</summary>
    public sealed class QuoteConditions
    {
        [JsonPropertyName("statut")] public string Status { get; set; }
        [JsonPropertyName("validiteJours")] public string ValidityDays { get; set; }
        [JsonPropertyName("delaiExecution")] public string ExecutionTime { get; set; }
        [JsonPropertyName("conditionsPaiement")] public string PaymentTerms { get; set; }
        [JsonPropertyName("notes")] public string Notes { get; set; }
    }

    /// <summary>
    /// Modele de devis reutilisable (par client, par type de projet) : TVA et majorations, page
    /// de garde, conditions de l'offre, fiche client et prix generaux. Chaque partie est
    /// facultative : absente, elle ne change rien quand on applique le modele.
    /// </summary>
    public sealed class QuoteTemplate
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("nom")] public string Name { get; set; }
        [JsonPropertyName("description")] public string Description { get; set; }
        [JsonPropertyName("creeLe")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("devis")] public ProjectEstimate Estimate { get; set; }
        [JsonPropertyName("pageDeGarde")] public CoverSettings Cover { get; set; }
        [JsonPropertyName("conditions")] public QuoteConditions Conditions { get; set; }
        [JsonPropertyName("client")] public PartyInfo Client { get; set; }
        /// <summary>Prix generaux des articles (code, PU, devise, date, source, decomposition).</summary>
        [JsonPropertyName("prix")] public List<PriceEntry> Prices { get; set; }

        /// <summary>Resume des parties incluses, pour la liste des modeles.</summary>
        [JsonIgnore]
        public string Contents => string.Join(", ", new[]
        {
            Estimate != null ? "TVA et majorations" : null,
            Prices != null && Prices.Count > 0 ? Prices.Count + " prix" : null,
            Cover != null ? "page de garde" : null,
            Conditions != null ? "conditions" : null,
            Client != null && !Client.IsEmpty ? "client" : null
        }.Where(x => x != null));
    }

    /// <summary>Modeles dans %AppData%\DanCI\BOQ\modeles (un fichier JSON par modele).</summary>
    public static class QuoteTemplateStore
    {
        public static string DefaultFolder =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DanCI", "BOQ", "modeles");

        public static string PathFor(string folder, string name)
        {
            string safe = string.IsNullOrWhiteSpace(name) ? "modele" : name.Trim();
            foreach (char c in Path.GetInvalidFileNameChars()) safe = safe.Replace(c, '_');
            return Path.Combine(folder, safe + ".json");
        }

        /// <summary>Modeles lisibles, tries par nom ; un fichier illisible est signale, jamais efface.</summary>
        public static List<QuoteTemplate> List(string folder, List<BoqIssue> issues)
        {
            var list = new List<QuoteTemplate>();
            if (!Directory.Exists(folder)) return list;
            foreach (string f in Directory.GetFiles(folder, "*.json").OrderBy(f => f, StringComparer.CurrentCultureIgnoreCase))
            {
                QuoteTemplate t = ConfigurationLoader.ReadText<QuoteTemplate>(File.ReadAllText(f), Path.GetFileName(f), issues);
                if (t != null)
                {
                    if (string.IsNullOrWhiteSpace(t.Name)) t.Name = Path.GetFileNameWithoutExtension(f);
                    list.Add(t);
                }
            }
            return list;
        }

        public static string Save(string folder, QuoteTemplate template)
        {
            if (string.IsNullOrWhiteSpace(template?.Name)) throw new ArgumentException("Le modèle doit avoir un nom.");
            Directory.CreateDirectory(folder);
            template.CreatedAt = DateTime.Now;
            string path = PathFor(folder, template.Name);
            File.WriteAllText(path, JsonSerializer.Serialize(template, BoqJson.Options), new UTF8Encoding(false));
            return path;
        }

        public static void Delete(string folder, string name)
        {
            string path = PathFor(folder, name);
            if (File.Exists(path)) File.Delete(path);
        }
    }
}
