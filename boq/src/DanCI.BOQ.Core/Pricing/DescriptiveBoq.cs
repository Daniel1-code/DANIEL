using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Pricing
{
    /// <summary>Article du BOQ descriptif : designation, prescriptions, composition reelle du modele, mode de metre.</summary>
    public sealed class DescriptiveRow
    {
        public string Code { get; set; }
        public string LotCode { get; set; }
        public string LotDesignation { get; set; }
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }
        public decimal Quantity { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? Amount { get; set; }
        /// <summary>Prescriptions : description du catalogue, a defaut texte type selon la nature de l'ouvrage.</summary>
        public string Prescription { get; set; }
        /// <summary>Vrai si la prescription vient du catalogue (regles_mapping.json) ; faux = texte type.</summary>
        public bool PrescriptionFromCatalog { get; set; }
        /// <summary>Composition reelle (types Revit et nombres, materiaux, niveaux).</summary>
        public string Composition { get; set; }
        /// <summary>Mode de metre, coefficients, quantite valorisee, fiabilite.</summary>
        public string Measurement { get; set; }
        /// <summary>Quantites derivees (informatives).</summary>
        public string Derived { get; set; }
        public int ElementCount { get; set; }
    }

    /// <summary>
    /// BOQ DESCRIPTIF : chaque article du devis avec un texte de prescription et, a partir du modele
    /// Revit, sa composition reelle (familles et types avec leur nombre, materiaux, niveaux), son mode de
    /// metre et ses quantites derivees. Les textes types sont generiques (a completer dans le CCTP) et
    /// signales comme tels ; tout le reste est tire du metre.
    /// </summary>
    public static class DescriptiveBoq
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static List<DescriptiveRow> Build(BoqResult boq, MappingCatalog catalog = null, int maxTypes = 8)
        {
            var rows = new List<DescriptiveRow>();
            if (boq == null) return rows;
            foreach (BoqLotResult lot in boq.Lots)
            {
                foreach (BoqLine line in lot.Lines.Where(l => l.Quantity != 0m))
                {
                    string article = line.ArticleCode ?? line.Code;
                    string catalogText = !string.IsNullOrWhiteSpace(line.Description) ? line.Description
                        : catalog?.Articles.FirstOrDefault(a => a.Code == article)?.Description;
                    var elements = line.Contributions.Select(c => c.Element).ToList();
                    int count = elements.Select(ElementRef.Of).Distinct().Count();
                    rows.Add(new DescriptiveRow
                    {
                        Code = line.Code, LotCode = lot.Code, LotDesignation = lot.Designation, Designation = line.Designation, Unit = line.Unit,
                        Quantity = line.Quantity, UnitPrice = line.UnitPrice, Amount = line.Amount, ElementCount = count,
                        Prescription = string.IsNullOrWhiteSpace(catalogText) ? DefaultPrescription(lot.Code, line.Designation, line.Unit, line.IsManual) : catalogText.Trim(),
                        PrescriptionFromCatalog = !string.IsNullOrWhiteSpace(catalogText),
                        Composition = line.IsManual ? "Forfait saisi (pas d'éléments Revit)." : Composition(elements, maxTypes),
                        Measurement = Measurement(line, boq.PricedQuantity),
                        Derived = line.Derived.Count == 0 ? null : string.Join(" ; ", line.Derived.Select(d => d.Designation + " : " + d.Value.ToString("N2", Fr) + " " + d.Unit.Symbol()))
                    });
                }
            }
            return rows;
        }

        /// <summary>Types Revit (famille : type x nombre), materiaux et niveaux reellement presents.</summary>
        public static string Composition(IReadOnlyList<ElementQuantity> elements, int maxTypes = 8)
        {
            if (elements == null || elements.Count == 0) return "Aucun élément Revit.";
            var parts = new List<string>();
            var types = elements.GroupBy(e => PriceVariants.Label(e.Family, e.TypeName, null))
                .Select(g => (Name: g.Key, Count: g.Select(ElementRef.Of).Distinct().Count()))
                .OrderByDescending(t => t.Count).ThenBy(t => t.Name, StringComparer.CurrentCulture).ToList();
            string typeText = string.Join(", ", types.Take(maxTypes).Select(t => t.Name + " (" + t.Count + ")"));
            if (types.Count > maxTypes) typeText += " et " + (types.Count - maxTypes) + " autre(s) type(s)";
            parts.Add("Types du modèle : " + typeText + ".");
            var materials = elements.Select(e => e.Material).Where(m => !string.IsNullOrWhiteSpace(m)).Distinct().OrderBy(m => m, StringComparer.CurrentCulture).ToList();
            if (materials.Count > 0) parts.Add("Matériaux : " + string.Join(", ", materials.Take(6)) + (materials.Count > 6 ? "…" : string.Empty) + ".");
            var levels = elements.Select(e => e.Level).Where(l => !string.IsNullOrWhiteSpace(l)).Distinct().ToList();
            if (levels.Count > 0) parts.Add("Niveaux : " + string.Join(", ", levels.Take(8)) + (levels.Count > 8 ? "…" : string.Empty) + ".");
            return string.Join(" ", parts);
        }

        /// <summary>Mode de metre : unite, origine de la mesure, coefficients, quantite valorisee, fiabilite.</summary>
        public static string Measurement(BoqLine line, PricedQuantity priced)
        {
            string how;
            switch (line.Unit)
            {
                case BoqUnit.CubicMeter: how = "Volume (m³) mesuré sur la géométrie des éléments Revit"; break;
                case BoqUnit.SquareMeter: how = "Surface (m²) mesurée sur les éléments Revit"; break;
                case BoqUnit.LinearMeter: how = "Longueur (ml) mesurée sur les éléments Revit"; break;
                case BoqUnit.Kilogram: how = "Masse (kg) calculée à partir du modèle (ratio ou profils)"; break;
                case BoqUnit.Each: how = "Nombre d'unités (U) compté dans le modèle"; break;
                default: how = "Quantité (" + line.Unit.Symbol() + ") tirée du modèle"; break;
            }
            if (line.IsManual) how = "Forfait saisi dans l'extension";
            var s = new List<string> { how + "." };
            if (line.EstimatedNetQuantity > 0m && line.NetQuantity > 0m)
                s.Add("Dont " + (line.EstimatedNetQuantity / line.NetQuantity * 100m).ToString("0", Fr) + " % estimés (signalés comme tels).");
            if (line.Coefficients.Count > 0)
                s.Add("Coefficients : " + string.Join(", ", line.Coefficients.Select(c => c.Designation + " ×" + c.Value.ToString("0.###", Fr))) + ".");
            s.Add("Quantité valorisée : " + (priced == PricedQuantity.Nette ? "nette en place (pertes incluses dans le prix)" : "brute (coefficients appliqués)") + ".");
            s.Add("Fiabilité : " + ReliabilityText(line.Reliability) + ".");
            return string.Join(" ", s);
        }

        private static string ReliabilityText(Reliability r) =>
            r == Reliability.Measured ? "mesuré" : r == Reliability.Manual ? "forfait saisi" : r == Reliability.Estimated ? "estimé" : "manquant";

        /// <summary>
        /// Texte de prescription TYPE selon la nature de l'ouvrage (lot et mots de la designation), quand le
        /// catalogue n'en donne pas : formulation generique, a preciser dans le CCTP du projet.
        /// </summary>
        public static string DefaultPrescription(string lot, string designation, BoqUnit unit, bool manual = false)
        {
            string d = (designation ?? string.Empty).ToLowerInvariant().Replace('é', 'e').Replace('è', 'e').Replace('ê', 'e').Replace('ç', 'c').Replace('à', 'a');
            bool Has(params string[] k) => k.Any(x => d.Contains(x));
            string t;
            if (manual) t = "Prestation au forfait, selon le descriptif établi par l'entreprise.";
            else if (Has("demolition", "depose")) t = "Démolition ou dépose soignée, y compris tri, chargement, évacuation des gravats en décharge agréée et protection des ouvrages conservés.";
            else if (Has("fouille", "terrass", "deblai")) t = "Exécution de fouilles en terrain de toute nature, y compris dressement des parois et du fond, étaiement si nécessaire, épuisement des eaux et jet sur berge ou chargement.";
            else if (Has("remblai")) t = "Remblai en matériaux sélectionnés, mis en œuvre par couches successives compactées, y compris réglage.";
            else if (Has("evacuation")) t = "Chargement, transport et mise en décharge agréée des terres excédentaires, foisonnement compris.";
            else if (Has("herisson", "proprete")) t = "Fourniture et mise en œuvre conformes au CCTP, y compris réglage et compactage du support.";
            else if (Has("coffrage")) t = "Coffrage ordinaire ou soigné selon parement, y compris étaiement, huile de décoffrage, décoffrage, nettoyage et toutes sujétions.";
            else if (Has("armature", "acier", "ferraill", "treillis")) t = "Fourniture, façonnage et pose d'aciers pour béton armé (HA / treillis soudés) selon plans d'exécution, y compris ligatures, cales, recouvrements et chutes.";
            else if (Has("beton", "voile", "dalle", "poteau", "poutre", "semelle", "longrine", "chainage", "linteau")) t = "Fourniture et mise en œuvre de béton de la classe prescrite au CCTP, y compris vibration, cure, réservations et toutes sujétions de mise en œuvre (coffrage et armatures en articles séparés si indiqués).";
            else if (Has("charpente", "profil", "assemblage", "boulon", "metallique")) t = "Fourniture, fabrication, protection anticorrosion et montage de charpente métallique, y compris assemblages, boulonnerie et réglages.";
            else if (Lot(lot, "05") || Has("maconnerie", "bloc", "agglo", "brique", "parpaing")) t = "Maçonnerie en éléments du type prescrit, hourdés au mortier de ciment, y compris joints, chaînages d'angle, coupes et toutes sujétions.";
            else if (Lot(lot, "06") || Has("toiture", "couverture", "etanch", "tole", "tuile")) t = "Fourniture et pose de couverture / étanchéité selon CCTP, y compris fixations, relevés, accessoires et raccords.";
            else if (Lot(lot, "07") || Has("porte", "fenetre", "menuiserie", "chassis", "baie")) t = "Fourniture et pose de menuiserie du type indiqué, y compris pré-cadre ou scellement, quincaillerie, joints, réglage et protection.";
            else if (Has("enduit", "crepi")) t = "Enduit au mortier de ciment en deux ou trois couches, dressé et taloché, y compris baguettes d'angle et protection.";
            else if (Has("chape")) t = "Chape au mortier de ciment dressée à la règle, y compris joints de fractionnement.";
            else if (Has("carrel", "faience", "revetement", "plinthe")) t = "Fourniture et pose de revêtement du type prescrit, collé ou scellé, y compris joints, coupes, plinthes si indiquées et nettoyage.";
            else if (Has("peinture")) t = "Peinture en deux couches minimum sur fond préparé (rebouchage, ponçage, impression), y compris protections.";
            else if (Has("faux plafond", "plafond")) t = "Faux-plafond du type prescrit, y compris ossature, suspentes et finitions.";
            else if (Lot(lot, "09") || Has("plomb", "sanitaire", "tuyau", "canalisation", "evier", "lavabo", "wc")) t = "Fourniture et pose d'installations de plomberie-sanitaire, y compris raccordements, essais d'étanchéité et mise en service.";
            else if (Lot(lot, "10") || Has("electr", "cable", "luminaire", "prise", "tableau")) t = "Fourniture et pose d'installations électriques conformes aux normes en vigueur, y compris raccordements, repérage, essais et mise en service.";
            else if (Lot(lot, "12") || Has("ventil", "climat", "gaine")) t = "Fourniture et pose d'équipements de ventilation / climatisation, y compris raccordements, réglages et mise en service.";
            else if (Lot(lot, "11") || Has("voirie", "vrd", "assainissement", "regard")) t = "Travaux de VRD selon CCTP, y compris terrassements, lits de pose, remblais et raccordements.";
            else t = "Fourniture et mise en œuvre conformes au CCTP et aux règles de l'art, y compris toutes sujétions.";
            return t + " (Texte type à préciser dans le CCTP du projet.)";
        }

        private static bool Lot(string lot, string code) => string.Equals(lot, code, StringComparison.Ordinal);
    }
}
