using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Dashboard
{
    /// <summary>Niveau d'un voyant du tableau de bord.</summary>
    public enum SignalLevel
    {
        Ok,
        Info,
        Attention,
        Critique
    }

    /// <summary>Voyant : un point a surveiller, avec l'action qui le leve.</summary>
    public sealed class DashboardSignal
    {
        public SignalLevel Level { get; set; }
        public string Title { get; set; }
        public string Detail { get; set; }
        public string Action { get; set; }
        /// <summary>Ou corriger : prix, audit, nc, devis, identite (navigation depuis la fenetre).</summary>
        public string Target { get; set; }
    }

    public sealed class LotShare
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal Amount { get; set; }
        /// <summary>Part du total des travaux, entre 0 et 1.</summary>
        public decimal Share { get; set; }
        public bool IsComplete { get; set; }
        public int Lines { get; set; }
        public int LinesWithoutPrice { get; set; }
    }

    /// <summary>Article du Pareto : les lignes qui font le devis.</summary>
    public sealed class ParetoLine
    {
        public int Rank { get; set; }
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal Amount { get; set; }
        public decimal Share { get; set; }
        public decimal CumulativeShare { get; set; }
        public Reliability Reliability { get; set; }
    }

    /// <summary>Quantite cle (beton, acier...), en quantite NETTE en place.</summary>
    public sealed class KeyQuantity
    {
        public string Label { get; set; }
        public string Kind { get; set; }
        public decimal Value { get; set; }
        public BoqUnit Unit { get; set; }
        public decimal MeasuredShare { get; set; }
    }

    public sealed class MissingPrice
    {
        public string Code { get; set; }
        public string Designation { get; set; }
        public decimal Quantity { get; set; }
        public BoqUnit Unit { get; set; }
    }

    /// <summary>
    /// Tableau de bord de pilotage du devis. Calcule UNIQUEMENT a partir du BOQ, de l'audit et
    /// du metre : aucune valeur n'y est ajoutee. Partage par la fenetre, le classeur et le PDF.
    /// </summary>
    public sealed class BoqDashboard
    {
        public string Currency { get; set; }
        public decimal WorksTotal { get; set; }
        public decimal TotalExcludingVat { get; set; }
        public decimal Vat { get; set; }
        public decimal VatPercent { get; set; }
        public decimal TotalIncludingVat { get; set; }
        public decimal MarkupsTotal { get; set; }
        public string SecondaryCurrency { get; set; }
        public decimal? TotalIncludingVatSecondary { get; set; }
        public bool IsComplete { get; set; }

        public int LotCount { get; set; }
        public int LineCount { get; set; }
        public int PricedLineCount { get; set; }
        /// <summary>Lignes avec prix / lignes, entre 0 et 1.</summary>
        public decimal PriceCoverage => LineCount == 0 ? 0m : (decimal)PricedLineCount / LineCount;

        public decimal? ReliabilityIndex { get; set; }
        public decimal MeasuredAmount { get; set; }
        public decimal EstimatedAmount { get; set; }
        public decimal ManualAmount { get; set; }

        public int ElementCount { get; set; }
        public int QuantityCount { get; set; }
        public int ExcludedCount { get; set; }
        public int UnclassifiedLineCount { get; set; }
        public int AuditErrors { get; set; }
        public int AuditWarnings { get; set; }

        public decimal? ReferenceArea { get; set; }
        public string ReferenceAreaLabel { get; set; }
        /// <summary>Cout TTC au m² de surface de reference ; null sans surface.</summary>
        public decimal? CostPerSquareMeter { get; set; }
        /// <summary>Ratio armatures / beton (kg/m³) ; null sans beton ou sans armatures.</summary>
        public decimal? RebarRatio { get; set; }

        public List<LotShare> Lots { get; } = new List<LotShare>();
        public List<ParetoLine> Pareto { get; } = new List<ParetoLine>();
        public List<KeyQuantity> KeyQuantities { get; } = new List<KeyQuantity>();
        public List<MissingPrice> MissingPrices { get; } = new List<MissingPrice>();
        public List<DashboardSignal> Signals { get; } = new List<DashboardSignal>();

        /// <summary>Note globale de 0 a 100 : prix renseignes, fiabilite, audit (voir Score()).</summary>
        public int HealthScore { get; set; }

        public SignalLevel WorstLevel => Signals.Count == 0 ? SignalLevel.Ok : Signals.Max(s => s.Level);

        private static readonly (string Kind, string Label)[] KeyKinds =
        {
            (QuantityKinds.Excavation, "Terrassements"),
            (QuantityKinds.ConcreteVolume, "Béton"),
            (QuantityKinds.Reinforcement, "Armatures"),
            (QuantityKinds.Formwork, "Coffrage"),
            (QuantityKinds.StructuralSteel, "Charpente métallique"),
            (QuantityKinds.Masonry, "Maçonnerie"),
            (QuantityKinds.Plaster, "Enduits"),
            (QuantityKinds.Paint, "Peinture"),
            (QuantityKinds.FloorFinish, "Revêtements de sol"),
            (QuantityKinds.Roofing, "Couverture")
        };

        public static BoqDashboard Build(BoqResult boq, AuditReport audit, IReadOnlyList<ElementQuantity> quantities, ProjectInfo project,
            DashboardOptions options = null)
        {
            options = options ?? new DashboardOptions();
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            quantities = quantities ?? new ElementQuantity[0];
            var d = new BoqDashboard
            {
                Currency = boq.Currency.ToString(),
                WorksTotal = boq.WorksTotal,
                TotalExcludingVat = boq.TotalExcludingVat,
                Vat = boq.Vat,
                VatPercent = boq.VatPercent,
                TotalIncludingVat = boq.TotalIncludingVat,
                MarkupsTotal = boq.Markups.Sum(m => m.Amount),
                SecondaryCurrency = boq.SecondaryCurrency?.ToString(),
                TotalIncludingVatSecondary = boq.TotalIncludingVatSecondary,
                IsComplete = boq.IsComplete,
                ReliabilityIndex = boq.ReliabilityIndex,
                MeasuredAmount = boq.MeasuredAmount,
                EstimatedAmount = boq.EstimatedAmount,
                ManualAmount = boq.ManualAmount,
                QuantityCount = quantities.Count,
                ElementCount = quantities.Select(q => (q.SourceDocument ?? string.Empty, q.ElementId)).Distinct().Count(),
                ExcludedCount = boq.Excluded.Count,
                AuditErrors = audit?.ErrorCount ?? 0,
                AuditWarnings = audit?.WarningCount ?? 0,
                ReferenceArea = project?.ReferenceArea > 0m ? project.ReferenceArea : null,
                ReferenceAreaLabel = project?.ReferenceAreaLabel
            };

            List<BoqLine> lines = boq.AllLines.ToList();
            d.LotCount = boq.Lots.Count;
            d.LineCount = lines.Count;
            d.PricedLineCount = lines.Count(l => l.Amount.HasValue);
            d.UnclassifiedLineCount = lines.Count(l => l.IsUnclassified);

            foreach (BoqLotResult lot in boq.Lots)
            {
                d.Lots.Add(new LotShare
                {
                    Code = lot.Code,
                    Designation = lot.Designation,
                    Amount = lot.Total,
                    Share = boq.WorksTotal > 0m ? lot.Total / boq.WorksTotal : 0m,
                    IsComplete = lot.IsComplete,
                    Lines = lot.Lines.Count,
                    LinesWithoutPrice = lot.Lines.Count(l => !l.Amount.HasValue)
                });
            }

            decimal cumulative = 0m;
            int rank = 0;
            foreach (BoqLine l in lines.Where(l => l.Amount > 0m).OrderByDescending(l => l.Amount.Value).ThenBy(l => l.Code, StringComparer.Ordinal).Take(Math.Max(1, options.ParetoSize)))
            {
                decimal share = boq.WorksTotal > 0m ? l.Amount.Value / boq.WorksTotal : 0m;
                cumulative += share;
                d.Pareto.Add(new ParetoLine
                {
                    Rank = ++rank,
                    Code = l.Code,
                    Designation = l.Designation,
                    Amount = l.Amount.Value,
                    Share = share,
                    CumulativeShare = Math.Min(1m, cumulative),
                    Reliability = l.Reliability
                });
            }

            foreach (BoqLine l in lines.Where(l => !l.Amount.HasValue).OrderBy(l => l.Code, StringComparer.Ordinal))
            {
                d.MissingPrices.Add(new MissingPrice { Code = l.Code, Designation = l.Designation, Quantity = l.Quantity, Unit = l.Unit });
            }

            // Quantites cles : contributions RETENUES dans le BOQ (les exclus n'y sont pas), en net.
            var used = lines.SelectMany(l => l.Contributions).Select(c => c.Element).ToList();
            foreach ((string kind, string label) in KeyKinds)
            {
                foreach (var g in used.Where(q => q.Kind == kind).GroupBy(q => q.Unit))
                {
                    decimal total = g.Sum(q => (decimal)q.Value);
                    if (total <= 0m) continue;
                    decimal measured = g.Where(q => q.Reliability == Reliability.Measured).Sum(q => (decimal)q.Value);
                    d.KeyQuantities.Add(new KeyQuantity { Label = label, Kind = kind, Unit = g.Key, Value = total, MeasuredShare = measured / total });
                }
            }
            KeyQuantity concrete = d.KeyQuantities.FirstOrDefault(k => k.Kind == QuantityKinds.ConcreteVolume && k.Unit == BoqUnit.CubicMeter);
            KeyQuantity rebar = d.KeyQuantities.FirstOrDefault(k => k.Kind == QuantityKinds.Reinforcement && k.Unit == BoqUnit.Kilogram);
            if (concrete != null && rebar != null && concrete.Value > 0m)
            {
                d.RebarRatio = rebar.Value / concrete.Value;
            }
            if (d.ReferenceArea.HasValue && boq.TotalIncludingVat > 0m)
            {
                d.CostPerSquareMeter = boq.TotalIncludingVat / d.ReferenceArea.Value;
            }

            AddSignals(d, boq, project, options);
            d.HealthScore = Score(d);
            return d;
        }

        /// <summary>
        /// Note de sante sur 100 : 50 pour la part des lignes avec prix, 30 pour l'indice de
        /// fiabilite (part mesuree), 20 pour l'audit (-5 par erreur, -1 par avertissement, plancher 0).
        /// Indicateur de pilotage, PAS une garantie d'exactitude.
        /// </summary>
        public static int Score(BoqDashboard d)
        {
            decimal prices = 50m * d.PriceCoverage;
            decimal reliability = 30m * (d.ReliabilityIndex ?? 0m);
            decimal auditPoints = Math.Max(0m, 20m - 5m * d.AuditErrors - 1m * d.AuditWarnings);
            return (int)Math.Round(prices + reliability + auditPoints, MidpointRounding.AwayFromZero);
        }

        private static void AddSignals(BoqDashboard d, BoqResult boq, ProjectInfo project, DashboardOptions options)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (d.MissingPrices.Count > 0)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Critique,
                    Title = d.MissingPrices.Count + " ligne(s) sans prix unitaire",
                    Detail = "Les totaux sont des MINORANTS du coût réel.",
                    Target = "prix",
                    Action = "Paramètres du projet > Prix unitaires : saisir les PU manquants."
                });
            }
            if (d.AuditErrors > 0)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Critique,
                    Title = d.AuditErrors + " erreur(s) d'audit",
                    Detail = "Des éléments faussent le métré (quantité nulle, non classés, doublons…).",
                    Target = "audit",
                    Action = "Onglet Audit : corriger le modèle ou les règles, puis relancer l'analyse."
                });
            }
            if (d.ReliabilityIndex.HasValue && d.ReliabilityIndex.Value < 0.8m)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = d.ReliabilityIndex.Value < 0.5m ? SignalLevel.Critique : SignalLevel.Attention,
                    Title = "Fiabilité " + (d.ReliabilityIndex.Value * 100m).ToString("0", fr) + " % (objectif ≥ 80 %)",
                    Detail = "Une part importante du montant vient de quantités estimées (ratios, hypothèses).",
                    Target = "audit",
                    Action = "Modéliser les armatures / éléments estimés, ou faire valider les hypothèses."
                });
            }
            if (d.UnclassifiedLineCount > 0)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Attention,
                    Title = d.UnclassifiedLineCount + " ligne(s) non classée(s) (lot NC)",
                    Detail = "Des quantités ne correspondent à aucune règle de mapping.",
                    Target = "nc",
                    Action = "Compléter config\\regles_mapping.json ou classer les éléments."
                });
            }
            if (d.AuditWarnings > 0)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Attention,
                    Title = d.AuditWarnings + " avertissement(s) d'audit",
                    Detail = "À lire avant de signer le devis.",
                    Target = "audit",
                    Action = "Onglet Audit."
                });
            }
            if (boq.Markups.Count > 0 && boq.Markups.All(m => m.Percent == 0m))
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Attention,
                    Title = "Imprévus, frais généraux et bénéfice à 0 %",
                    Detail = "Le pied de devis ne contient aucune majoration.",
                    Target = "devis",
                    Action = "Paramètres du projet > Devis : fixer les pourcentages du projet."
                });
            }
            if (string.IsNullOrWhiteSpace(project?.Revision))
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Info,
                    Title = "Indice de révision non renseigné",
                    Target = "identite",
                    Action = "Paramètres du projet > Identité : indice (A, B, C…)."
                });
            }
            if (!d.ReferenceArea.HasValue)
            {
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Info,
                    Title = "Surface de référence non renseignée",
                    Detail = "Le coût au m² ne peut pas être calculé.",
                    Target = "identite",
                    Action = "Paramètres du projet > Identité : surface de référence (m²)."
                });
            }
            DateTime? rateDate = options.ExchangeRateDate;
            if (d.SecondaryCurrency != null && rateDate.HasValue && ((options.Today ?? DateTime.Today) - rateDate.Value).TotalDays > 30)
            {
                // Le taux est date : on rappelle de le verifier s'il a plus de 30 jours.
                d.Signals.Add(new DashboardSignal
                {
                    Level = SignalLevel.Attention,
                    Title = "Taux de change du " + rateDate.Value.ToString("dd/MM/yyyy", fr) + " (plus de 30 jours)",
                    Target = "devis",
                    Action = "Paramètres du projet > Devis : saisir le taux du jour."
                });
            }
            if (d.Signals.Count == 0)
            {
                d.Signals.Add(new DashboardSignal { Level = SignalLevel.Ok, Title = "Aucun point bloquant détecté", Detail = "Devis à relire et signer par le responsable." });
            }
            // Tri stable : les plus graves d'abord, l'ordre ci-dessus a niveau egal.
            List<DashboardSignal> sorted = d.Signals.OrderByDescending(x => x.Level).ToList();
            d.Signals.Clear();
            d.Signals.AddRange(sorted);
        }
    }

    public sealed class DashboardOptions
    {
        /// <summary>Date de reference (tests) ; null = aujourd'hui.</summary>
        public DateTime? Today { get; set; }

        /// <summary>Date du taux de change (voyant « taux de plus de 30 jours »).</summary>
        public DateTime? ExchangeRateDate { get; set; }

        public int ParetoSize { get; set; } = 10;
    }
}
