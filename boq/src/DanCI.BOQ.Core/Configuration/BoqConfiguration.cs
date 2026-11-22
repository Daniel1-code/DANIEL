using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Configuration
{
    /// <summary>Les quatre fichiers qui pilotent le moteur.</summary>
    public sealed class BoqConfiguration
    {
        public const int SupportedSchemaVersion = 1;

        public const string MappingFileName = "regles_mapping.json";
        public const string CoefficientsFileName = "coefficients.json";
        public const string PricesFileName = "prix_unitaires.json";
        public const string SettingsFileName = "parametres_devis.json";
        public const string ExtractionFileName = "extraction.json";
        public const string SteelProfilesFileName = "profils_acier.json";
        public const string MaterialsFileName = "materiaux.json";

        public MappingCatalog Mapping { get; set; }
        public CoefficientSet Coefficients { get; set; } = new CoefficientSet { SchemaVersion = SupportedSchemaVersion };

        /// <summary>Peut etre absente : le BOQ est alors un metre sans montants.</summary>
        public PriceLibrary Prices { get; set; }

        public EstimateSettings Settings { get; set; } = new EstimateSettings { SchemaVersion = SupportedSchemaVersion };

        /// <summary>Conventions d'extraction (deduction des ouvertures, reconnaissance du beton...).</summary>
        public DanCI.BOQ.Core.Extraction.ExtractionSettings Extraction { get; set; } = new DanCI.BOQ.Core.Extraction.ExtractionSettings();

        /// <summary>Masses lineiques des profiles (table de repli quand le type Revit ne donne pas son poids).</summary>
        public DanCI.BOQ.Core.Extraction.SteelProfileTable SteelProfiles { get; set; } = new DanCI.BOQ.Core.Extraction.SteelProfileTable();

        /// <summary>Hypotheses de la liste des materiaux (dosages...) ; null = fichier absent, liste non produite.</summary>
        public DanCI.BOQ.Core.Materials.MaterialSettings Materials { get; set; }
    }

    /// <summary>Configuration invalide : le moteur refuse de produire un BOQ.</summary>
    public sealed class BoqConfigurationException : Exception
    {
        public BoqConfigurationException(IReadOnlyList<BoqIssue> issues)
            : base("Configuration du BOQ invalide :" + Environment.NewLine +
                   string.Join(Environment.NewLine, issues.Where(i => i.Severity == IssueSeverity.Error).Select(i => "  - " + i)))
        {
            Issues = issues;
        }

        public IReadOnlyList<BoqIssue> Issues { get; }
    }
}
