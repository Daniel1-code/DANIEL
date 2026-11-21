using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>Contribution d'un element a une ligne, avec la regle qui l'y a rattache.</summary>
    public sealed class LineContribution
    {
        public LineContribution(ElementQuantity element, string ruleId)
        {
            Element = element;
            RuleId = ruleId;
        }

        public ElementQuantity Element { get; }

        /// <summary>Regle de mapping appliquee (null pour les non classes).</summary>
        public string RuleId { get; }
    }

    /// <summary>Coefficient effectivement applique a une ligne.</summary>
    public sealed class AppliedCoefficient
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal Value { get; set; }
        public bool NeedsValidation { get; set; }
    }

    /// <summary>
    /// Ligne du BOQ (LigneBOQ). Toutes les quantites sont des decimal : la somme d'elements
    /// convertis en decimal ne depend pas de l'ordre d'addition, ce qui rend le BOQ
    /// reproductible a l'identique.
    /// </summary>
    public sealed class BoqLine
    {
        public string Code { get; set; }

        /// <summary>Article d'origine (egal a Code, sauf pour une sous-ligne ventilee par type).</summary>
        public string ArticleCode { get; set; }

        public string LotCode { get; set; }
        public string Designation { get; set; }
        public string Description { get; set; }
        public BoqUnit Unit { get; set; }

        /// <summary>Somme exacte des contributions (non arrondie).</summary>
        public decimal NetQuantity { get; set; }

        /// <summary>Part de la quantite nette issue de contributions mesurees.</summary>
        public decimal MeasuredNetQuantity { get; set; }

        /// <summary>Part de la quantite nette estimee (ratio, hypothese).</summary>
        public decimal EstimatedNetQuantity { get; set; }

        public IReadOnlyList<AppliedCoefficient> Coefficients { get; set; } = new AppliedCoefficient[0];

        /// <summary>Produit des (1 + coefficient).</summary>
        public decimal CoefficientFactor { get; set; } = 1m;

        /// <summary>Quantite nette x facteur (non arrondie).</summary>
        public decimal GrossQuantity { get; set; }

        /// <summary>
        /// Quantite affichee dans la colonne « Quantité » et multipliee par le PU : nette ou
        /// brute selon les parametres, ARRONDIE. Le montant est calcule sur cette valeur
        /// arrondie, pour que la formule Excel (Quantité x PU) retombe exactement sur le montant.
        /// </summary>
        public decimal Quantity { get; set; }

        /// <summary>PU dans la devise du devis, arrondi ; null si aucun prix applicable.</summary>
        public decimal? UnitPrice { get; set; }

        /// <summary>Prix d'origine si converti depuis une autre devise.</summary>
        public decimal? OriginalUnitPrice { get; set; }
        public Currency? OriginalCurrency { get; set; }
        public string PriceSource { get; set; }

        /// <summary>Quantite x PU, arrondi ; null si pas de PU.</summary>
        public decimal? Amount { get; set; }

        public Reliability Reliability { get; set; }

        public bool IsManual { get; set; }
        public bool IsUnclassified { get; set; }

        public List<LineContribution> Contributions { get; } = new List<LineContribution>();

        /// <summary>Quantites derivees (information : nombre de blocs...).</summary>
        public List<DerivedQuantity> Derived { get; } = new List<DerivedQuantity>();

        /// <summary>Designation suivie des quantites derivees : « … — soit ≈ 1 250 U (blocs, 12,5 U/m²) ».</summary>
        public string DesignationWithDerived
        {
            get
            {
                if (Derived.Count == 0) return Designation;
                var fr = System.Globalization.CultureInfo.GetCultureInfo("fr-FR");
                return Designation + " — soit ≈ " + string.Join(" ; ", Derived.Select(d =>
                    d.Value.ToString("N0", fr) + " " + d.Unit.Symbol() + " (" + d.Designation + ", " +
                    d.PerUnit.ToString("0.###", fr) + " " + d.Unit.Symbol() + "/" + Unit.Symbol() + ")"));
            }
        }

        /// <summary>Hypotheses distinctes des contributions estimees.</summary>
        public IEnumerable<string> Assumptions =>
            Contributions.Select(c => c.Element.Assumption).Where(a => !string.IsNullOrWhiteSpace(a)).Distinct();

        public IEnumerable<long> ElementIds => Contributions.Select(c => c.Element.ElementId);

        /// <summary>References pour la selection dans Revit (elements des liens compris).</summary>
        public IReadOnlyCollection<ElementRef> ElementRefs => ElementRef.Of(Contributions.Select(c => c.Element));
    }

    public sealed class BoqLotResult
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public List<BoqLine> Lines { get; } = new List<BoqLine>();

        /// <summary>Somme des montants connus.</summary>
        public decimal Total { get; set; }

        /// <summary>Faux si une ligne au moins n'a pas de montant : le total est un minorant.</summary>
        public bool IsComplete { get; set; }
    }

    public sealed class MarkupLine
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal Percent { get; set; }
        public MarkupBase Base { get; set; }
        public decimal BaseAmount { get; set; }
        public decimal Amount { get; set; }
    }

    /// <summary>Element ecarte du metre, conserve pour l'audit.</summary>
    public sealed class ExcludedQuantity
    {
        public ExcludedQuantity(ElementQuantity element, string reason, string ruleId)
        {
            Element = element;
            Reason = reason;
            RuleId = ruleId;
        }

        public ElementQuantity Element { get; }
        public string Reason { get; }
        public string RuleId { get; }
    }

    /// <summary>BOQ complet : lots, pied de devis, anomalies, indice de fiabilite.</summary>
    public sealed class BoqResult
    {
        public Currency Currency { get; set; }
        public PricedQuantity PricedQuantity { get; set; }

        public List<BoqLotResult> Lots { get; } = new List<BoqLotResult>();

        /// <summary>Total des travaux HT (somme des lots, montants connus).</summary>
        public decimal WorksTotal { get; set; }

        public List<MarkupLine> Markups { get; } = new List<MarkupLine>();

        public decimal TotalExcludingVat { get; set; }
        public decimal VatPercent { get; set; }
        public decimal Vat { get; set; }
        public decimal TotalIncludingVat { get; set; }

        public Currency? SecondaryCurrency { get; set; }
        public decimal? TotalIncludingVatSecondary { get; set; }

        /// <summary>Faux si une ligne au moins n'a pas de montant : les totaux sont des minorants.</summary>
        public bool IsComplete { get; set; }

        /// <summary>
        /// Indice de fiabilite = montant issu de quantites mesurees / montant des lignes metrees
        /// (mesurees + estimees). Les forfaits saisis en sont exclus (ni mesures ni estimes).
        /// Null si aucun montant metre.
        /// </summary>
        public decimal? ReliabilityIndex { get; set; }

        public decimal MeasuredAmount { get; set; }
        public decimal EstimatedAmount { get; set; }
        public decimal ManualAmount { get; set; }

        public List<ExcludedQuantity> Excluded { get; } = new List<ExcludedQuantity>();

        public List<BoqIssue> Issues { get; } = new List<BoqIssue>();

        public IEnumerable<BoqLine> AllLines => Lots.SelectMany(l => l.Lines);
    }
}
