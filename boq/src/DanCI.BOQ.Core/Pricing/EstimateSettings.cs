using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>
    /// Taux de change USD/CDF saisi a la main et date. Jamais recupere automatiquement : le
    /// taux d'un devis est celui qu'on peut justifier a la date du devis.
    /// </summary>
    public sealed class ExchangeRate
    {
        /// <summary>Nombre de CDF pour 1 USD.</summary>
        [JsonPropertyName("cdfParUsd")] public decimal CdfPerUsd { get; set; }
        [JsonPropertyName("date")] public DateTime? Date { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; }

        public decimal Convert(decimal amount, Currency from, Currency to)
        {
            if (from == to)
            {
                return amount;
            }
            if (CdfPerUsd <= 0)
            {
                throw new InvalidOperationException("Taux de change USD/CDF non renseigné.");
            }
            return from == Currency.USD ? amount * CdfPerUsd : amount / CdfPerUsd;
        }
    }

    /// <summary>Base de calcul d'une majoration de pied de devis.</summary>
    public enum MarkupBase
    {
        /// <summary>Sur le total des travaux (somme des lots), sans cascade.</summary>
        Travaux,
        /// <summary>Sur le cumul courant : travaux + majorations precedentes (cascade).</summary>
        Cumul
    }

    /// <summary>Frais generaux, imprevus, benefice... : pourcentage de pied de devis.</summary>
    public sealed class Markup
    {
        [JsonPropertyName("code")] public string Code { get; set; }
        [JsonPropertyName("designation")] public string Designation { get; set; }
        [JsonPropertyName("pourcentage")] public decimal Percent { get; set; }
        [JsonPropertyName("base")] public MarkupBase Base { get; set; } = MarkupBase.Travaux;
    }

    /// <summary>Quantite multipliee par le prix unitaire.</summary>
    public enum PricedQuantity
    {
        /// <summary>Quantite brute (nette x coefficients). Les PU ne doivent PAS deja inclure les pertes.</summary>
        Brute,
        /// <summary>Quantite nette en place. Les pertes sont alors reputees incluses dans les PU.</summary>
        Nette
    }

    /// <summary>Contenu de parametres_devis.json.</summary>
    public sealed class EstimateSettings
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; }

        /// <summary>Devise dans laquelle le devis est etabli.</summary>
        [JsonPropertyName("devise")] public Currency Currency { get; set; } = Currency.USD;

        /// <summary>Devise secondaire affichee pour le total TTC (conversion au taux ci-dessous).</summary>
        [JsonPropertyName("deviseSecondaire")] public Currency? SecondaryCurrency { get; set; }

        [JsonPropertyName("tauxDeChange")] public ExchangeRate ExchangeRate { get; set; }

        /// <summary>TVA en pourcentage (RDC : 16).</summary>
        [JsonPropertyName("tvaPourcentage")] public decimal VatPercent { get; set; } = 16m;

        /// <summary>Majorations de pied de devis, appliquees dans cet ordre.</summary>
        [JsonPropertyName("majorations")] public List<Markup> Markups { get; set; } = new List<Markup>();

        [JsonPropertyName("quantiteValorisee")] public PricedQuantity PricedQuantity { get; set; } = PricedQuantity.Nette;

        /// <summary>Decimales des quantites affichees et valorisees (par defaut).</summary>
        [JsonPropertyName("decimalesQuantite")] public int QuantityDecimals { get; set; } = 2;

        /// <summary>Exceptions par unite (ex. kg : 0, U : 0).</summary>
        [JsonPropertyName("decimalesQuantiteParUnite")]
        public Dictionary<BoqUnit, int> QuantityDecimalsByUnit { get; set; } = new Dictionary<BoqUnit, int>();

        /// <summary>Decimales des prix unitaires et des montants.</summary>
        [JsonPropertyName("decimalesMontant")] public int AmountDecimals { get; set; } = 2;

        public int DecimalsFor(BoqUnit unit) =>
            QuantityDecimalsByUnit != null && QuantityDecimalsByUnit.TryGetValue(unit, out int d) ? d : QuantityDecimals;
    }
}
