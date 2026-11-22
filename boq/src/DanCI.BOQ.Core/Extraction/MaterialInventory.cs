using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Reglages de l'inventaire des materiaux et du signalement des elements generiques.</summary>
    public sealed class InventorySettings
    {
        [System.Text.Json.Serialization.JsonPropertyName("actif")] public bool Enabled { get; set; } = true;

        /// <summary>Noms de types, familles ou materiaux « generiques » (gabarit Revit non adapte au projet).</summary>
        [System.Text.Json.Serialization.JsonPropertyName("motifsGeneriques")] public PatternList GenericNames { get; set; } = new PatternList(new[]
        {
            "*generic*", "*générique*", "*generique*", "*par défaut*", "*par defaut*", "*default*"
        });

        /// <summary>Categories entierement « generiques » : chaque element est signale.</summary>
        [System.Text.Json.Serialization.JsonPropertyName("categoriesGeneriques")] public List<string> GenericCategories { get; set; } =
            new List<string> { "OST_GenericModel" };
    }

    /// <summary>Un materiau du modele : tous les elements qui le portent.</summary>
    public sealed class MaterialInventoryRow
    {
        public string Material { get; set; }
        public string Categories { get; set; }
        public string Levels { get; set; }
        public double AreaM2 { get; set; }
        public double VolumeM3 { get; set; }
        public int ElementCount { get; set; }
        public bool IsGeneric { get; set; }
        public List<ElementQuantity> Items { get; } = new List<ElementQuantity>();
    }

    /// <summary>Un element generique a verifier, et pourquoi.</summary>
    public sealed class GenericFinding
    {
        public ElementQuantity Element { get; set; }
        public string Code { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>
    /// Inventaire de TOUS les materiaux modelises (quantites « Materiau » de l'extraction : un
    /// enregistrement par element et par materiau, surface et volume calcules par Revit) et
    /// detection des elements generiques. Informatif : rien n'est ajoute au BOQ.
    /// </summary>
    public static class MaterialInventory
    {
        public const string Kind = "Materiau";
        public const string Area = "SurfaceM2";
        /// <summary>Element sans aucun materiau : volume de sa geometrie.</summary>
        public const string NoMaterial = "SansMateriau";
        public const string NoMaterialLabel = "(aucun matériau)";
        public const string ByCategoryLabel = "(matériau « par catégorie »)";

        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OST_Walls"] = "Murs", ["OST_Floors"] = "Sols", ["OST_Roofs"] = "Toits", ["OST_Ceilings"] = "Plafonds",
            ["OST_StructuralColumns"] = "Poteaux", ["OST_Columns"] = "Poteaux archi.", ["OST_StructuralFraming"] = "Poutres",
            ["OST_StructuralFoundation"] = "Fondations", ["OST_Stairs"] = "Escaliers", ["OST_StairsRailing"] = "Garde-corps",
            ["OST_Doors"] = "Portes", ["OST_Windows"] = "Fenêtres", ["OST_GenericModel"] = "Modèles génériques",
            ["OST_EdgeSlab"] = "Rives de dalle", ["OST_Furniture"] = "Mobilier", ["OST_PlumbingFixtures"] = "Sanitaires",
            ["OST_CurtainWallPanels"] = "Panneaux de mur-rideau", ["OST_CurtainWallMullions"] = "Meneaux", ["OST_Site"] = "Site"
        };

        public static string CategoryLabel(ElementQuantity q) =>
            q?.Category != null && Labels.TryGetValue(q.Category, out string l) ? l : q?.CategoryLabel ?? q?.Category ?? string.Empty;

        public static List<MaterialInventoryRow> Build(IEnumerable<ElementQuantity> records, InventorySettings settings = null)
        {
            settings = settings ?? new InventorySettings();
            return (records ?? Enumerable.Empty<ElementQuantity>())
                .Where(q => q.Kind == Kind)
                .GroupBy(q => string.IsNullOrEmpty(q.Material) ? NoMaterialLabel : q.Material)
                .Select(g =>
                {
                    var row = new MaterialInventoryRow
                    {
                        Material = g.Key,
                        Categories = string.Join(", ", g.Select(CategoryLabel).Distinct().OrderBy(c => c, StringComparer.Ordinal)),
                        Levels = string.Join(", ", g.Select(q => string.IsNullOrEmpty(q.Level) ? "—" : q.Level).Distinct().OrderBy(c => c, StringComparer.Ordinal)),
                        AreaM2 = g.Sum(q => Number(q, Area)),
                        VolumeM3 = g.Sum(q => q.Value),
                        ElementCount = g.Select(ElementRef.Of).Distinct().Count(),
                        IsGeneric = g.Key == NoMaterialLabel || g.Key == ByCategoryLabel || settings.GenericNames.IsMatch(g.Key)
                    };
                    row.Items.AddRange(g);
                    return row;
                })
                .OrderByDescending(r => r.VolumeM3)
                .ThenByDescending(r => r.AreaM2)
                .ThenBy(r => r.Material, StringComparer.Ordinal)
                .ToList();
        }

        /// <summary>
        /// Elements generiques : categorie generique, type ou famille generique, materiau generique,
        /// couche « par categorie », aucun materiau. Un element peut avoir plusieurs raisons.
        /// </summary>
        public static List<GenericFinding> FindGeneric(IEnumerable<ElementQuantity> records, InventorySettings settings = null)
        {
            settings = settings ?? new InventorySettings();
            var findings = new List<GenericFinding>();
            var seen = new HashSet<(ElementRef, string)>();
            void Add(ElementQuantity q, string code, string reason)
            {
                if (seen.Add((ElementRef.Of(q), code + reason))) findings.Add(new GenericFinding { Element = q, Code = code, Reason = reason });
            }
            foreach (ElementQuantity q in records ?? Enumerable.Empty<ElementQuantity>())
            {
                if (q.Category != null && settings.GenericCategories.Contains(q.Category))
                    Add(q, "MODELE_GENERIQUE", "Modèle générique : à remplacer par une famille de la bonne catégorie (poteau, équipement…) ou à classer à la main.");
                if (settings.GenericNames.IsMatch(q.TypeName) || settings.GenericNames.IsMatch(q.Family))
                    Add(q, "TYPE_GENERIQUE", "Type ou famille générique « " + (settings.GenericNames.IsMatch(q.TypeName) ? q.TypeName : q.Family) + " » : composition et matériaux du gabarit Revit, pas du projet.");
                if (q.Kind == Kind)
                {
                    if (q.Parameters != null && q.Parameters.ContainsKey(NoMaterial))
                        Add(q, "SANS_MATERIAU", "Aucun matériau : impossible de savoir de quoi l'élément est fait.");
                    else if (q.Material == ByCategoryLabel)
                        Add(q, "MATERIAU_PAR_CATEGORIE", "Matériau « par catégorie » (non défini dans le type ou la famille).");
                    else if (settings.GenericNames.IsMatch(q.Material))
                        Add(q, "MATERIAU_GENERIQUE", "Matériau générique « " + q.Material + " ».");
                }
            }
            return findings.OrderBy(f => f.Code, StringComparer.Ordinal).ThenBy(f => f.Element.Category, StringComparer.Ordinal)
                           .ThenBy(f => f.Element.TypeName, StringComparer.Ordinal).ThenBy(f => f.Element.ElementId).ToList();
        }

        /// <summary>Constats d'audit regroupes par raison et par type (un constat liste ses elements).</summary>
        public static List<BoqIssue> ToIssues(IEnumerable<GenericFinding> findings)
        {
            var issues = new List<BoqIssue>();
            foreach (var g in findings.GroupBy(f => (f.Code, f.Element.Category, f.Element.TypeName, f.Element.SourceDocument)))
            {
                GenericFinding first = g.First();
                var ids = g.Select(f => f.Element.ElementId).Distinct().ToList();
                var issue = new BoqIssue(IssueSeverity.Warning, "ELEMENT_GENERIQUE_" + g.Key.Code,
                    (string.IsNullOrEmpty(g.Key.SourceDocument) ? string.Empty : "[lien " + g.Key.SourceDocument + "] ") +
                    ids.Count + " élément(s) " + CategoryLabel(first.Element) + " — type « " + (first.Element.TypeName ?? "?") + " » : " + first.Reason)
                {
                    LinkInstanceId = ElementRef.Of(first.Element).LinkInstanceId
                };
                issue.ElementIds.AddRange(ids);
                issues.Add(issue);
            }
            return issues;
        }

        private static double Number(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) &&
            double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
    }
}
