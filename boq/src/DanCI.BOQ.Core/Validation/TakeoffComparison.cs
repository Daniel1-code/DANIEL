using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Validation
{
    /// <summary>Une mesure manuelle : par element (ElementId + nature) ou par article (code).</summary>
    public sealed class ManualEntry
    {
        public string SourceDocument { get; set; } = string.Empty;
        public long? ElementId { get; set; }
        public string Kind { get; set; }
        public string ArticleCode { get; set; }
        public decimal Value { get; set; }
        public string Comment { get; set; }
    }

    public enum ComparisonStatus
    {
        /// <summary>Ecart dans la tolerance.</summary>
        Conforme,
        /// <summary>Ecart superieur a la tolerance : a expliquer.</summary>
        HorsTolerance,
        /// <summary>Mesure manuellement mais absent du metre du modele.</summary>
        AbsentDuModele,
        /// <summary>Present dans le modele mais non mesure manuellement (pas d'ecart calculable).</summary>
        NonMesure
    }

    public sealed class ComparisonRow
    {
        public string Key { get; set; }
        public string SourceDocument { get; set; }
        public long? ElementId { get; set; }
        public string ArticleCode { get; set; }
        public string Kind { get; set; }
        public string Description { get; set; }
        public string Unit { get; set; }
        public decimal? Model { get; set; }
        public decimal? Manual { get; set; }
        public decimal? Difference => Model.HasValue && Manual.HasValue ? Model - Manual : (decimal?)null;

        /// <summary>Ecart relatif au metre manuel (reference), en %.</summary>
        public decimal? DifferencePercent =>
            Model.HasValue && Manual.HasValue && Manual.Value != 0m ? (Model.Value - Manual.Value) / Manual.Value * 100m : (decimal?)null;

        public decimal TolerancePercent { get; set; }
        public ComparisonStatus Status { get; set; }

        /// <summary>Causes probables d'ecart, deduites de la methode de mesure (a verifier, pas des certitudes).</summary>
        public List<string> Hints { get; } = new List<string>();

        public string Comment { get; set; }
    }

    /// <summary>Bilan par nature de quantite, sur les seuls elements mesures des deux cotes.</summary>
    public sealed class KindSummary
    {
        public string Kind { get; set; }
        public string Unit { get; set; }
        public int Compared { get; set; }
        public int OutOfTolerance { get; set; }
        public decimal ModelTotal { get; set; }
        public decimal ManualTotal { get; set; }
        public decimal? DifferencePercent => ManualTotal != 0m ? (ModelTotal - ManualTotal) / ManualTotal * 100m : (decimal?)null;
        public decimal TolerancePercent { get; set; }

        /// <summary>Critere de l'etape 8 : ecart sur le TOTAL de la nature inferieur a la tolerance.</summary>
        public bool WithinTolerance => DifferencePercent.HasValue && Math.Abs(DifferencePercent.Value) <= TolerancePercent;

        public bool IsTarget { get; set; }
    }

    public sealed class TakeoffComparisonReport
    {
        public List<ComparisonRow> Elements { get; } = new List<ComparisonRow>();
        public List<ComparisonRow> Articles { get; } = new List<ComparisonRow>();
        public List<KindSummary> Kinds { get; } = new List<KindSummary>();

        /// <summary>Vrai si TOUTES les natures cibles (beton, acier) mesurees sont dans la tolerance.</summary>
        public bool TargetsMet => Kinds.Where(k => k.IsTarget && k.Compared > 0).All(k => k.WithinTolerance) &&
                                  Kinds.Any(k => k.IsTarget && k.Compared > 0);
    }

    /// <summary>
    /// Comparaison du metre du modele a un metre manuel (etape 8). La reference est le metre
    /// MANUEL : ecart % = (modele - manuel) / manuel. Cible du cahier des charges : moins de 2 %
    /// sur le beton et l'acier, evaluee sur le TOTAL de chaque nature (les ecarts element par
    /// element servent a expliquer : au droit d'une jointure, Revit attribue le volume commun a
    /// un seul des deux elements, ce que le metreur ne fait pas forcement de la meme facon).
    /// </summary>
    public static class TakeoffComparer
    {
        public const decimal TargetTolerancePercent = 2m;
        public const decimal OtherTolerancePercent = 5m;

        /// <summary>Natures soumises a la cible de 2 % (beton et acier).</summary>
        public static readonly string[] TargetKinds = { QuantityKinds.ConcreteVolume, QuantityKinds.Reinforcement, QuantityKinds.StructuralSteel };

        public static decimal ToleranceFor(string kind) =>
            TargetKinds.Contains(kind, StringComparer.OrdinalIgnoreCase) ? TargetTolerancePercent : OtherTolerancePercent;

        public static TakeoffComparisonReport Compare(IReadOnlyList<ElementQuantity> model, BoqResult boq, IReadOnlyList<ManualEntry> manual)
        {
            var report = new TakeoffComparisonReport();
            model = model ?? new List<ElementQuantity>();
            manual = manual ?? new List<ManualEntry>();

            // --- par element : cle document | ElementId | nature (plusieurs materiaux d'un element : additionnes)
            var modelByKey = model
                .Where(q => !q.ExcludedByUser)
                .GroupBy(q => Key(q.SourceDocument, q.ElementId, q.Kind))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
            var manualByKey = manual
                .Where(m => m.ElementId.HasValue && !string.IsNullOrWhiteSpace(m.Kind))
                .GroupBy(m => Key(m.SourceDocument, m.ElementId.Value, m.Kind))
                .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);

            foreach (string key in modelByKey.Keys.Union(manualByKey.Keys).OrderBy(k => k, StringComparer.Ordinal))
            {
                modelByKey.TryGetValue(key, out List<ElementQuantity> qs);
                manualByKey.TryGetValue(key, out List<ManualEntry> ms);
                ElementQuantity first = qs?.First();
                ManualEntry m0 = ms?.First();
                var row = new ComparisonRow
                {
                    Key = key,
                    SourceDocument = first?.SourceDocument ?? m0?.SourceDocument ?? string.Empty,
                    ElementId = first?.ElementId ?? m0?.ElementId,
                    Kind = first?.Kind ?? m0?.Kind,
                    Description = first == null ? "(absent du modèle)" : (first.CategoryLabel ?? first.Category) + " — " + first.TypeName,
                    Unit = first?.Unit.Symbol(),
                    Model = qs?.Sum(q => (decimal)q.Value),
                    Manual = ms?.Sum(m => m.Value),
                    Comment = ms == null ? null : string.Join(" / ", ms.Select(m => m.Comment).Where(c => !string.IsNullOrWhiteSpace(c)))
                };
                row.TolerancePercent = ToleranceFor(row.Kind);
                row.Status = Status(row);
                if (row.Status == ComparisonStatus.HorsTolerance || row.Status == ComparisonStatus.AbsentDuModele)
                {
                    row.Hints.AddRange(Hints(row, qs));
                }
                report.Elements.Add(row);
            }

            // --- par nature (elements mesures des deux cotes)
            foreach (var g in report.Elements.Where(r => r.Model.HasValue && r.Manual.HasValue).GroupBy(r => r.Kind ?? string.Empty))
            {
                report.Kinds.Add(new KindSummary
                {
                    Kind = g.Key,
                    Unit = g.First().Unit,
                    Compared = g.Count(),
                    OutOfTolerance = g.Count(r => r.Status == ComparisonStatus.HorsTolerance),
                    ModelTotal = g.Sum(r => r.Model.Value),
                    ManualTotal = g.Sum(r => r.Manual.Value),
                    TolerancePercent = ToleranceFor(g.Key),
                    IsTarget = TargetKinds.Contains(g.Key, StringComparer.OrdinalIgnoreCase)
                });
            }
            report.Kinds.Sort((a, b) => string.CompareOrdinal(a.Kind, b.Kind));

            // --- par article (quantite NETTE, avant coefficients : un metre manuel est un metre net)
            if (boq != null)
            {
                var manualArticles = manual.Where(m => !m.ElementId.HasValue && !string.IsNullOrWhiteSpace(m.ArticleCode))
                    .GroupBy(m => m.ArticleCode.Trim(), StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.ToList(), StringComparer.Ordinal);
                foreach (BoqLine line in boq.AllLines.Where(l => manualArticles.ContainsKey(l.Code)))
                {
                    List<ManualEntry> ms = manualArticles[line.Code];
                    string kind = line.Contributions.FirstOrDefault()?.Element.Kind;
                    var row = new ComparisonRow
                    {
                        Key = line.Code,
                        ArticleCode = line.Code,
                        Kind = kind,
                        Description = line.Designation,
                        Unit = line.Unit.Symbol(),
                        Model = line.NetQuantity,
                        Manual = ms.Sum(m => m.Value),
                        TolerancePercent = ToleranceFor(kind),
                        Comment = string.Join(" / ", ms.Select(m => m.Comment).Where(c => !string.IsNullOrWhiteSpace(c)))
                    };
                    row.Status = Status(row);
                    report.Articles.Add(row);
                }
                foreach (string code in manualArticles.Keys.Where(c => boq.AllLines.All(l => l.Code != c)).OrderBy(c => c, StringComparer.Ordinal))
                {
                    report.Articles.Add(new ComparisonRow
                    {
                        Key = code,
                        ArticleCode = code,
                        Description = "(article absent du BOQ)",
                        Manual = manualArticles[code].Sum(m => m.Value),
                        Status = ComparisonStatus.AbsentDuModele
                    });
                }
            }
            return report;
        }

        private static ComparisonStatus Status(ComparisonRow r)
        {
            if (!r.Model.HasValue) return ComparisonStatus.AbsentDuModele;
            if (!r.Manual.HasValue) return ComparisonStatus.NonMesure;
            if (r.Manual.Value == 0m) return r.Model.Value == 0m ? ComparisonStatus.Conforme : ComparisonStatus.HorsTolerance;
            return Math.Abs(r.DifferencePercent.Value) <= r.TolerancePercent ? ComparisonStatus.Conforme : ComparisonStatus.HorsTolerance;
        }

        /// <summary>Pistes d'explication tirees de la methode de mesure. A verifier une par une.</summary>
        private static IEnumerable<string> Hints(ComparisonRow row, List<ElementQuantity> qs)
        {
            if (qs == null)
            {
                yield return "Élément absent du métré du modèle : exclu (phase, portée, DanCI_BOQ_Exclure, option de conception), " +
                             "sans matériau béton reconnu, ou ElementId différent (élément recréé depuis le métré manuel).";
                yield break;
            }
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            bool higher = row.Difference > 0;
            if (qs.Any(q => q.Reliability != Reliability.Measured))
            {
                yield return "Quantité du modèle " + qs.First(q => q.Reliability != Reliability.Measured).Reliability.Label() +
                             " : " + qs.Select(q => q.Assumption).FirstOrDefault(a => !string.IsNullOrEmpty(a));
            }
            switch (row.Kind)
            {
                case QuantityKinds.ConcreteVolume:
                    yield return "Jointures : au droit d'une jointure, Revit attribue le volume commun à UN seul élément (ordre de jointure). " +
                                 "Comparer aussi le total poteaux + poutres + dalles, qui n'en dépend pas.";
                    if (higher) yield return "Modèle plus fort : chevauchement non joint (voir l'audit, rubrique 6), ou surépaisseur modélisée.";
                    else yield return "Modèle plus faible : vides ou réservations modélisés, matériau non béton sur une partie de l'élément, Parts.";
                    if (qs.Count > 1) yield return qs.Count + " matériaux béton dans l'élément : volumes additionnés.";
                    break;
                case QuantityKinds.Formwork:
                    string contacts = qs.Select(q => Param(q, "ContactsDeduitsM2")).FirstOrDefault(v => v != null);
                    yield return "Coffrage : faces de contact déduites (" + (contacts ?? "?") + " m²), dessus de pente < seuil exclus, sous-faces de fondation exclues : " +
                                 "vérifier que le métré manuel applique les mêmes conventions.";
                    break;
                case QuantityKinds.Reinforcement:
                    yield return "Armatures : la longueur Revit inclut crochets et façonnage ; les recouvrements non modélisés ne sont PAS comptés " +
                                 "(ils relèvent du coefficient de chutes). Comparer au carnet de ferraillage, longueurs développées.";
                    break;
                case QuantityKinds.StructuralSteel:
                    yield return "Charpente : " + qs.First().MethodDescription +
                                 " Longueur de coupe Revit (après coupes et ajustements) contre longueur entre axes du métré manuel ?";
                    break;
                case QuantityKinds.Masonry:
                    yield return "Maçonnerie : " + qs.First().MethodDescription;
                    break;
            }
            yield return "Écart : " + (row.Difference?.ToString("+0.###;-0.###", fr) ?? "?") + " " + row.Unit +
                         " (" + (row.DifferencePercent?.ToString("+0.00;-0.00", fr) ?? "?") + " %).";
        }

        private static string Param(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) ? v : null;

        public static string Key(string sourceDocument, long elementId, string kind) =>
            (sourceDocument ?? string.Empty) + "|" + elementId.ToString(CultureInfo.InvariantCulture) + "|" + (kind ?? string.Empty);
    }
}
