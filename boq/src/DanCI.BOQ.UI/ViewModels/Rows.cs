using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Ligne du BOQ affichee (lecture seule).</summary>
    public sealed class LineRow
    {
        public LineRow(BoqLotResult lot, BoqLine line, string currency)
        {
            Line = line;
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            LotHeader = "LOT " + lot.Code + " – " + lot.Designation + "   |   " + lot.Total.ToString("N2", fr) + " " + currency +
                        (lot.IsComplete ? string.Empty : "   (incomplet : lignes sans prix)");
            Contributions = line.Contributions.Select(c => new ContributionRow(c)).ToList();
        }

        public BoqLine Line { get; }
        public string LotHeader { get; }
        public string Code => Line.Code;
        public string Designation => Line.Designation;
        public string Unit => Line.Unit.Symbol();
        public decimal NetQuantity => Line.NetQuantity;
        public decimal CoefficientFactor => Line.CoefficientFactor;
        public string Coefficients => Line.Coefficients.Count == 0 ? string.Empty : string.Join(" · ", Line.Coefficients.Select(c => c.Code));
        public decimal Quantity => Line.Quantity;
        public decimal? UnitPrice => Line.UnitPrice;
        public decimal? Amount => Line.Amount;
        public string Status => Line.Reliability.Label();
        public int ElementCount => Line.Contributions.Count;
        public string Assumptions => string.Join(" / ", Line.Assumptions);
        public bool IsUnclassified => Line.IsUnclassified;
        public bool HasNoPrice => !Line.Amount.HasValue;
        public IReadOnlyList<ContributionRow> Contributions { get; }
        public IReadOnlyCollection<long> ElementIds => Line.ElementIds.Distinct().ToList();
    }

    /// <summary>Contribution d'un element a la ligne selectionnee (detail du metre).</summary>
    public sealed class ContributionRow
    {
        public ContributionRow(LineContribution c)
        {
            Q = c.Element;
            RuleId = c.RuleId;
        }

        public ElementQuantity Q { get; }
        public string RuleId { get; }
        public long ElementId => Q.ElementId;
        public string Category => Q.CategoryLabel ?? Q.Category;
        public string TypeName => Q.TypeName;
        public string Material => Q.Material;
        public string Level => Q.Level;
        public double Value => Q.Value;
        public string Unit => Q.Unit.Symbol();
        public string Method => Q.MethodDescription;
        public string Status => Q.Reliability.Label();
        public string Assumption => Q.Assumption;
    }

    /// <summary>Constat d'audit affiche, avec ses elements a selectionner.</summary>
    public sealed class FindingRow
    {
        public FindingRow(AuditFinding f)
        {
            Finding = f;
        }

        public AuditFinding Finding { get; }
        public string SectionTitle => ((int)Finding.Section) + ". " + AuditReport.Title(Finding.Section);
        public string Severity => Finding.Severity == IssueSeverity.Error ? "❌ Erreur" : Finding.Severity == IssueSeverity.Warning ? "⚠️ Avertissement" : "ℹ️ Info";
        public string Code => Finding.Code;
        public string Message => Finding.Message;
        public int ElementCount => Finding.ElementIds.Count;
        public bool CanSelect => Finding.ElementIds.Count > 0;
        public IReadOnlyCollection<long> ElementIds => Finding.ElementIds.Distinct().ToList();
    }

    public sealed class ExcludedRow
    {
        public ExcludedRow(ExcludedQuantity x)
        {
            X = x;
        }

        public ExcludedQuantity X { get; }
        public long ElementId => X.Element.ElementId;
        public string Category => X.Element.CategoryLabel ?? X.Element.Category;
        public string TypeName => X.Element.TypeName;
        public string Kind => X.Element.Kind;
        public string Value => X.Element.Value.ToString("0.###", CultureInfo.GetCultureInfo("fr-FR")) + " " + X.Element.Unit.Symbol();
        public string Reason => X.Reason + (X.RuleId != null ? " (règle " + X.RuleId + ")" : string.Empty);
    }

    public sealed class ScopeOption
    {
        public DanCI.BOQ.Core.Extraction.ScopeMode Mode { get; set; }
        public string Label { get; set; }
    }

    public sealed class LevelItem : Mvvm.ObservableObject
    {
        private bool isChecked;

        public string Name { get; set; }

        public bool IsChecked
        {
            get => isChecked;
            set => Set(ref isChecked, value);
        }
    }
}
