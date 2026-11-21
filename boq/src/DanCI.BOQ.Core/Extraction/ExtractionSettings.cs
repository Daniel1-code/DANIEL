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
    }
}
