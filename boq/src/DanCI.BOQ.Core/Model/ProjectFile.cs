using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Configuration;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Fichier projet « &lt;projet&gt;.danciboq.json », a cote du .rvt : ce qui appartient au projet
    /// et doit survivre a une reanalyse — forfaits saisis, phase, liens retenus.
    /// </summary>
    public sealed class ProjectFile
    {
        public const string Extension = ".danciboq.json";

        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;

        /// <summary>Forfaits saisis (installation de chantier, etudes, essais...).</summary>
        [JsonPropertyName("forfaits")] public List<ManualLine> ManualLines { get; set; } = new List<ManualLine>();

        [JsonPropertyName("phase")] public string Phase { get; set; }
        [JsonPropertyName("inclureDemolition")] public bool IncludeDemolition { get; set; }

        /// <summary>Liens RVT inclus au metre (noms des types de lien).</summary>
        [JsonPropertyName("liens")] public List<string> Links { get; set; } = new List<string>();

        [JsonPropertyName("modifieLe")] public DateTime? SavedAt { get; set; }
    }

    /// <summary>Lecture et ecriture du fichier projet. Ecriture atomique : jamais de fichier a moitie ecrit.</summary>
    public static class ProjectFileStore
    {
        /// <summary>Chemin du fichier projet pour un .rvt (meme dossier, meme nom).</summary>
        public static string PathFor(string rvtPath, string fallbackFolder, string title)
        {
            if (!string.IsNullOrEmpty(rvtPath) && File.Exists(rvtPath))
            {
                return Path.Combine(Path.GetDirectoryName(rvtPath), Path.GetFileNameWithoutExtension(rvtPath) + ProjectFile.Extension);
            }
            string name = string.IsNullOrWhiteSpace(title) ? "projet" : title;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            if (name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - 4);
            }
            return Path.Combine(fallbackFolder, name + ProjectFile.Extension);
        }

        /// <summary>Fichier absent : projet vide. Fichier invalide : erreur explicite (jamais ecrase en silence).</summary>
        public static ProjectFile Load(string path, List<BoqIssue> issues)
        {
            if (!File.Exists(path))
            {
                return new ProjectFile();
            }
            ProjectFile file = ConfigurationLoader.ReadText<ProjectFile>(File.ReadAllText(path), Path.GetFileName(path), issues);
            return file ?? new ProjectFile();
        }

        public static void Save(string path, ProjectFile file)
        {
            file.SavedAt = DateTime.Now;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonSerializer.Serialize(file, BoqJson.Options);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json, new UTF8Encoding(false));
            if (File.Exists(path))
            {
                File.Replace(temp, path, path + ".bak");
            }
            else
            {
                File.Move(temp, path);
            }
        }
    }
}
