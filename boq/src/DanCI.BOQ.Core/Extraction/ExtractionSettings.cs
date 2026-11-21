using System.Collections.Generic;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Regle de deduction des ouvertures dans les surfaces de maconnerie.</summary>
    public enum OpeningDeductionMode
    {
        /// <summary>Toutes les ouvertures sont deduites.</summary>
        Toutes,
        /// <summary>Seules les ouvertures de surface STRICTEMENT superieure au seuil sont deduites.</summary>
        AuDelaDuSeuil,
        /// <summary>Aucune ouverture n'est deduite (surface brute).</summary>
        Aucune
    }

    /// <summary>Face du mur sur laquelle la surface de maconnerie est mesuree.</summary>
    public enum MasonryMeasureFace
    {
        /// <summary>Moyenne des faces exterieure et interieure : equivaut a la surface a l'axe.</summary>
        Moyenne,
        Exterieure,
        Interieure
    }

    /// <summary>
    /// Contenu de extraction.json : comment l'adaptateur Revit transforme la maquette en
    /// quantites. Ce sont des conventions de metre, donc des choix explicites et modifiables.
    /// </summary>
    public sealed class ExtractionSettings
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;

        /// <summary>
        /// Motifs de nom de materiau reconnus comme beton QUAND le materiau n'a pas d'actif
        /// structurel de classe Beton ni de classe de materiau « Béton/Concrete ».
        /// </summary>
        [JsonPropertyName("materiauxBeton")]
        public PatternList ConcreteMaterialNames { get; set; } =
            new PatternList(new[] { "*béton*", "*beton*", "*concrete*", "*C??/??*" });

        [JsonPropertyName("deductionOuvertures")]
        public OpeningDeductionMode OpeningDeduction { get; set; } = OpeningDeductionMode.AuDelaDuSeuil;

        /// <summary>Seuil en m² (mode AuDelaDuSeuil).</summary>
        [JsonPropertyName("seuilDeductionM2")]
        public decimal OpeningDeductionThresholdM2 { get; set; } = 0.5m;

        [JsonPropertyName("faceMesureMaconnerie")]
        public MasonryMeasureFace MasonryFace { get; set; } = MasonryMeasureFace.Moyenne;

        /// <summary>Valeurs du parametre DanCI_BOQ_Exclure qui excluent l'element.</summary>
        [JsonPropertyName("valeursExclusion")]
        public List<string> ExcludeValues { get; set; } = new List<string> { "Oui", "Yes", "1", "Vrai", "True" };

        [JsonPropertyName("coffrage")] public FormworkSettings Formwork { get; set; } = new FormworkSettings();

        /// <summary>
        /// Ratios d'armatures (kg/m³ de beton) appliques aux elements en beton SANS armature
        /// modelisee. Une categorie sans ratio produit une quantite MANQUANTE, jamais une
        /// quantite inventee.
        /// </summary>
        [JsonPropertyName("ratiosArmatures")] public List<RebarRatio> RebarRatios { get; set; } = new List<RebarRatio>();

        [JsonPropertyName("acier")] public SteelSettings Steel { get; set; } = new SteelSettings();
    }

    /// <summary>Conventions de metre du coffrage.</summary>
    public sealed class FormworkSettings
    {
        /// <summary>
        /// Une face dont la pente sur l'horizontale est INFERIEURE a cet angle et qui regarde
        /// vers le haut n'est pas coffree (dessus de dalle, de semelle, de poutre).
        /// </summary>
        [JsonPropertyName("penteMaxDessusNonCoffreDeg")] public double MaxUncoveredTopSlopeDeg { get; set; } = 30.0;

        /// <summary>Sous-faces des fondations (sur sol ou beton de proprete) : non coffrees.</summary>
        [JsonPropertyName("exclureSousFacesFondations")] public bool ExcludeFoundationBottoms { get; set; } = true;

        /// <summary>Fondations coulees pleine fouille : leurs faces laterales ne sont pas coffrees non plus.</summary>
        [JsonPropertyName("fondationsPleineFouille")] public bool FoundationsCastAgainstSoil { get; set; }

        /// <summary>Types de sols poses sur terre-plein (motifs) : sous-face non coffree.</summary>
        [JsonPropertyName("typesSurTerrePlein")]
        public PatternList SlabOnGradeTypes { get; set; } = new PatternList(new[] { "*terre*plein*", "*dallage*" });

        /// <summary>Deduire les surfaces en contact avec un autre element (poutre contre poteau, voile sous dalle...).</summary>
        [JsonPropertyName("deduireContacts")] public bool DeductContacts { get; set; } = true;
    }

    /// <summary>Ratio d'armatures pour une categorie Revit (motif sur le nom BuiltInCategory).</summary>
    public sealed class RebarRatio
    {
        [JsonPropertyName("categorie")] public PatternList Category { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("kgParM3")] public decimal KgPerM3 { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
        [JsonPropertyName("aValider")] public bool NeedsValidation { get; set; } = true;
    }

    /// <summary>Conventions de metre de la charpente metallique.</summary>
    public sealed class SteelSettings
    {
        [JsonPropertyName("masseVolumiqueKgM3")] public decimal DensityKgPerM3 { get; set; } = 7850m;

        /// <summary>Assemblages et boulonnerie, en part de la masse des profiles (0,10 = 10 %). 0 : pas de ligne.</summary>
        [JsonPropertyName("assemblagesPart")] public decimal ConnectionsShare { get; set; }

        [JsonPropertyName("assemblagesSource")] public string ConnectionsSource { get; set; }
    }
}
