using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Configuration
{
    /// <summary>
    /// Verifie la coherence des quatre fichiers. Une ERREUR bloque le moteur ; un
    /// AVERTISSEMENT laisse produire le BOQ mais figure au rapport.
    /// </summary>
    public static class ConfigurationValidator
    {
        public static List<BoqIssue> Validate(BoqConfiguration config)
        {
            var issues = new List<BoqIssue>();
            if (config?.Mapping == null)
            {
                issues.Add(Error(BoqConfiguration.MappingFileName, "MAPPING_ABSENT", "Aucune règle de mapping chargée."));
                return issues;
            }
            ValidateMapping(config.Mapping, issues);
            ValidateCoefficients(config.Coefficients, config.Mapping, issues);
            ValidatePrices(config.Prices, config.Mapping, issues);
            ValidateSettings(config.Settings, issues);
            ValidateExtraction(config.Extraction, issues);
            ValidateSteelProfiles(config.SteelProfiles, issues);
            foreach (PriceEntry p in (config.Prices?.Prices ?? new List<PriceEntry>()).Where(p => p.IsTypeSpecific))
            {
                Article a = config.Mapping.FindArticle(p.Code);
                if (a != null && a.SplitBy != ArticleSplit.Type)
                {
                    issues.Add(Warning(BoqConfiguration.PricesFileName, "PRIX_TYPE_INUTILE",
                        "Prix " + p.Code + " avec critère de type, mais l'article n'est pas ventilé par type : le critère est ignoré.", p.Code));
                }
            }
            return issues;
        }

        private static void ValidateMapping(MappingCatalog m, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.MappingFileName;
            CheckSchema(m.SchemaVersion, file, issues);

            var lotCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (Lot lot in m.Lots)
            {
                if (string.IsNullOrWhiteSpace(lot.Code) || string.IsNullOrWhiteSpace(lot.Designation))
                {
                    issues.Add(Error(file, "LOT_INCOMPLET", "Lot sans code ou sans désignation."));
                }
                else if (lot.Code == MappingCatalog.UnclassifiedLotCode)
                {
                    issues.Add(Error(file, "LOT_RESERVE", "Le code de lot « NC » est réservé aux non classés."));
                }
                else if (!lotCodes.Add(lot.Code))
                {
                    issues.Add(Error(file, "LOT_DOUBLON", "Code de lot en double : " + lot.Code));
                }
            }

            var articleCodes = new HashSet<string>(StringComparer.Ordinal);
            foreach (Article a in m.Articles)
            {
                if (string.IsNullOrWhiteSpace(a.Code) || string.IsNullOrWhiteSpace(a.Designation))
                {
                    issues.Add(Error(file, "ARTICLE_INCOMPLET", "Article sans code ou sans désignation."));
                    continue;
                }
                if (!articleCodes.Add(a.Code))
                {
                    issues.Add(Error(file, "ARTICLE_DOUBLON", "Code d'article en double : " + a.Code, a.Code));
                }
                if (a.Code.StartsWith(MappingCatalog.UnclassifiedLotCode + ".", StringComparison.Ordinal))
                {
                    issues.Add(Error(file, "ARTICLE_RESERVE", "Les codes « NC.* » sont réservés aux non classés.", a.Code));
                }
                foreach (DerivedQuantityRule d in a.Derived ?? new List<DerivedQuantityRule>())
                {
                    if (d.PerUnit <= 0m || string.IsNullOrWhiteSpace(d.Designation))
                    {
                        issues.Add(Error(file, "DERIVEE_INVALIDE", "Article " + a.Code + " : quantité dérivée sans désignation ou avec « parUnite » ≤ 0.", a.Code));
                    }
                }
                if (a.Lot == null || !lotCodes.Contains(a.Lot))
                {
                    issues.Add(Error(file, "ARTICLE_SANS_LOT", "L'article " + a.Code + " renvoie au lot inconnu « " + a.Lot + " ».", a.Code));
                }
            }

            var ruleIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (IGrouping<int, MappingRule> same in m.Rules.GroupBy(r => r.Order).Where(g => g.Count() > 1))
            {
                issues.Add(Error(file, "ORDRE_AMBIGU",
                    "Plusieurs règles ont l'ordre " + same.Key + " (" + string.Join(", ", same.Select(r => r.Id)) +
                    ") : l'ordre doit être explicite et unique, sinon « la première qui correspond » n'a pas de sens."));
            }

            foreach (MappingRule r in m.Rules)
            {
                string label = "Règle " + (r.Id ?? "(sans id)");
                if (string.IsNullOrWhiteSpace(r.Id))
                {
                    issues.Add(Error(file, "REGLE_SANS_ID", "Règle d'ordre " + r.Order + " sans identifiant."));
                }
                else if (!ruleIds.Add(r.Id))
                {
                    issues.Add(Error(file, "REGLE_DOUBLON", "Identifiant de règle en double : " + r.Id));
                }

                if (r.When == null || r.When.IsEmpty)
                {
                    issues.Add(Error(file, "REGLE_SANS_CRITERE",
                        label + " : aucun critère. Une règle sans critère correspondrait à tous les éléments."));
                }
                else if (r.When.Kind != null && !r.When.Kind.IsEmpty)
                {
                    foreach (TextPattern p in r.When.Kind.Items.Where(p => !p.Pattern.Contains("*") && !p.Pattern.Contains("?")))
                    {
                        if (!QuantityKinds.All.Contains(p.Pattern, StringComparer.OrdinalIgnoreCase))
                        {
                            issues.Add(Warning(file, "NATURE_INCONNUE",
                                label + " : nature « " + p.Pattern + " » non produite par l'extraction (" +
                                string.Join(", ", QuantityKinds.All) + ")."));
                        }
                    }
                }

                if (r.Exclude)
                {
                    if (!string.IsNullOrWhiteSpace(r.Article))
                    {
                        issues.Add(Error(file, "REGLE_CONTRADICTOIRE", label + " : « exclure » et « article » à la fois."));
                    }
                    if (string.IsNullOrWhiteSpace(r.Reason))
                    {
                        issues.Add(Error(file, "EXCLUSION_SANS_MOTIF", label + " : une exclusion doit donner son motif."));
                    }
                }
                else if (string.IsNullOrWhiteSpace(r.Article))
                {
                    issues.Add(Error(file, "REGLE_SANS_ARTICLE", label + " : ni article cible, ni exclusion."));
                }
                else if (!articleCodes.Contains(r.Article))
                {
                    issues.Add(Error(file, "REGLE_ARTICLE_INCONNU", label + " : article inconnu « " + r.Article + " ».", r.Article));
                }
            }
        }

        private static void ValidateCoefficients(CoefficientSet set, MappingCatalog m, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.CoefficientsFileName;
            if (set == null)
            {
                return;
            }
            CheckSchema(set.SchemaVersion, file, issues);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (Coefficient c in set.Items)
            {
                string label = "Coefficient " + (c.Code ?? "(sans code)");
                if (string.IsNullOrWhiteSpace(c.Code) || !codes.Add(c.Code))
                {
                    issues.Add(Error(file, "COEF_CODE", label + " : code absent, ou en double pour les mêmes critères de type."));
                }
                if (c.Value < 0m)
                {
                    issues.Add(Error(file, "COEF_NEGATIF", label + " : valeur négative (" + c.Value + ")."));
                }
                else if (c.Value > 1m)
                {
                    issues.Add(Warning(file, "COEF_ELEVE",
                        label + " : majoration de " + (c.Value * 100m) + " %. Vérifier qu'il ne s'agit pas d'un pourcentage saisi en entier (0,05 = 5 %)."));
                }
                if (c.AppliesTo == null || c.AppliesTo.IsEmpty)
                {
                    issues.Add(Error(file, "COEF_SANS_CIBLE", label + " : « appliqueA » vide."));
                }
                else if (!m.Articles.Any(a => c.AppliesToArticle(a.Code)))
                {
                    issues.Add(Warning(file, "COEF_INUTILISE", label + " : ne s'applique à aucun article (" + c.AppliesTo + ")."));
                }
            }
        }

        private static void ValidatePrices(PriceLibrary lib, MappingCatalog m, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.PricesFileName;
            if (lib == null)
            {
                return;
            }
            CheckSchema(lib.SchemaVersion, file, issues);
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (PriceEntry p in lib.Prices)
            {
                string label = "Prix " + (p.Code ?? "(sans code)");
                if (string.IsNullOrWhiteSpace(p.Code) || !codes.Add(p.Key))
                {
                    issues.Add(Error(file, "PRIX_CODE", label + " : code absent, ou en double pour les mêmes critères de type.", p.Code));
                    continue;
                }
                if (p.UnitPrice < 0m)
                {
                    issues.Add(Error(file, "PRIX_NEGATIF", label + " : prix unitaire négatif.", p.Code));
                }
                if (p.Date == null)
                {
                    issues.Add(Warning(file, "PRIX_NON_DATE", label + " : prix sans date. Un prix non daté ne se défend pas.", p.Code));
                }
                if (string.IsNullOrWhiteSpace(p.Source))
                {
                    issues.Add(Warning(file, "PRIX_SANS_SOURCE", label + " : source du prix non renseignée.", p.Code));
                }
                if (p.Breakdown != null && Math.Abs(p.Breakdown.Total - p.UnitPrice) > 0.01m)
                {
                    issues.Add(Warning(file, "PRIX_DECOMPOSITION",
                        label + " : décomposition (" + p.Breakdown.Total + ") différente du PU (" + p.UnitPrice + ").", p.Code));
                }
                Article article = m.FindArticle(p.Code);
                if (article == null)
                {
                    issues.Add(Warning(file, "PRIX_ARTICLE_INCONNU", label + " : aucun article de ce code dans les règles.", p.Code));
                }
                else if (article.Unit != p.Unit)
                {
                    issues.Add(Error(file, "PRIX_UNITE",
                        label + " : prix en " + p.Unit.Symbol() + ", article en " + article.Unit.Symbol() + ".", p.Code));
                }
            }
        }

        private static void ValidateSettings(EstimateSettings s, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.SettingsFileName;
            if (s == null)
            {
                return;
            }
            CheckSchema(s.SchemaVersion, file, issues);
            if (s.VatPercent < 0m || s.VatPercent > 100m)
            {
                issues.Add(Error(file, "TVA_INVALIDE", "Taux de TVA hors de [0 ; 100] : " + s.VatPercent));
            }
            foreach (Markup mk in s.Markups ?? new List<Markup>())
            {
                if (mk.Percent < 0m)
                {
                    issues.Add(Error(file, "MAJORATION_NEGATIVE", "Majoration « " + mk.Designation + " » négative."));
                }
                if (string.IsNullOrWhiteSpace(mk.Code) || string.IsNullOrWhiteSpace(mk.Designation))
                {
                    issues.Add(Error(file, "MAJORATION_INCOMPLETE", "Majoration sans code ou sans désignation."));
                }
            }
            if (s.ExchangeRate != null)
            {
                if (s.ExchangeRate.CdfPerUsd <= 0m)
                {
                    issues.Add(Error(file, "TAUX_INVALIDE", "Taux de change USD/CDF nul ou négatif."));
                }
                if (s.ExchangeRate.Date == null)
                {
                    issues.Add(Error(file, "TAUX_NON_DATE", "Taux de change sans date : il doit être daté pour être opposable."));
                }
            }
            if (s.SecondaryCurrency.HasValue && s.SecondaryCurrency != s.Currency && s.ExchangeRate == null)
            {
                issues.Add(Error(file, "TAUX_ABSENT", "Devise secondaire demandée sans taux de change."));
            }
            if (s.QuantityDecimals < 0 || s.QuantityDecimals > 6 || s.AmountDecimals < 0 || s.AmountDecimals > 4)
            {
                issues.Add(Error(file, "DECIMALES", "Nombre de décimales hors limites (quantités 0–6, montants 0–4)."));
            }
        }

        private static void ValidateExtraction(DanCI.BOQ.Core.Extraction.ExtractionSettings e, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.ExtractionFileName;
            if (e == null)
            {
                return;
            }
            CheckSchema(e.SchemaVersion, file, issues);
            if (e.OpeningDeductionThresholdM2 < 0m)
            {
                issues.Add(Error(file, "SEUIL_NEGATIF", "Seuil de déduction des ouvertures négatif."));
            }
            foreach (DanCI.BOQ.Core.Extraction.RebarRatio r in e.RebarRatios ?? new List<DanCI.BOQ.Core.Extraction.RebarRatio>())
            {
                string label = "Ratio d'armatures " + (r.Designation ?? r.Category?.ToString() ?? "(sans catégorie)");
                if (r.Category == null || r.Category.IsEmpty)
                {
                    issues.Add(Error(file, "RATIO_SANS_CATEGORIE", label + " : « categorie » vide."));
                }
                if (r.KgPerM3 <= 0m)
                {
                    issues.Add(Error(file, "RATIO_INVALIDE", label + " : ratio nul ou négatif."));
                }
                else if (r.KgPerM3 > 400m)
                {
                    issues.Add(Warning(file, "RATIO_ELEVE", label + " : " + r.KgPerM3 + " kg/m³, valeur inhabituelle (> 400)."));
                }
            }
            if (e.Steel != null)
            {
                if (e.Steel.DensityKgPerM3 < 7000m || e.Steel.DensityKgPerM3 > 8100m)
                {
                    issues.Add(Warning(file, "ACIER_DENSITE", "Masse volumique de l'acier inhabituelle : " + e.Steel.DensityKgPerM3 + " kg/m³."));
                }
                if (e.Steel.ConnectionsShare < 0m || e.Steel.ConnectionsShare > 0.5m)
                {
                    issues.Add(Error(file, "ASSEMBLAGES_PART", "Part des assemblages hors de [0 ; 0,5] : " + e.Steel.ConnectionsShare + " (0,10 = 10 %)."));
                }
            }
            if (e.Formwork != null && (e.Formwork.MaxUncoveredTopSlopeDeg < 0 || e.Formwork.MaxUncoveredTopSlopeDeg > 89))
            {
                issues.Add(Error(file, "COFFRAGE_PENTE", "Pente des dessus non coffrés hors de [0 ; 89]°."));
            }
            if (e.ConcreteMaterialNames == null || e.ConcreteMaterialNames.IsEmpty)
            {
                issues.Add(Warning(file, "BETON_SANS_MOTIF",
                    "Aucun motif de nom de matériau béton : seuls les matériaux à actif structurel « Béton » seront reconnus."));
            }
        }

        private static void ValidateSteelProfiles(DanCI.BOQ.Core.Extraction.SteelProfileTable t, List<BoqIssue> issues)
        {
            const string file = BoqConfiguration.SteelProfilesFileName;
            if (t == null)
            {
                return;
            }
            CheckSchema(t.SchemaVersion, file, issues);
            foreach (KeyValuePair<string, decimal> p in t.KgPerMeter ?? new Dictionary<string, decimal>())
            {
                if (p.Value <= 0m)
                {
                    issues.Add(Error(file, "PROFIL_MASSE", "Profil " + p.Key + " : masse linéique nulle ou négative."));
                }
                if (DanCI.BOQ.Core.Extraction.SteelProfileName.Normalize(p.Key) != p.Key)
                {
                    issues.Add(Error(file, "PROFIL_NOM",
                        "Profil « " + p.Key + " » : la clé doit être une désignation normalisée (ex. IPE200, HEA200, HEB200, UPN200), sinon elle ne sera jamais trouvée."));
                }
            }
        }

        private static void CheckSchema(int version, string file, List<BoqIssue> issues)
        {
            if (version != BoqConfiguration.SupportedSchemaVersion)
            {
                issues.Add(Error(file, "SCHEMA_VERSION",
                    "schemaVersion = " + version + ", version prise en charge : " + BoqConfiguration.SupportedSchemaVersion + "."));
            }
        }

        private static BoqIssue Error(string file, string code, string message, string articleCode = null) =>
            new BoqIssue(IssueSeverity.Error, code, message) { File = file, ArticleCode = articleCode };

        private static BoqIssue Warning(string file, string code, string message, string articleCode = null) =>
            new BoqIssue(IssueSeverity.Warning, code, message) { File = file, ArticleCode = articleCode };
    }
}
