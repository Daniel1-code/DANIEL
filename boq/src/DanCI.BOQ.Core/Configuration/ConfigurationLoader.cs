using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Configuration
{
    /// <summary>Resultat d'un chargement : la configuration et toutes les anomalies (lecture + validation).</summary>
    public sealed class LoadResult
    {
        public BoqConfiguration Configuration { get; set; }
        public List<BoqIssue> Issues { get; } = new List<BoqIssue>();
        public bool HasErrors => Issues.Any(i => i.Severity == IssueSeverity.Error);
    }

    /// <summary>
    /// Lit les fichiers JSON d'un dossier de configuration. Ne leve pas d'exception sur un
    /// fichier invalide : toutes les erreurs sont collectees, avec fichier et ligne, pour que
    /// l'utilisateur les corrige en une fois.
    /// </summary>
    public static class ConfigurationLoader
    {
        public static LoadResult LoadDirectory(string directory)
        {
            var result = new LoadResult();
            var config = new BoqConfiguration
            {
                Mapping = Read<MappingCatalog>(Path.Combine(directory, BoqConfiguration.MappingFileName), true, result.Issues)
            };

            CoefficientSet coefficients = Read<CoefficientSet>(
                Path.Combine(directory, BoqConfiguration.CoefficientsFileName), false, result.Issues);
            if (coefficients != null)
            {
                config.Coefficients = coefficients;
            }

            config.Prices = Read<PriceLibrary>(Path.Combine(directory, BoqConfiguration.PricesFileName), false, result.Issues);

            EstimateSettings settings = Read<EstimateSettings>(
                Path.Combine(directory, BoqConfiguration.SettingsFileName), false, result.Issues);
            if (settings != null)
            {
                config.Settings = settings;
            }

            DanCI.BOQ.Core.Extraction.ExtractionSettings extraction = Read<DanCI.BOQ.Core.Extraction.ExtractionSettings>(
                Path.Combine(directory, BoqConfiguration.ExtractionFileName), false, result.Issues);
            if (extraction != null)
            {
                config.Extraction = extraction;
            }

            DanCI.BOQ.Core.Extraction.SteelProfileTable profiles = Read<DanCI.BOQ.Core.Extraction.SteelProfileTable>(
                Path.Combine(directory, BoqConfiguration.SteelProfilesFileName), false, result.Issues);
            if (profiles != null)
            {
                config.SteelProfiles = profiles;
            }

            result.Configuration = config;
            if (config.Mapping != null)
            {
                result.Issues.AddRange(ConfigurationValidator.Validate(config));
            }
            return result;
        }

        public static T ReadText<T>(string json, string fileName, List<BoqIssue> issues) where T : class
        {
            try
            {
                T value = JsonSerializer.Deserialize<T>(json, BoqJson.Options);
                if (value == null)
                {
                    issues.Add(new BoqIssue(IssueSeverity.Error, "JSON_VIDE", "Fichier vide.") { File = fileName });
                }
                return value;
            }
            catch (JsonException ex)
            {
                string where = ex.LineNumber.HasValue
                    ? " (ligne " + (ex.LineNumber + 1) + ", colonne " + (ex.BytePositionInLine + 1) + ")"
                    : string.Empty;
                // Le message de System.Text.Json numerote les lignes a partir de 0 : on le retire
                // pour ne garder que la position humaine (a partir de 1).
                string message = System.Text.RegularExpressions.Regex.Replace(
                    ex.Message, @"\s*\|?\s*LineNumber: \d+ \| BytePositionInLine: \d+\.?", string.Empty).Trim();
                issues.Add(new BoqIssue(IssueSeverity.Error, "JSON_INVALIDE", Translate(message, ex.Path) + where) { File = fileName });
                return null;
            }
        }

        /// <summary>Rend lisibles par un metreur les deux erreurs les plus frequentes de System.Text.Json.</summary>
        private static string Translate(string message, string path)
        {
            var unknown = System.Text.RegularExpressions.Regex.Match(
                message, @"The JSON property '(?<name>[^']+)' could not be mapped to any \.NET member contained in type '(?<type>[^']+)'");
            if (unknown.Success)
            {
                Type type = typeof(ConfigurationLoader).Assembly.GetType(unknown.Groups["type"].Value);
                string allowed = type == null
                    ? string.Empty
                    : " Propriétés admises : " + string.Join(", ", type.GetProperties()
                        .Select(p => p.GetCustomAttributes(typeof(System.Text.Json.Serialization.JsonPropertyNameAttribute), false)
                            .Cast<System.Text.Json.Serialization.JsonPropertyNameAttribute>().FirstOrDefault()?.Name)
                        .Where(n => n != null)) + ".";
                return "Propriété inconnue « " + unknown.Groups["name"].Value + " » (faute de frappe ?)" +
                       (string.IsNullOrEmpty(path) ? string.Empty : " dans " + path) + "." + allowed;
            }
            if (message.StartsWith("Unité", StringComparison.Ordinal) || message.StartsWith("Un critère", StringComparison.Ordinal))
            {
                return message + (string.IsNullOrEmpty(path) ? string.Empty : " (" + path + ")");
            }
            if (message.StartsWith("The JSON value could not be converted", StringComparison.Ordinal))
            {
                return "Valeur invalide" + (string.IsNullOrEmpty(path) ? string.Empty : " pour " + path) +
                       " (type attendu : texte, nombre, date AAAA-MM-JJ ou valeur d'une liste selon le champ).";
            }
            return "Syntaxe JSON invalide : " + message;
        }

        private static T Read<T>(string path, bool required, List<BoqIssue> issues) where T : class
        {
            string name = Path.GetFileName(path);
            if (!File.Exists(path))
            {
                issues.Add(required
                    ? new BoqIssue(IssueSeverity.Error, "FICHIER_ABSENT", "Fichier obligatoire introuvable : " + path) { File = name }
                    : new BoqIssue(IssueSeverity.Warning, "FICHIER_ABSENT", "Fichier absent, valeurs par défaut utilisées : " + path) { File = name });
                return null;
            }
            try
            {
                return ReadText<T>(File.ReadAllText(path), name, issues);
            }
            catch (IOException ex)
            {
                issues.Add(new BoqIssue(IssueSeverity.Error, "FICHIER_ILLISIBLE", ex.Message) { File = name });
                return null;
            }
        }
    }
}
