using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Identite du projet saisie dans l'extension (onglet « Paramètres du projet »). Un champ
    /// vide reprend la valeur des Informations sur le projet de Revit : rien n'est invente.
    /// </summary>
    public sealed class ProjectDetails
    {
        [JsonPropertyName("nom")] public string Name { get; set; }
        [JsonPropertyName("numero")] public string Number { get; set; }
        [JsonPropertyName("objet")] public string Purpose { get; set; }
        [JsonPropertyName("maitreOuvrage")] public string Client { get; set; }
        [JsonPropertyName("maitreOeuvre")] public string Architect { get; set; }
        [JsonPropertyName("bureauEtudes")] public string Engineer { get; set; }
        [JsonPropertyName("entreprise")] public string Contractor { get; set; }
        [JsonPropertyName("lieu")] public string Location { get; set; }
        [JsonPropertyName("dateDevis")] public string IssueDate { get; set; }
        [JsonPropertyName("referenceDevis")] public string QuoteReference { get; set; }
        [JsonPropertyName("indice")] public string Revision { get; set; }
        [JsonPropertyName("statut")] public string Status { get; set; }
        [JsonPropertyName("redacteur")] public string Author { get; set; }
        [JsonPropertyName("verificateur")] public string CheckedBy { get; set; }
        [JsonPropertyName("validiteJours")] public string ValidityDays { get; set; }
        [JsonPropertyName("delaiExecution")] public string ExecutionTime { get; set; }
        [JsonPropertyName("conditionsPaiement")] public string PaymentTerms { get; set; }
        [JsonPropertyName("notes")] public string Notes { get; set; }

        /// <summary>Surface de reference en m² (surface de plancher, SHON...) pour le cout au m².</summary>
        [JsonPropertyName("surfaceReferenceM2")] public decimal? ReferenceArea { get; set; }
        [JsonPropertyName("libelleSurface")] public string ReferenceAreaLabel { get; set; }

        /// <summary>Identite finale : la saisie de l'extension, a defaut celle de Revit.</summary>
        public ProjectInfo Merge(ProjectInfo revit)
        {
            revit = revit ?? new ProjectInfo();
            return new ProjectInfo
            {
                Name = Or(Name, revit.Name),
                Number = Or(Number, revit.Number),
                Client = Or(Client, revit.Client),
                Location = Or(Location, revit.Location),
                Author = Or(Author, revit.Author),
                Revision = Or(Revision, revit.Revision),
                IssueDate = Or(IssueDate, revit.IssueDate),
                Status = Or(Status, revit.Status),
                Purpose = Or(Purpose, revit.Purpose),
                Architect = Or(Architect, revit.Architect),
                Engineer = Or(Engineer, revit.Engineer),
                Contractor = Or(Contractor, revit.Contractor),
                QuoteReference = Or(QuoteReference, revit.QuoteReference),
                CheckedBy = Or(CheckedBy, revit.CheckedBy),
                ValidityDays = Or(ValidityDays, revit.ValidityDays),
                ExecutionTime = Or(ExecutionTime, revit.ExecutionTime),
                PaymentTerms = Or(PaymentTerms, revit.PaymentTerms),
                Notes = Or(Notes, revit.Notes),
                ReferenceArea = ReferenceArea ?? revit.ReferenceArea,
                ReferenceAreaLabel = Or(ReferenceAreaLabel, revit.ReferenceAreaLabel),
                Company = revit.Company,
                ClientParty = revit.ClientParty
            };
        }

        private static string Or(string value, string fallback) => string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
    }

    /// <summary>
    /// Parametres financiers propres au projet : devise, TVA, majorations, taux de change.
    /// Absents (null) : ceux de l'installation (config\parametres_devis.json). Presents : ils
    /// remplacent ces cinq reglages ; les decimales et la quantite valorisee restent ceux de
    /// l'installation.
    /// </summary>
    public sealed class ProjectEstimate
    {
        [JsonPropertyName("devise")] public Currency Currency { get; set; } = Currency.USD;
        [JsonPropertyName("tvaPourcentage")] public decimal VatPercent { get; set; } = 16m;
        [JsonPropertyName("majorations")] public List<Markup> Markups { get; set; } = new List<Markup>();
        [JsonPropertyName("deviseSecondaire")] public Currency? SecondaryCurrency { get; set; }
        [JsonPropertyName("tauxDeChange")] public ExchangeRate ExchangeRate { get; set; }
        /// <summary>« niveau » ou « lot » ; null = reglage de l'installation.</summary>
        [JsonPropertyName("presentation")] public string Presentation { get; set; }

        /// <summary>Point de depart de la saisie : les reglages de l'installation.</summary>
        public static ProjectEstimate From(EstimateSettings s)
        {
            s = s ?? new EstimateSettings();
            return new ProjectEstimate
            {
                Currency = s.Currency,
                VatPercent = s.VatPercent,
                Markups = (s.Markups ?? new List<Markup>()).Select(m => new Markup { Code = m.Code, Designation = m.Designation, Percent = m.Percent, Base = m.Base }).ToList(),
                SecondaryCurrency = s.SecondaryCurrency,
                ExchangeRate = s.ExchangeRate == null ? null : new ExchangeRate { CdfPerUsd = s.ExchangeRate.CdfPerUsd, Date = s.ExchangeRate.Date, Source = s.ExchangeRate.Source },
                Presentation = s.Presentation
            };
        }

        /// <summary>Nouveaux reglages : ceux de l'installation, avec les cinq valeurs du projet.</summary>
        public EstimateSettings ApplyTo(EstimateSettings installation)
        {
            installation = installation ?? new EstimateSettings();
            return new EstimateSettings
            {
                SchemaVersion = installation.SchemaVersion,
                Currency = Currency,
                SecondaryCurrency = SecondaryCurrency,
                ExchangeRate = ExchangeRate,
                VatPercent = VatPercent,
                Markups = (Markups ?? new List<Markup>()).ToList(),
                PricedQuantity = installation.PricedQuantity,
                QuantityDecimals = installation.QuantityDecimals,
                QuantityDecimalsByUnit = installation.QuantityDecimalsByUnit,
                AmountDecimals = installation.AmountDecimals,
                Presentation = Presentation ?? installation.Presentation
            };
        }
    }

    /// <summary>Style de la page de garde generee.</summary>
    public enum CoverTheme
    {
        /// <summary>Fond nuit, lignes et chiffres lumineux (cyan / orange DanCI).</summary>
        Futuriste,
        /// <summary>Page blanche, titres bleus : sobre, economise l'encre.</summary>
        Classique
    }

    /// <summary>Page de garde du PDF et du classeur.</summary>
    public sealed class CoverSettings
    {
        [JsonPropertyName("theme")] public CoverTheme Theme { get; set; } = CoverTheme.Futuriste;

        /// <summary>Sous-titre affiche sous le titre (ex. « Dossier d'appel d'offres — Phase APD »).</summary>
        [JsonPropertyName("sousTitre")] public string Subtitle { get; set; }

        /// <summary>Image du projet (rendu, perspective, photo) affichee sur la page generee.</summary>
        [JsonPropertyName("imageProjet")] public string ProjectImage { get; set; }

        /// <summary>
        /// Page de garde realisee a part (PNG ou JPG, format A4 portrait) : elle REMPLACE la page
        /// generee dans le PDF et figure en tete du classeur.
        /// </summary>
        [JsonPropertyName("pageImportee")] public string ImportedPage { get; set; }

        /// <summary>Logo propre au projet ; absent : config\logo.png de l'installation.</summary>
        [JsonPropertyName("logo")] public string Logo { get; set; }

        /// <summary>Copie dont les chemins sont absolus (relatifs au dossier du fichier projet).</summary>
        public CoverSettings Resolve(string projectFilePath) => new CoverSettings
        {
            Theme = Theme,
            Subtitle = Subtitle,
            ProjectImage = ProjectFile.ResolvePath(ProjectImage, projectFilePath),
            ImportedPage = ProjectFile.ResolvePath(ImportedPage, projectFilePath),
            Logo = ProjectFile.ResolvePath(Logo, projectFilePath)
        };

        /// <summary>Images choisies mais introuvables (signalees, jamais remplacees en silence).</summary>
        public IEnumerable<string> MissingFiles()
        {
            foreach (string p in new[] { ProjectImage, ImportedPage, Logo })
            {
                if (!string.IsNullOrWhiteSpace(p) && !File.Exists(p))
                {
                    yield return p;
                }
            }
        }
    }
}
