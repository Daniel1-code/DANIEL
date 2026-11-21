using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Engine
{
    /// <summary>
    /// Moteur du BOQ : quantites d'elements -&gt; classement -&gt; coefficients -&gt; valorisation
    /// -&gt; pied de devis. Aucune dependance Revit, aucun etat : memes entrees, meme BOQ, au
    /// centime pres, quel que soit l'ordre des entrees.
    ///
    /// Ce que le moteur NE fait JAMAIS :
    ///  - inventer une quantite ou un prix ;
    ///  - convertir une quantite d'une unite a une autre ;
    ///  - ecarter un element sans le dire (tout element ecarte figure dans Excluded ou Issues).
    /// </summary>
    public sealed class BoqEngine
    {
        public BoqResult Build(
            IEnumerable<ElementQuantity> quantities,
            BoqConfiguration configuration,
            IEnumerable<ManualLine> manualLines = null)
        {
            if (configuration == null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }
            List<BoqIssue> configIssues = ConfigurationValidator.Validate(configuration);
            CheckCurrencies(configuration, configIssues);
            if (configIssues.Any(i => i.Severity == IssueSeverity.Error))
            {
                throw new BoqConfigurationException(configIssues);
            }

            MappingCatalog catalog = configuration.Mapping;
            EstimateSettings settings = configuration.Settings ?? new EstimateSettings();
            var result = new BoqResult
            {
                Currency = settings.Currency,
                PricedQuantity = settings.PricedQuantity,
                VatPercent = settings.VatPercent,
                SecondaryCurrency = settings.SecondaryCurrency
            };
            result.Issues.AddRange(configIssues);

            // 1. Classement des quantites, dans un ordre deterministe.
            var byArticle = new Dictionary<string, List<LineContribution>>(StringComparer.Ordinal);
            var unclassified = new List<ElementQuantity>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (ElementQuantity q in (quantities ?? Enumerable.Empty<ElementQuantity>())
                         .Where(q => q != null)
                         .OrderBy(q => q.SortKey, StringComparer.Ordinal)
                         .ThenBy(q => q.Value))
            {
                if (!IsUsable(q, result))
                {
                    continue;
                }

                string identity = q.SourceDocument + "|" + q.ElementId + "|" + q.Kind + "|" + q.Material + "|" + q.Unit;
                if (!seen.Add(identity))
                {
                    result.Excluded.Add(new ExcludedQuantity(q, "Quantité fournie deux fois pour le même élément (double comptage évité).", null));
                    result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "DOUBLON_QUANTITE",
                        "L'élément " + q.ElementId + " fournit deux fois la même quantité (" + q.Kind + ", " + q.Material +
                        ") : la seconde est écartée. Corriger l'extraction."), q));
                    continue;
                }

                if (q.ExcludedByUser)
                {
                    result.Excluded.Add(new ExcludedQuantity(q, "Paramètre DanCI_BOQ_Exclure = Oui.", null));
                    continue;
                }

                MappingRule rule = catalog.Match(q);
                if (rule == null)
                {
                    unclassified.Add(q);
                    continue;
                }
                if (rule.Exclude)
                {
                    result.Excluded.Add(new ExcludedQuantity(q, rule.Reason, rule.Id));
                    continue;
                }

                Article article = catalog.FindArticle(rule.Article);
                if (article.Unit != q.Unit)
                {
                    result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "UNITE_INCOMPATIBLE",
                        "La règle " + rule.Id + " rattache une quantité en " + q.Unit.Symbol() + " à l'article " +
                        article.Code + " en " + article.Unit.Symbol() + ". Quantité laissée en non classés.")
                    { ArticleCode = article.Code }, q));
                    unclassified.Add(q);
                    continue;
                }

                if (!byArticle.TryGetValue(article.Code, out List<LineContribution> list))
                {
                    byArticle[article.Code] = list = new List<LineContribution>();
                }
                list.Add(new LineContribution(q, rule.Id));
            }

            // 2. Lignes metrees, dans l'ordre du catalogue.
            var lotLines = new Dictionary<string, List<BoqLine>>(StringComparer.Ordinal);
            var coefficientUse = new Dictionary<string, List<string>>(StringComparer.Ordinal);
            foreach (Article article in catalog.Articles.Where(a => byArticle.ContainsKey(a.Code)))
            {
                List<LineContribution> contributions = byArticle[article.Code];
                if (article.SplitBy != ArticleSplit.Type)
                {
                    Add(lotLines, article.Lot, BuildMeasuredLine(article, contributions, null, configuration, settings, result, coefficientUse));
                    continue;
                }

                // Une sous-ligne par famille et type, dans un ordre stable (alphabetique).
                // Les codes 07.01.01, 07.01.02... dependent des types presents : les prix se
                // rattachent donc par NOM de type, jamais par ce code.
                var groups = contributions
                    .GroupBy(c => new { Family = c.Element.Family ?? string.Empty, Type = c.Element.TypeName ?? string.Empty })
                    .OrderBy(g => g.Key.Family, StringComparer.Ordinal)
                    .ThenBy(g => g.Key.Type, StringComparer.Ordinal);
                int sub = 0;
                foreach (var g in groups)
                {
                    sub++;
                    var split = new SplitKey
                    {
                        Code = article.Code + "." + sub.ToString("D2", CultureInfo.InvariantCulture),
                        Family = g.Key.Family,
                        TypeName = g.Key.Type
                    };
                    Add(lotLines, article.Lot, BuildMeasuredLine(article, g.ToList(), split, configuration, settings, result, coefficientUse));
                }
            }

            // 3. Forfaits saisis.
            int manualIndex = 0;
            foreach (ManualLine manual in manualLines ?? Enumerable.Empty<ManualLine>())
            {
                manualIndex++;
                BoqLine line = BuildManualLine(manual, manualIndex, catalog, configuration, settings, result);
                Add(lotLines, line.LotCode, line);
            }

            // 4. Non classes : visibles, jamais valorises.
            List<BoqLine> ncLines = BuildUnclassifiedLines(unclassified, settings, result);
            foreach (BoqLine line in ncLines)
            {
                Add(lotLines, MappingCatalog.UnclassifiedLotCode, line);
            }

            // 5. Lots et totaux.
            IEnumerable<Lot> lots = catalog.Lots.Concat(new[]
            {
                new Lot { Code = MappingCatalog.UnclassifiedLotCode, Designation = "Non classés (aucune règle de mapping)" }
            });
            foreach (Lot lot in lots)
            {
                if (!lotLines.TryGetValue(lot.Code, out List<BoqLine> lines))
                {
                    continue;
                }
                var lotResult = new BoqLotResult { Code = lot.Code, Designation = lot.Designation };
                lotResult.Lines.AddRange(lines);
                lotResult.Total = lines.Where(l => l.Amount.HasValue).Sum(l => l.Amount.Value);
                lotResult.IsComplete = lines.All(l => l.Amount.HasValue);
                result.Lots.Add(lotResult);
            }

            foreach (KeyValuePair<string, List<string>> use in coefficientUse)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Warning, "COEF_A_VALIDER",
                    "Le coefficient " + use.Key + " est marqué « à valider » et s'applique aux articles " +
                    string.Join(", ", use.Value) + ". Valider sa valeur pour le projet."));
            }

            ComputeFooter(result, settings);
            ComputeReliability(result);

            if (result.Excluded.Count > 0)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Info, "ELEMENTS_EXCLUS",
                    result.Excluded.Count + " quantité(s) écartée(s) du métré (détail dans la liste des exclusions)."));
            }
            return result;
        }

        private static bool IsUsable(ElementQuantity q, BoqResult result)
        {
            if (double.IsNaN(q.Value) || double.IsInfinity(q.Value))
            {
                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "QUANTITE_INVALIDE",
                    "Quantité non numérique pour l'élément " + q.ElementId + " (" + q.Kind + ") : élément non compté."), q));
                return false;
            }
            if (q.Value < 0)
            {
                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "QUANTITE_NEGATIVE",
                    "Quantité négative (" + q.Value.ToString(CultureInfo.InvariantCulture) + ") pour l'élément " +
                    q.ElementId + " : élément non compté."), q));
                return false;
            }
            if (Math.Abs(q.Value) > 1e15)
            {
                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "QUANTITE_ABERRANTE",
                    "Quantité aberrante pour l'élément " + q.ElementId + " : élément non compté."), q));
                return false;
            }
            if (q.Reliability == Reliability.Estimated && string.IsNullOrWhiteSpace(q.Assumption))
            {
                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "ESTIMATION_SANS_HYPOTHESE",
                    "Quantité estimée sans hypothèse pour l'élément " + q.ElementId + " : une estimation doit dire sur quoi elle repose."), q));
            }
            return true;
        }

        private sealed class SplitKey
        {
            public string Code;
            public string Family;
            public string TypeName;
        }

        private static string SplitDesignation(Article article, SplitKey split, List<LineContribution> contributions)
        {
            string label = string.IsNullOrEmpty(split.Family) ? split.TypeName : split.Family + " : " + split.TypeName;
            var dims = contributions
                .Select(c => Dimension(c.Element, "LargeurMm") + " × " + Dimension(c.Element, "HauteurMm"))
                .Distinct()
                .ToList();
            string suffix = dims.Count == 1 && !dims[0].Contains("?") ? " — " + dims[0] + " mm"
                          : dims.Count > 1 ? " — dimensions variables" : string.Empty;
            return article.Designation + " — " + (string.IsNullOrEmpty(label) ? "type inconnu" : label) + suffix;
        }

        private static string Dimension(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) && !string.IsNullOrWhiteSpace(v) ? v : "?";

        private static BoqLine BuildMeasuredLine(
            Article article,
            List<LineContribution> contributions,
            SplitKey split,
            BoqConfiguration configuration,
            EstimateSettings settings,
            BoqResult result,
            Dictionary<string, List<string>> coefficientUse)
        {
            var line = new BoqLine
            {
                Code = split?.Code ?? article.Code,
                ArticleCode = article.Code,
                LotCode = article.Lot,
                Designation = split == null ? article.Designation : SplitDesignation(article, split, contributions),
                Description = article.Description,
                Unit = article.Unit,
                Reliability = Reliability.Measured
            };
            line.Contributions.AddRange(contributions);

            foreach (LineContribution c in contributions)
            {
                decimal value = (decimal)c.Element.Value;
                line.NetQuantity += value;
                if (c.Element.Reliability == Reliability.Measured)
                {
                    line.MeasuredNetQuantity += value;
                }
                else
                {
                    line.EstimatedNetQuantity += value;
                }
                line.Reliability = Reliabilities.Worst(line.Reliability, c.Element.Reliability);
            }

            IReadOnlyList<Coefficient> coefficients = configuration.Coefficients?.For(article.Code) ?? new Coefficient[0];
            line.Coefficients = coefficients.Select(c => new AppliedCoefficient
            {
                Code = c.Code,
                Designation = c.Designation,
                Value = c.Value,
                NeedsValidation = c.NeedsValidation
            }).ToArray();
            line.CoefficientFactor = coefficients.Aggregate(1m, (f, c) => f * (1m + c.Value));
            line.GrossQuantity = line.NetQuantity * line.CoefficientFactor;
            foreach (Coefficient c in coefficients.Where(c => c.NeedsValidation))
            {
                if (!coefficientUse.TryGetValue(c.Code, out List<string> codes))
                {
                    coefficientUse[c.Code] = codes = new List<string>();
                }
                if (!codes.Contains(article.Code))
                {
                    codes.Add(article.Code);
                }
            }

            decimal priced = settings.PricedQuantity == PricedQuantity.Brute ? line.GrossQuantity : line.NetQuantity;
            line.Quantity = Round(priced, settings.DecimalsFor(article.Unit));

            if (line.Reliability == Reliability.Missing)
            {
                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Error, "QUANTITE_MANQUANTE",
                    "L'article " + line.Code + " contient des quantités manquantes : sa quantité est incomplète.")
                { ArticleCode = line.Code }, contributions.Where(c => c.Element.Reliability == Reliability.Missing).Select(c => c.Element)));
            }

            PriceEntry price = split == null
                ? configuration.Prices?.Find(article.Code)
                : configuration.Prices?.Find(article.Code, split.Family, split.TypeName);
            if (price == null)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Warning, "PRIX_ABSENT",
                    "Aucun prix unitaire pour l'article " + line.Code + " « " + line.Designation + " » : montant laissé vide.")
                { ArticleCode = line.Code });
                return line;
            }

            ApplyPrice(line, price.UnitPrice, price.Currency, settings, result);
            line.PriceSource = price.Source;
            return line;
        }

        private static BoqLine BuildManualLine(
            ManualLine manual, int index, MappingCatalog catalog, BoqConfiguration configuration,
            EstimateSettings settings, BoqResult result)
        {
            string lotCode = manual.Lot;
            if (lotCode == null || catalog.Lots.All(l => l.Code != lotCode))
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Error, "FORFAIT_LOT_INCONNU",
                    "Forfait « " + manual.Designation + " » : lot « " + manual.Lot + " » inconnu, placé en non classés."));
                lotCode = MappingCatalog.UnclassifiedLotCode;
            }

            string code = string.IsNullOrWhiteSpace(manual.Code)
                ? lotCode + ".F" + index.ToString("D2", CultureInfo.InvariantCulture)
                : manual.Code;
            if (catalog.FindArticle(code) != null)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Error, "FORFAIT_CODE_PRIS",
                    "Forfait « " + manual.Designation + " » : le code " + code + " est déjà celui d'un article.") { ArticleCode = code });
            }

            var line = new BoqLine
            {
                Code = code,
                ArticleCode = code,
                LotCode = lotCode,
                Designation = manual.Designation,
                Description = manual.Justification,
                Unit = manual.Unit,
                NetQuantity = manual.Quantity,
                GrossQuantity = manual.Quantity,
                Quantity = Round(manual.Quantity, settings.DecimalsFor(manual.Unit)),
                Reliability = Reliability.Manual,
                IsManual = true
            };

            if (manual.Quantity < 0m)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Error, "FORFAIT_QUANTITE", "Forfait « " + manual.Designation + " » : quantité négative.") { ArticleCode = code });
            }

            if (manual.UnitPrice == null)
            {
                result.Issues.Add(new BoqIssue(IssueSeverity.Warning, "PRIX_ABSENT",
                    "Forfait « " + manual.Designation + " » sans prix : montant laissé vide.") { ArticleCode = code });
                return line;
            }
            ApplyPrice(line, manual.UnitPrice.Value, manual.Currency, settings, result);
            line.PriceSource = "Saisie manuelle";
            return line;
        }

        private static List<BoqLine> BuildUnclassifiedLines(List<ElementQuantity> unclassified, EstimateSettings settings, BoqResult result)
        {
            var lines = new List<BoqLine>();
            var groups = unclassified
                .GroupBy(q => new { q.Kind, q.Category, q.TypeName, q.Unit })
                .OrderBy(g => g.Key.Kind ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Category ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(g => g.Key.TypeName ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Unit);

            int index = 0;
            foreach (var g in groups)
            {
                index++;
                ElementQuantity first = g.First();
                var line = new BoqLine
                {
                    Code = MappingCatalog.UnclassifiedLotCode + "." + index.ToString("D3", CultureInfo.InvariantCulture),
                    ArticleCode = null,
                    LotCode = MappingCatalog.UnclassifiedLotCode,
                    Designation = "Non classé — " + (first.CategoryLabel ?? first.Category ?? "?") + " / " +
                                  (first.TypeName ?? "?") + " (" + (first.Kind ?? "?") + ")",
                    Unit = g.Key.Unit,
                    Reliability = Reliability.Measured,
                    IsUnclassified = true
                };
                foreach (ElementQuantity q in g)
                {
                    line.Contributions.Add(new LineContribution(q, null));
                    decimal v = (decimal)q.Value;
                    line.NetQuantity += v;
                    if (q.Reliability == Reliability.Measured) line.MeasuredNetQuantity += v; else line.EstimatedNetQuantity += v;
                    line.Reliability = Reliabilities.Worst(line.Reliability, q.Reliability);
                }
                line.GrossQuantity = line.NetQuantity;
                line.Quantity = Round(line.NetQuantity, settings.DecimalsFor(line.Unit));
                lines.Add(line);

                result.Issues.Add(WithElements(new BoqIssue(IssueSeverity.Warning, "NON_CLASSE",
                    g.Count() + " quantité(s) sans règle de mapping (" + line.Designation + ") : non valorisées.")
                { ArticleCode = line.Code }, g));
            }
            return lines;
        }

        private static void ApplyPrice(BoqLine line, decimal unitPrice, Currency currency, EstimateSettings settings, BoqResult result)
        {
            decimal pu = unitPrice;
            if (currency != settings.Currency)
            {
                if (settings.ExchangeRate == null || settings.ExchangeRate.CdfPerUsd <= 0m)
                {
                    result.Issues.Add(new BoqIssue(IssueSeverity.Error, "TAUX_ABSENT",
                        "Prix de " + line.Code + " en " + currency + ", devis en " + settings.Currency + ", sans taux de change : montant laissé vide.")
                    { ArticleCode = line.Code });
                    return;
                }
                pu = settings.ExchangeRate.Convert(unitPrice, currency, settings.Currency);
                line.OriginalUnitPrice = unitPrice;
                line.OriginalCurrency = currency;
            }
            line.UnitPrice = Round(pu, settings.AmountDecimals);
            line.Amount = Round(line.Quantity * line.UnitPrice.Value, settings.AmountDecimals);
        }

        private static void ComputeFooter(BoqResult result, EstimateSettings settings)
        {
            int d = settings.AmountDecimals;
            result.WorksTotal = result.Lots.Sum(l => l.Total);
            decimal running = result.WorksTotal;
            foreach (Markup mk in settings.Markups ?? new List<Markup>())
            {
                decimal baseAmount = mk.Base == MarkupBase.Travaux ? result.WorksTotal : running;
                decimal amount = Round(baseAmount * mk.Percent / 100m, d);
                result.Markups.Add(new MarkupLine
                {
                    Code = mk.Code,
                    Designation = mk.Designation,
                    Percent = mk.Percent,
                    Base = mk.Base,
                    BaseAmount = baseAmount,
                    Amount = amount
                });
                running += amount;
            }
            result.TotalExcludingVat = running;
            result.Vat = Round(running * settings.VatPercent / 100m, d);
            result.TotalIncludingVat = result.TotalExcludingVat + result.Vat;

            if (settings.SecondaryCurrency.HasValue && settings.SecondaryCurrency != settings.Currency && settings.ExchangeRate != null)
            {
                result.TotalIncludingVatSecondary = Round(
                    settings.ExchangeRate.Convert(result.TotalIncludingVat, settings.Currency, settings.SecondaryCurrency.Value), d);
            }

            result.IsComplete = result.Lots.All(l => l.IsComplete);
            if (!result.IsComplete)
            {
                int missing = result.AllLines.Count(l => !l.Amount.HasValue);
                result.Issues.Add(new BoqIssue(IssueSeverity.Warning, "TOTAUX_INCOMPLETS",
                    missing + " ligne(s) sans montant : les totaux sont des MINORANTS du coût réel."));
            }
        }

        private static void ComputeReliability(BoqResult result)
        {
            foreach (BoqLine line in result.AllLines.Where(l => l.Amount.HasValue))
            {
                if (line.IsManual)
                {
                    result.ManualAmount += line.Amount.Value;
                    continue;
                }
                decimal measuredShare = line.NetQuantity > 0m ? line.MeasuredNetQuantity / line.NetQuantity : 1m;
                decimal measured = line.Amount.Value * measuredShare;
                result.MeasuredAmount += measured;
                result.EstimatedAmount += line.Amount.Value - measured;
            }
            decimal metered = result.MeasuredAmount + result.EstimatedAmount;
            result.ReliabilityIndex = metered > 0m ? result.MeasuredAmount / metered : (decimal?)null;
        }

        /// <summary>Si un prix ou un forfait est dans une autre devise que le devis, un taux est obligatoire.</summary>
        private static void CheckCurrencies(BoqConfiguration configuration, List<BoqIssue> issues)
        {
            EstimateSettings s = configuration.Settings ?? new EstimateSettings();
            if (s.ExchangeRate != null || configuration.Prices == null)
            {
                return;
            }
            foreach (PriceEntry p in configuration.Prices.Prices.Where(p => p.Currency != s.Currency))
            {
                issues.Add(new BoqIssue(IssueSeverity.Error, "TAUX_ABSENT",
                    "Prix " + p.Code + " en " + p.Currency + " alors que le devis est en " + s.Currency +
                    " et qu'aucun taux de change n'est saisi.")
                { File = BoqConfiguration.PricesFileName, ArticleCode = p.Code });
            }
        }

        internal static decimal Round(decimal value, int decimals) =>
            Math.Round(value, decimals, MidpointRounding.AwayFromZero);

        private static void Add(Dictionary<string, List<BoqLine>> lotLines, string lot, BoqLine line)
        {
            if (!lotLines.TryGetValue(lot, out List<BoqLine> list))
            {
                lotLines[lot] = list = new List<BoqLine>();
            }
            list.Add(line);
        }

        private static BoqIssue WithElements(BoqIssue issue, ElementQuantity q)
        {
            issue.ElementIds.Add(q.ElementId);
            return issue;
        }

        private static BoqIssue WithElements(BoqIssue issue, IEnumerable<ElementQuantity> qs)
        {
            issue.ElementIds.AddRange(qs.Select(q => q.ElementId).Distinct());
            return issue;
        }
    }
}
