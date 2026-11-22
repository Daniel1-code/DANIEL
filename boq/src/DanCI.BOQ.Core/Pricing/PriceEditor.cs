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
                string label = "Article " + e.Code;
                if (byCode.ContainsKey(e.Code))
                {
                    issues.Add(Error("PRIX_DOUBLON", label + " : saisi deux fois.", e.Code));
                    continue;
                }
                byCode[e.Code] = e;
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
            var written = new HashSet<string>(StringComparer.Ordinal);
            foreach (PriceEntry p in existing?.Prices ?? new List<PriceEntry>())
            {
                if (p.IsTypeSpecific || !byCode.TryGetValue(p.Code ?? string.Empty, out PriceEdit edit))
                {
                    result.Prices.Add(p);
                    continue;
                }
                if (edit.UnitPrice.HasValue && written.Add(edit.Code))
                {
                    result.Prices.Add(ToEntry(edit));
                }
            }
            foreach (PriceEdit edit in byCode.Values)
            {
                if (edit.UnitPrice.HasValue && written.Add(edit.Code))
                {
                    result.Prices.Add(ToEntry(edit));
                }
            }
            return result;
        }

        /// <summary>Prix general saisi -> entree de bibliotheque (date du jour et source par defaut).</summary>
        public static PriceEntry ToEntry(PriceEdit e) => new PriceEntry
        {
            Code = e.Code,
            Designation = e.Designation,
            Unit = e.Unit,
            UnitPrice = e.UnitPrice.Value,
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
