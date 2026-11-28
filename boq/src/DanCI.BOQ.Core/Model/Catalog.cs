using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Lot du devis (ex. « 03 – Gros œuvre / Superstructure béton armé »).</summary>
    public sealed class Lot
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        /// <summary>Corps d'etat (Gros œuvre, Second œuvre, VRD...) ; absent : deduit du code du lot (<see cref="Trades"/>).</summary>
        [JsonPropertyName("corpsEtat")] public string Trade { get; set; }
    }

    public enum ArticleSplit
    {
        Aucune,
        Type,
        /// <summary>Une sous-ligne par valeur du parametre « parametreVentilation » (ex. DiametreMm).</summary>
        Parametre
    }

    /// <summary>Article du BOQ : ce qui apparait sur une ligne du devis.</summary>
    public sealed class Article
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public BoqUnit Unit { get; set; }

        /// <summary>Precisions de la designation (prescriptions, dosage...).</summary>
        [JsonPropertyName("description")] public string Description { get; set; }

        /// <summary>
        /// Ventilation de l'article en sous-lignes. "type" : une sous-ligne par famille et type
        /// Revit (menuiseries : chaque type de porte a sa ligne, ses dimensions et son prix).
        /// </summary>
        [JsonPropertyName("ventilerPar")] public ArticleSplit SplitBy { get; set; } = ArticleSplit.Aucune;

        /// <summary>Parametre de ventilation (mode Parametre), ex. « DiametreMm ».</summary>
        [JsonPropertyName("parametreVentilation")] public string SplitParameter { get; set; }

        /// <summary>Libelle de la valeur dans la designation, {0} = valeur (ex. « Ø {0} mm »).</summary>
        [JsonPropertyName("libelleVentilation")] public string SplitLabel { get; set; }

        /// <summary>
        /// Quantites derivees, a titre d'information (ex. nombre de blocs a partir des m² de
        /// maconnerie). Calculees sur la quantite valorisee, jamais valorisees elles-memes.
        /// </summary>
        [JsonPropertyName("quantitesDerivees")] public List<DerivedQuantityRule> Derived { get; set; } = new List<DerivedQuantityRule>();
    }
}

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Regle de quantite derivee : valeur = quantite de l'article x parUnite.</summary>
    public sealed class DerivedQuantityRule
    {
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("unite")] public BoqUnit Unit { get; set; } = BoqUnit.Each;
        [JsonPropertyName("parUnite")] public decimal PerUnit { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }
    }

    /// <summary>Quantite derivee calculee pour une ligne.</summary>
    public sealed class DerivedQuantity
    {
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }
        public decimal PerUnit { get; set; }
        public decimal Value { get; set; }
        public string Source { get; set; }
    }
}
