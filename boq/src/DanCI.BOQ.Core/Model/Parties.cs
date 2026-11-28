using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Configuration;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Coordonnees d'une societe ou d'une personne (le client, ou notre entreprise).</summary>
    public sealed class PartyInfo
    {
        /// <summary>Sigle ou nom court, affiche en grand (ex. « DanCI »).</summary>
        [JsonPropertyName("sigle")] public string ShortName { get; set; }
        /// <summary>Raison sociale ou nom complet.</summary>
        [JsonPropertyName("nom")] public string Name { get; set; }
        /// <summary>Activite / slogan (ex. « Bureau d'études — structures et métré »).</summary>
        [JsonPropertyName("activite")] public string Activity { get; set; }
        [JsonPropertyName("adresse")] public string Address { get; set; }
        [JsonPropertyName("ville")] public string City { get; set; }
        [JsonPropertyName("pays")] public string Country { get; set; }
        [JsonPropertyName("telephone")] public string Phone { get; set; }
        [JsonPropertyName("email")] public string Email { get; set; }
        [JsonPropertyName("siteWeb")] public string Website { get; set; }
        /// <summary>Personne a contacter (client) ou responsable (entreprise).</summary>
        [JsonPropertyName("contact")] public string Contact { get; set; }
        [JsonPropertyName("fonctionContact")] public string ContactRole { get; set; }
        [JsonPropertyName("rccm")] public string Rccm { get; set; }
        [JsonPropertyName("idNat")] public string IdNat { get; set; }
        [JsonPropertyName("nif")] public string Nif { get; set; }
        /// <summary>Compte bancaire (RIB) a imprimer sur le devis, facultatif.</summary>
        [JsonPropertyName("banque")] public string Bank { get; set; }
        /// <summary>Logo PNG ou JPG (absolu, ou relatif au fichier qui contient cette fiche).</summary>
        [JsonPropertyName("logo")] public string Logo { get; set; }

        [JsonIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(Name) && string.IsNullOrWhiteSpace(ShortName);

        /// <summary>Nom a afficher en grand : le sigle, a defaut le nom.</summary>
        [JsonIgnore]
        public string DisplayShort => !string.IsNullOrWhiteSpace(ShortName) ? ShortName.Trim() : (Name ?? string.Empty).Trim();

        /// <summary>Adresse sur une ligne (adresse, ville, pays).</summary>
        [JsonIgnore]
        public string AddressLine => Join(", ", Address, City, Country);

        /// <summary>Joignabilite sur une ligne (tel, email, site).</summary>
        [JsonIgnore]
        public string ContactLine => Join("  ·  ", Phone, Email, Website);

        /// <summary>Mentions legales RDC (RCCM, Id. Nat., NIF) sur une ligne.</summary>
        [JsonIgnore]
        public string LegalLine => Join("  ·  ",
            string.IsNullOrWhiteSpace(Rccm) ? null : "RCCM " + Rccm.Trim(),
            string.IsNullOrWhiteSpace(IdNat) ? null : "Id. Nat. " + IdNat.Trim(),
            string.IsNullOrWhiteSpace(Nif) ? null : "NIF " + Nif.Trim());

        public PartyInfo Copy() => (PartyInfo)MemberwiseClone();

        /// <summary>Copie dont le logo est un chemin absolu (relatif au dossier du fichier de la fiche).</summary>
        public PartyInfo Resolve(string ownerFilePath)
        {
            PartyInfo c = Copy();
            c.Logo = ProjectFile.ResolvePath(Logo, ownerFilePath);
            return c;
        }

        /// <summary>Assemble les morceaux non vides avec un separateur.</summary>
        public static string JoinPublic(string sep, params string[] parts) => Join(sep, parts);

        internal static string Join(string sep, params string[] parts) =>
            string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => p.Trim()));
    }

    /// <summary>
    /// Fiche de NOTRE entreprise (celle qui etablit le devis) : commune a tous les projets,
    /// enregistree dans %AppData%\DanCI\BOQ\entreprise.json (une mise a jour de l'extension n'y
    /// touche pas). Absente : valeurs DanCI par defaut.
    /// </summary>
    public static class CompanyProfileStore
    {
        public const string FileName = "entreprise.json";

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DanCI", "BOQ", FileName);

        public static PartyInfo Default() => new PartyInfo
        {
            ShortName = "DanCI",
            Name = "Dan Consult Intelligente",
            Activity = "Bureau d'études",
            City = "Kinshasa",
            Country = "RDC",
            Contact = "AMISI RIDVAN DANIEL",
            ContactRole = "Ingénieur civil et structures"
        };

        /// <summary>Fiche lue (logo en chemin absolu) ; fichier absent : valeurs par defaut ; illisible : erreur dans issues.</summary>
        public static PartyInfo Load(string path, List<BoqIssue> issues)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return Default();
            }
            PartyInfo p = ConfigurationLoader.ReadText<PartyInfo>(File.ReadAllText(path), FileName, issues);
            return p == null ? Default() : p.Resolve(path);
        }

        public static void Save(string path, PartyInfo company)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = JsonSerializer.Serialize(company, BoqJson.Options);
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

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Identite finale d'un devis : saisie du projet, fiche client, notre entreprise, puis Revit.</summary>
    public static class ProjectIdentity
    {
        public static ProjectInfo Build(ProjectInfo revit, ProjectDetails details, PartyInfo client, PartyInfo company)
        {
            ProjectInfo p = (details ?? new ProjectDetails()).Merge(revit ?? new ProjectInfo());
            p.Company = company ?? CompanyProfileStore.Default();
            if (client != null && !client.IsEmpty)
            {
                p.ClientParty = client;
                // Le nom de la fiche client prime sur le champ « Maitre d'ouvrage » de l'identite.
                p.Client = !string.IsNullOrWhiteSpace(client.Name) ? client.Name.Trim() : p.Client;
            }
            // Bureau d'etudes et redacteur : a defaut, notre entreprise.
            if (string.IsNullOrWhiteSpace(p.Engineer))
            {
                p.Engineer = PartyInfo.Join(" — ", p.Company.DisplayShort, p.Company.Name == p.Company.DisplayShort ? null : p.Company.Name);
            }
            if (string.IsNullOrWhiteSpace(p.Author) && !string.IsNullOrWhiteSpace(p.Company.Contact))
            {
                p.Author = PartyInfo.Join(", ", p.Company.Contact, p.Company.ContactRole);
            }
            return p;
        }
    }
}
