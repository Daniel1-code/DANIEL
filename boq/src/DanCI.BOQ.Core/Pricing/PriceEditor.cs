using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>Prix general d'un article tel qu'il est saisi dans l'onglet « Paramètres du projet ».</summary>
    public sealed class PriceEdit
    {
        public string Code { get; set; }
        public string LotCode { get; set; }
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }

        /// <summary>Vide : l'article n'a pas de prix (la ligne restera signalee « sans prix »).</summary>
        public decimal? UnitPrice { get; set; }
        public Currency Currency { get; set; } = Currency.USD;
        public DateTime? Date { get; set; }
        public string Source { get; set; }

        /// <summary>Decomposition facultative : si l'une est saisie, leur somme doit egaler le PU.</summary>
        public decimal? Materials { get; set; }
        public decimal? Labour { get; set; }
        public decimal? Equipment { get; set; }

        /// <summary>Prix par type ou par valeur deja dans la bibliotheque (conserves tels quels).</summary>
        public int SpecificPrices { get; set; }

        /// <summary>
        /// Prix d'une VARIANTE du modele (bordereau dynamique) : noms exacts de la famille, du type
        /// et du materiau presents dans Revit. Tous vides = prix general de l'article.
        /// </summary>
        public string Family { get; set; }
        public string TypeName { get; set; }
        public string Material { get; set; }

        public bool IsVariant => !string.IsNullOrWhiteSpace(Family) || !string.IsNullOrWhiteSpace(TypeName) || !string.IsNullOrWhiteSpace(Material);

        /// <summary>Article ou variante REELLEMENT present dans le modele analyse (faux : article du catalogue seulement).</summary>
        public bool InModel { get; set; }

        /// <summary>Quantite du modele (affichage) ; null hors modele.</summary>
        public decimal? ModelQuantity { get; set; }

        /// <summary>Nombre d'elements Revit concernes (affichage).</summary>
        public int ElementCount { get; set; }

        /// <summary>Prix qui s'applique aujourd'hui a une variante sans prix propre (affichage, ex. « 45,00 — prix général de l'article »).</summary>
        public string Applied { get; set; }

        /// <summary>Cle : article + variante.</summary>
        public string Key => Code + "|" + (Family ?? string.Empty) + "|" + (TypeName ?? string.Empty) + "|" + (Material ?? string.Empty);
    }

    /// <summary>
    /// Saisie des prix dans l'extension. Seul le prix GENERAL de chaque article est edite ; les
    /// prix par type de porte, par diametre, etc. de la bibliotheque sont conserves sans changement.
    /// Rien n'est invente : un article sans PU saisi reste sans prix.
    /// </summary>
    public static class PriceEditor
    {
        /// <summary>Une ligne par article du catalogue (ordre du catalogue), avec le prix general existant.</summary>
        public static List<PriceEdit> Rows(MappingCatalog catalog, PriceLibrary library, Currency defaultCurrency = Currency.USD)
        {
            var rows = new List<PriceEdit>();
            if (catalog == null)
            {
                return rows;
            }
            List<PriceEntry> prices = library?.Prices ?? new List<PriceEntry>();
            foreach (Article a in catalog.Articles)
            {
                PriceEntry general = prices.FirstOrDefault(p => p.Code == a.Code && !p.IsTypeSpecific);
                rows.Add(new PriceEdit
                {
                    Code = a.Code,
                    LotCode = a.Lot,
                    Designation = a.Designation,
                    Unit = a.Unit,
                    UnitPrice = general?.UnitPrice,
                    Currency = general?.Currency ?? defaultCurrency,
                    Date = general?.Date,
                    Source = general?.Source,
                    Materials = general?.Breakdown?.Materials,
                    Labour = general?.Breakdown?.Labour,
                    Equipment = general?.Breakdown?.Equipment,
                    SpecificPrices = prices.Count(p => p.Code == a.Code && p.IsTypeSpecific)
                });
            }
            return rows;
        }

        /// <summary>
        /// Prix du PROJET, comme le bordereau des prix unitaires : les articles REELLEMENT presents dans
        /// le modele analyse, chacun suivi de ses variantes (famille / type / materiau Revit). Puis, marques
        /// « hors modele » (masques par defaut), les autres articles du catalogue, pour saisir un prix a l'avance.
        /// Une variante n'a un PU que si la bibliotheque a un prix pour CES noms exacts ; sinon elle suit le
        /// prix applique (general de l'article, ou par type), indique a part. Rien n'est invente.
        /// </summary>
        public static List<PriceEdit> RowsFromModel(BoqResult boq, MappingCatalog catalog, PriceLibrary library, Currency defaultCurrency = Currency.USD)
        {
            var rows = new List<PriceEdit>();
            var byKey = new Dictionary<string, PriceEdit>(StringComparer.Ordinal);
            List<PriceEntry> prices = library?.Prices ?? new List<PriceEntry>();
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            string cur = (boq?.Currency ?? defaultCurrency).ToString();
            foreach (BpuRow r in boq == null ? new List<BpuRow>() : UnitPriceSchedule.Build(boq, library, int.MaxValue))
            {
                string code = r.ArticleCode ?? r.Code;
                Article article = catalog?.Articles.FirstOrDefault(a => a.Code == code);
                PriceEdit e;
                if (!r.IsVariant)
                {
                    e = General(code, r.LotCode, article?.Designation ?? r.Designation, r.Unit, prices, defaultCurrency);
                }
                else
                {
                    e = new PriceEdit
                    {
                        Code = code, LotCode = r.LotCode, Unit = r.Unit, Currency = defaultCurrency,
                        Designation = "   – " + PriceVariants.Label(r.Family, r.TypeName, r.Material),
                        Family = NullIfEmpty(r.Family), TypeName = NullIfEmpty(r.TypeName), Material = NullIfEmpty(r.Material)
                    };
                    // Prix propre de la variante : entree de la bibliotheque avec EXACTEMENT ces noms.
                    string entryKey = ToEntry(e, allowEmpty: true).Key;
                    PriceEntry own = prices.FirstOrDefault(p => p.IsTypeSpecific && p.Key == entryKey);
                    if (own != null)
                    {
                        Fill(e, own);
                    }
                    else
                    {
                        e.Applied = r.UnitPrice.HasValue
                            ? r.UnitPrice.Value.ToString("N2", fr) + " " + cur + " — " + (r.HasOwnPrice ? "prix de la bibliothèque pour ce type / matériau" : "prix général de l'article")
                            : "À CHIFFRER";
                    }
                }
                e.InModel = true;
                if (byKey.TryGetValue(e.Key, out PriceEdit existing))
                {
                    // Article ventile en plusieurs lignes : une seule saisie, quantites cumulees.
                    existing.ModelQuantity = (existing.ModelQuantity ?? 0m) + r.Quantity;
                    existing.ElementCount += r.ElementCount;
                    continue;
                }
                e.ModelQuantity = r.Quantity;
                e.ElementCount = r.ElementCount;
                byKey[e.Key] = e;
                rows.Add(e);
            }
            // Articles du catalogue absents du modele : conserves (prix a l'avance), masques par defaut.
            foreach (PriceEdit e in Rows(catalog, library, defaultCurrency))
            {
                if (byKey.ContainsKey(e.Key)) continue;
                e.InModel = false;
                byKey[e.Key] = e;
                rows.Add(e);
            }
            return rows;
        }

        private static PriceEdit General(string code, string lot, string designation, BoqUnit unit, List<PriceEntry> prices, Currency defaultCurrency)
        {
            var e = new PriceEdit { Code = code, LotCode = lot, Designation = designation, Unit = unit, Currency = defaultCurrency };
            PriceEntry general = prices.FirstOrDefault(p => p.Code == code && !p.IsTypeSpecific);
            if (general != null) Fill(e, general);
            e.SpecificPrices = prices.Count(p => p.Code == code && p.IsTypeSpecific);
            return e;
        }

        private static void Fill(PriceEdit e, PriceEntry p)
        {
            e.UnitPrice = p.UnitPrice;
            e.Currency = p.Currency;
            e.Date = p.Date;
            e.Source = p.Source;
            e.Materials = p.Breakdown?.Materials;
            e.Labour = p.Breakdown?.Labour;
            e.Equipment = p.Breakdown?.Equipment;
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s;

        /// <summary>
        /// Nouvelle bibliotheque : l'existante (non modifiee), dont les prix generaux sont remplaces
        /// par la saisie. Erreurs (PU negatif, decomposition fausse) : null, avec les motifs.
        /// </summary>
        public static PriceLibrary Apply(PriceLibrary existing, IEnumerable<PriceEdit> edits, List<BoqIssue> issues)
        {
            var result = new PriceLibrary
            {
                SchemaVersion = existing?.SchemaVersion > 0 ? existing.SchemaVersion : 1,
                Name = existing?.Name ?? "Prix du projet",
                City = existing?.City,
                Client = existing?.Client,
                Date = DateTime.Today,
                Prices = new List<PriceEntry>()
            };
            var byCode = new Dictionary<string, PriceEdit>(StringComparer.Ordinal);
            int before = issues.Count;
            foreach (PriceEdit e in edits ?? Enumerable.Empty<PriceEdit>())
            {
                if (string.IsNullOrWhiteSpace(e?.Code))
                {
                    continue;
                }
                string label = "Article " + e.Code + (e.IsVariant ? " (" + DanCI.BOQ.Core.Model.PriceVariants.Label(e.Family, e.TypeName, e.Material) + ")" : string.Empty);
                if (byCode.ContainsKey(e.Key))
                {
                    issues.Add(Error("PRIX_DOUBLON", label + " : saisi deux fois.", e.Code));
                    continue;
                }
                byCode[e.Key] = e;
                if (!e.UnitPrice.HasValue)
                {
                    if (e.Materials.HasValue || e.Labour.HasValue || e.Equipment.HasValue)
                    {
                        issues.Add(Error("PRIX_DECOMPOSITION_SANS_PU", label + " : décomposition saisie sans prix unitaire.", e.Code));
                    }
                    continue;
                }
                if (e.UnitPrice.Value < 0m)
                {
                    issues.Add(Error("PRIX_NEGATIF", label + " : prix unitaire négatif.", e.Code));
                }
                if (e.Materials.HasValue || e.Labour.HasValue || e.Equipment.HasValue)
                {
                    decimal sum = (e.Materials ?? 0m) + (e.Labour ?? 0m) + (e.Equipment ?? 0m);
                    if (sum != e.UnitPrice.Value)
                    {
                        issues.Add(Error("PRIX_DECOMPOSITION",
                            label + " : matériaux + main-d'œuvre + matériel = " + sum.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR")) +
                            ", différent du PU " + e.UnitPrice.Value.ToString("0.####", CultureInfo.GetCultureInfo("fr-FR")) + ".", e.Code));
                    }
                }
            }
            if (issues.Count > before)
            {
                return null;
            }

            // Ordre conserve : les entrees existantes a leur place, les nouveaux prix generaux a la fin.
            // Prix de variante : remplace l'entree de la bibliotheque qui a EXACTEMENT ces criteres ; PU vide = entree retiree.
            var written = new HashSet<string>(StringComparer.Ordinal);
            var byEntryKey = byCode.Values.ToDictionary(e => ToEntry(e, allowEmpty: true).Key, e => e, StringComparer.Ordinal);
            foreach (PriceEntry p in existing?.Prices ?? new List<PriceEntry>())
            {
                PriceEdit edit;
                bool replaced = p.IsTypeSpecific
                    ? byEntryKey.TryGetValue(p.Key, out edit)
                    : byCode.TryGetValue(p.Code + "|||", out edit);
                if (!replaced)
                {
                    result.Prices.Add(p);
                    continue;
                }
                if (edit.UnitPrice.HasValue && written.Add(edit.Key))
                {
                    result.Prices.Add(ToEntry(edit));
                }
                else
                {
                    written.Add(edit.Key); // PU efface : entree retiree
                }
            }
            foreach (PriceEdit edit in byCode.Values)
            {
                if (edit.UnitPrice.HasValue && written.Add(edit.Key))
                {
                    result.Prices.Add(ToEntry(edit));
                }
            }
            return result;
        }

        /// <summary>Prix general saisi -> entree de bibliotheque (date du jour et source par defaut).</summary>
        public static PriceEntry ToEntry(PriceEdit e) => ToEntry(e, false);

        private static PatternList Exact(string name) =>
            string.IsNullOrWhiteSpace(name) ? null : new PatternList(new[] { name.Trim().Replace("*", "?") });

        private static PriceEntry ToEntry(PriceEdit e, bool allowEmpty) => new PriceEntry
        {
            Code = e.Code,
            Designation = e.Designation,
            Unit = e.Unit,
            UnitPrice = allowEmpty ? e.UnitPrice ?? 0m : e.UnitPrice.Value,
            Family = Exact(e.Family),
            TypeName = Exact(e.TypeName),
            Material = Exact(e.Material),
            Currency = e.Currency,
            Date = e.Date ?? DateTime.Today,
            Source = string.IsNullOrWhiteSpace(e.Source) ? "Saisie DanCI — Paramètres du projet" : e.Source.Trim(),
            Breakdown = e.Materials.HasValue || e.Labour.HasValue || e.Equipment.HasValue
                ? new PriceBreakdown { Materials = e.Materials ?? 0m, Labour = e.Labour ?? 0m, Equipment = e.Equipment ?? 0m }
                : null
        };

        private static BoqIssue Error(string code, string message, string article) =>
            new BoqIssue(IssueSeverity.Error, code, message) { File = DanCI.BOQ.Core.Configuration.BoqConfiguration.PricesFileName, ArticleCode = article };
    }
}
