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
        public string Designation => Line.DesignationWithDerived;
        public string Unit => Line.Unit.Symbol();
        public decimal NetQuantity => Line.NetQuantity;
        public decimal CoefficientFactor => Line.CoefficientFactor;
        public string Coefficients => Line.Coefficients.Count == 0 ? string.Empty : string.Join(" · ", Line.Coefficients.Select(c => c.Code));
        public decimal Quantity => Line.Quantity;
        public decimal? UnitPrice => Line.UnitPrice;
        public decimal? Amount => Line.Amount;
        public string Status => Line.Reliability.Label();
        public string StatusColor => DanCI.BOQ.Core.Dashboard.Palette.ForReliability(Line.Reliability);
        public int ElementCount => Line.Contributions.Count;
        public string Assumptions => string.Join(" / ", Line.Assumptions);
        public bool IsUnclassified => Line.IsUnclassified;
        public bool HasNoPrice => !Line.Amount.HasValue;
        public IReadOnlyList<ContributionRow> Contributions { get; }
        public IReadOnlyCollection<ElementRef> ElementRefs => Line.ElementRefs;
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
        public ElementRef Ref => ElementRef.Of(Q);
        public string Source => string.IsNullOrEmpty(Q.SourceDocument) ? string.Empty : Q.SourceDocument;
        public string Category => Q.CategoryLabel ?? Q.Category;
        public string TypeName => Q.TypeName;
        public string Material => Q.Material;
        public string Level => Q.Level;
        public double Value => Q.Value;
        public string Unit => Q.Unit.Symbol();
        public string Method => Q.MethodDescription;
        public string Status => Q.Reliability.Label();
        public string StatusColor => DanCI.BOQ.Core.Dashboard.Palette.ForReliability(Q.Reliability);
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
        public string SeverityColor => DanCI.BOQ.Core.Dashboard.Palette.ForSeverity(Finding.Severity);
        public string Code => Finding.Code;
        public string Message => Finding.Message;
        public int ElementCount => Finding.ElementIds.Count;
        public bool CanSelect => Finding.ElementIds.Count > 0;
        public IReadOnlyCollection<ElementRef> ElementRefs => Finding.ElementRefs;
    }

    public sealed class ExcludedRow
    {
        public ExcludedRow(ExcludedQuantity x)
        {
            X = x;
        }

        public ExcludedQuantity X { get; }
        public long ElementId => X.Element.ElementId;
        public ElementRef Ref => ElementRef.Of(X.Element);
        public string Category => X.Element.CategoryLabel ?? X.Element.Category;
        public string TypeName => X.Element.TypeName;
        public string Kind => X.Element.Kind;
        public string Value => X.Element.Value.ToString("0.###", CultureInfo.GetCultureInfo("fr-FR")) + " " + X.Element.Unit.Symbol();
        public string Reason => X.Reason + (X.RuleId != null ? " (règle " + X.RuleId + ")" : string.Empty);
    }

    /// <summary>Recapitulatif d'un materiau dans une fonction de couche (onglet Couches).</summary>
    public sealed class LayerGroupRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public LayerGroupRow(DanCI.BOQ.Core.Extraction.LayerSummaryRow row)
        {
            Row = row;
        }

        public DanCI.BOQ.Core.Extraction.LayerSummaryRow Row { get; }
        public string Function => Row.Function;
        public string Material => Row.Material;
        public string Categories => Row.Categories;
        public string Thicknesses => Row.Thicknesses;
        public double Area => Row.AreaM2;
        public double Volume => Row.VolumeM3;
        public int Elements => Row.ElementCount;
        public string Status => Row.Reliability.Label();
        public string StatusColor => Row.Reliability == Reliability.Measured ? "#FF21E6A1" : "#FFFFC233";
        public IReadOnlyCollection<ElementRef> Refs => Row.Items.Select(ElementRef.Of).Distinct().ToList();
    }

    /// <summary>Une couche d'un element (detail de l'onglet Couches).</summary>
    public sealed class LayerItemRow
    {
        public LayerItemRow(ElementQuantity q)
        {
            Q = q;
        }

        public ElementQuantity Q { get; }
        public long ElementId => Q.ElementId;
        public ElementRef Ref => ElementRef.Of(Q);
        public string Source => string.IsNullOrEmpty(Q.SourceDocument) ? string.Empty : Q.SourceDocument;
        public string Category => DanCI.BOQ.Core.Extraction.LayerSummary.CategoryLabel(Q.Category);
        public string TypeName => Q.TypeName;
        public string Level => Q.Level;
        public string Layer => Get(DanCI.BOQ.Core.Extraction.LayerSummary.Index);
        public double Thickness => Num(Get(DanCI.BOQ.Core.Extraction.LayerSummary.Thickness));
        public double Area => Num(Get(DanCI.BOQ.Core.Extraction.LayerSummary.Area));
        public double Volume => Q.Value;
        public string Status => Q.Reliability.Label();
        public string StatusColor => Q.Reliability == Reliability.Measured ? "#FF21E6A1" : "#FFFFC233";
        public string Method => Q.MethodDescription + (string.IsNullOrEmpty(Q.Assumption) ? string.Empty : " — " + Q.Assumption);

        private string Get(string key) => Q.Parameters != null && Q.Parameters.TryGetValue(key, out string v) ? v : string.Empty;

        private static double Num(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
    }

    /// <summary>Un niveau dans la liste de l'onglet « Par niveau ».</summary>
    public sealed class LevelBlockRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");
        private readonly string currency;

        public LevelBlockRow(DanCI.BOQ.Core.Engine.LevelBlock block, string currency)
        {
            Block = block;
            this.currency = currency;
        }

        public DanCI.BOQ.Core.Engine.LevelBlock Block { get; }
        public string Name => Block.Level;
        public string TotalText => Block.Total.ToString("N2", Fr) + " " + currency + (Block.IsComplete ? string.Empty : " *");
        public int Elements => Block.ElementCount;
        public string Detail => Block.Lots.Count + " lot(s) · " + Block.ArticleCount + " article(s) · " + Block.ElementCount + " élément(s)";
        public string Lots => string.Join("  ", Block.Lots.Select(l => l.Code));
        public string Summary => "NIVEAU « " + Block.Level + " » — " + Detail + " — " + TotalText.Replace(" *", string.Empty) + " HT" +
                                 (Block.IsComplete ? string.Empty : " (incomplet : lignes sans prix, montant minorant)");
        public IReadOnlyCollection<ElementRef> Refs => Block.Lots.SelectMany(l => l.Articles).SelectMany(a => a.Elements).Select(ElementRef.Of).Distinct().ToList();
    }

    /// <summary>Un article du BOQ sur un niveau.</summary>
    public sealed class LevelArticleRow
    {
        private readonly string currency;

        public LevelArticleRow(DanCI.BOQ.Core.Engine.LevelLot lot, DanCI.BOQ.Core.Engine.LevelArticle a, string currency)
        {
            Lot = lot;
            A = a;
            this.currency = currency;
        }

        public DanCI.BOQ.Core.Engine.LevelLot Lot { get; }
        public DanCI.BOQ.Core.Engine.LevelArticle A { get; }
        public string LotHeader => "LOT " + Lot.Code + " – " + Lot.Designation + "  |  " + Lot.Total.ToString("N2", CultureInfo.GetCultureInfo("fr-FR")) + " " + currency +
                                   (Lot.IsComplete ? string.Empty : "  (incomplet)");
        public string Code => A.Code;
        public string Designation => A.Designation;
        public string Unit => A.Unit.Symbol();
        public decimal NetQuantity => A.NetQuantity;
        public decimal Quantity => A.Quantity;
        public decimal? UnitPrice => A.UnitPrice;
        public decimal? Amount => A.Amount;
        public int ElementCount => A.ElementCount;
        public string Status => A.Reliability.Label();
        public string StatusColor => A.Reliability == Reliability.Measured ? "#FF21E6A1" : A.Reliability == Reliability.Manual ? "#FFB388FF" : "#FFFFC233";
        public IReadOnlyCollection<ElementRef> Refs => A.Elements.Select(ElementRef.Of).Distinct().ToList();
    }

    /// <summary>Un materiau de l'inventaire (tous les elements modelises qui le portent).</summary>
    public sealed class InventoryGroupRow
    {
        public InventoryGroupRow(DanCI.BOQ.Core.Extraction.MaterialInventoryRow row)
        {
            Row = row;
        }

        public DanCI.BOQ.Core.Extraction.MaterialInventoryRow Row { get; }
        public string Material => Row.Material;
        public string Categories => Row.Categories;
        public string Levels => Row.Levels;
        public double Area => Row.AreaM2;
        public double Volume => Row.VolumeM3;
        public int Elements => Row.ElementCount;
        public string Flag => Row.IsGeneric ? "⚠ générique" : string.Empty;
        public string StatusColor => Row.IsGeneric ? "#FFFFC233" : "#FF21E6A1";
        public string Status => Row.IsGeneric ? "⚠ à préciser" : "✅ défini";
        public IReadOnlyCollection<ElementRef> Refs => Row.Items.Select(ElementRef.Of).Distinct().ToList();
    }

    /// <summary>Un element dans l'inventaire (un materiau d'un element).</summary>
    public sealed class InventoryItemRow
    {
        public InventoryItemRow(ElementQuantity q)
        {
            Q = q;
        }

        public ElementQuantity Q { get; }
        public long ElementId => Q.ElementId;
        public ElementRef Ref => ElementRef.Of(Q);
        public string Source => Q.SourceDocument ?? string.Empty;
        public string Category => DanCI.BOQ.Core.Extraction.MaterialInventory.CategoryLabel(Q);
        public string Family => Q.Family;
        public string TypeName => Q.TypeName;
        public string Level => Q.Level;
        public double Area => Q.Parameters != null && Q.Parameters.TryGetValue(DanCI.BOQ.Core.Extraction.MaterialInventory.Area, out string v) &&
                              double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
        public double Volume => Q.Value;
        public string Method => Q.MethodDescription;
    }

    /// <summary>Un element generique a verifier.</summary>
    public sealed class GenericRow
    {
        public GenericRow(DanCI.BOQ.Core.Extraction.GenericFinding f)
        {
            F = f;
        }

        public DanCI.BOQ.Core.Extraction.GenericFinding F { get; }
        public long ElementId => F.Element.ElementId;
        public ElementRef Ref => ElementRef.Of(F.Element);
        public string Source => F.Element.SourceDocument ?? string.Empty;
        public string Category => DanCI.BOQ.Core.Extraction.MaterialInventory.CategoryLabel(F.Element);
        public string Family => F.Element.Family;
        public string TypeName => F.Element.TypeName;
        public string Material => F.Element.Material;
        public string Level => F.Element.Level;
        public string Problem
        {
            get
            {
                switch (F.Code)
                {
                    case "MODELE_GENERIQUE": return "Modèle générique";
                    case "TYPE_GENERIQUE": return "Type / famille générique";
                    case "SANS_MATERIAU": return "Aucun matériau";
                    case "MATERIAU_PAR_CATEGORIE": return "Matériau « par catégorie »";
                    default: return "Matériau générique";
                }
            }
        }
        public string Reason => F.Reason;
    }

    /// <summary>Un element d'un niveau (navigateur element par element).</summary>
    public sealed class LevelElementRow
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public LevelElementRow(DanCI.BOQ.Core.Engine.LevelElement e, int position)
        {
            E = e;
            Position = position;
        }

        public DanCI.BOQ.Core.Engine.LevelElement E { get; }
        public int Position { get; }
        public ElementRef Ref => E.Ref;
        public long ElementId => E.Ref.ElementId;
        public string Source => E.Sample.SourceDocument ?? string.Empty;
        public string Category => DanCI.BOQ.Core.Extraction.MaterialInventory.CategoryLabel(E.Sample);
        public string Family => E.Sample.Family;
        public string TypeName => E.Sample.TypeName;
        public string Articles => E.InBoq ? string.Join(", ", E.Articles) : "— (hors BOQ)";
        public string Quantities => string.Join("  ·  ", E.Quantities.Select(q => q.Value.ToString(q.Unit == BoqUnit.Kilogram || q.Unit == BoqUnit.Each ? "N0" : "N3", Fr) + " " + q.Unit.Symbol()));
        public string Materials => string.Join(", ", E.Materials);
        public string Status => E.InBoq ? E.Reliability.Label() : "inventaire";
        public string StatusColor => !E.InBoq ? "#FF8AA4C8" : E.Reliability == Reliability.Measured ? "#FF21E6A1" : E.Reliability == Reliability.Manual ? "#FFB388FF" : "#FFFFC233";
        public string Label => Category + " « " + TypeName + " » (" + ElementId + ")";
    }

    /// <summary>Ligne du Gantt de la fenetre (en-tete de niveau ou tache), geometrie deja calculee.</summary>
    public sealed class GanttRowView
    {
        public bool IsHeader { get; set; }
        public bool IsTask => !IsHeader;
        public string Title { get; set; }
        public string Duration { get; set; }
        public string Start { get; set; }
        public string Finish { get; set; }
        public string Float { get; set; }
        public double BarLeft { get; set; }
        public double BarWidth { get; set; }
        public string Color { get; set; }
        public bool IsCritical { get; set; }
        public string TitleColor => IsCritical ? "#FFFF4D6D" : null;
        public string Tooltip { get; set; }
        public DanCI.BOQ.Core.Planning.PlanTask Task { get; set; }
    }

    /// <summary>Un mois du plan de tresorerie (barre du mois + courbe en S).</summary>
    public sealed class CashMonthRow
    {
        public string Month { get; set; }
        public string Amount { get; set; }
        public string Cumulative { get; set; }
        public string Share { get; set; }
        public double BarHeight { get; set; }
    }

    /// <summary>Graduation de l'echelle de temps du Gantt.</summary>
    public sealed class GanttTick
    {
        public double Left { get; set; }
        public double Width { get; set; }
        public string Label { get; set; }
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


namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Forfait editable dans l'onglet Forfaits.</summary>
    public sealed class ManualLineRow : Mvvm.ObservableObject
    {
        public static readonly string[] UnitChoices = { "Ff", "U", "ml", "m²", "m³", "kg", "t" };
        public static readonly string[] CurrencyChoices = { "USD", "CDF" };

        public string Lot { get; set; } = "00";
        public string Code { get; set; }
        public string Designation { get; set; } = "Nouveau forfait";
        public string Unit { get; set; } = "Ff";
        public decimal Quantity { get; set; } = 1m;
        public decimal? UnitPrice { get; set; }
        public string Currency { get; set; } = "USD";
        public string Justification { get; set; }

        public static ManualLineRow From(ManualLine m) => new ManualLineRow
        {
            Lot = m.Lot,
            Code = m.Code,
            Designation = m.Designation,
            Unit = m.Unit.Symbol(),
            Quantity = m.Quantity,
            UnitPrice = m.UnitPrice,
            Currency = m.Currency.ToString(),
            Justification = m.Justification
        };

        public ManualLine ToManualLine() => new ManualLine
        {
            Lot = Lot?.Trim(),
            Code = string.IsNullOrWhiteSpace(Code) ? null : Code.Trim(),
            Designation = Designation,
            Unit = BoqUnits.TryParse(Unit, out BoqUnit u) ? u : BoqUnit.LumpSum,
            Quantity = Quantity,
            UnitPrice = UnitPrice,
            Currency = Currency == "CDF" ? DanCI.BOQ.Core.Pricing.Currency.CDF : DanCI.BOQ.Core.Pricing.Currency.USD,
            Justification = Justification
        };
    }
}
