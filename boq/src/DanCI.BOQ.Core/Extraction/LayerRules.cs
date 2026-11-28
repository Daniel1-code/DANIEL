using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Fonctions des couches Revit (MaterialFunctionAssignment), en francais.</summary>
    public static class LayerFunctions
    {
        public const string Parameter = "Fonction";
        public const string Structure = "Structure";
        public const string Substrate = "Substrat";
        public const string Insulation = "Isolation";
        public const string Finish1 = "Finition 1";
        public const string Finish2 = "Finition 2";
        public const string Membrane = "Membrane";
        public const string Air = "Lame d'air";
        public const string Other = "Autre";

        /// <summary>Nom de l'enumeration Revit (Structure, Substrate, Insulation, Finish1, Finish2, Membrane, StructuralDeck, ThermalOrAir) vers le libelle.</summary>
        public static string FromRevit(string revitName)
        {
            switch (revitName)
            {
                case "Structure": return Structure;
                case "StructuralDeck": return Structure;
                case "Substrate": return Substrate;
                case "Insulation": return Insulation;
                case "Finish1": return Finish1;
                case "Finish2": return Finish2;
                case "Membrane": return Membrane;
                case "ThermalOrAir": return Air;
                default: return Other;
            }
        }

        /// <summary>Ordre d'affichage (porteur d'abord).</summary>
        public static int Order(string label)
        {
            switch (label)
            {
                case Structure: return 0;
                case Substrate: return 1;
                case Insulation: return 2;
                case Membrane: return 3;
                case Air: return 4;
                case Finish1: return 5;
                case Finish2: return 6;
                default: return 7;
            }
        }
    }

    /// <summary>Une couche d'un type composite (lue sur la structure du type).</summary>
    public sealed class LayerDefinition
    {
        public int Index { get; set; }
        public string Function { get; set; }
        /// <summary>Cle du materiau (ElementId en texte) ; vide = « par categorie ».</summary>
        public string MaterialKey { get; set; }
        public double ThicknessMm { get; set; }
    }

    /// <summary>Quantites d'une couche apres repartition.</summary>
    public sealed class LayerShare
    {
        public LayerDefinition Layer { get; set; }
        public double AreaM2 { get; set; }
        public double VolumeM3 { get; set; }
        /// <summary>Vrai si le materiau est present dans plusieurs couches : repartition calculee.</summary>
        public bool Split { get; set; }
        /// <summary>Vrai si la surface a ete deduite du volume (surface Revit nulle).</summary>
        public bool AreaFromVolume { get; set; }
    }

    /// <summary>
    /// Repartit les quantites Revit d'un materiau (GetMaterialArea / GetMaterialVolume, par element)
    /// entre ses couches. Un materiau dans une seule couche : quantites telles quelles. Dans
    /// plusieurs couches : volume au prorata des epaisseurs, surface a parts egales (hypothese).
    /// </summary>
    public static class LayerSplit
    {
        public static List<LayerShare> Apply(IReadOnlyList<LayerDefinition> layers, double materialAreaM2, double materialVolumeM3)
        {
            var result = new List<LayerShare>();
            if (layers == null || layers.Count == 0) return result;
            double totalThickness = layers.Sum(l => Math.Max(0, l.ThicknessMm));
            bool split = layers.Count > 1;
            foreach (LayerDefinition l in layers)
            {
                double volume = split
                    ? (totalThickness > 0 ? materialVolumeM3 * Math.Max(0, l.ThicknessMm) / totalThickness : materialVolumeM3 / layers.Count)
                    : materialVolumeM3;
                double area = split ? materialAreaM2 / layers.Count : materialAreaM2;
                bool fromVolume = false;
                if (area <= 1e-9 && volume > 1e-9 && l.ThicknessMm > 0)
                {
                    area = volume / (l.ThicknessMm / 1000.0);
                    fromVolume = true;
                }
                result.Add(new LayerShare { Layer = l, AreaM2 = area, VolumeM3 = volume, Split = split, AreaFromVolume = fromVolume });
            }
            return result;
        }
    }

    /// <summary>Une ligne du recapitulatif des couches : un materiau dans une fonction.</summary>
    public sealed class LayerSummaryRow
    {
        public string Material { get; set; }
        public string Function { get; set; }
        public string Categories { get; set; }
        public string Thicknesses { get; set; }
        public double AreaM2 { get; set; }
        public double VolumeM3 { get; set; }
        public int ElementCount { get; set; }
        public int LayerCount { get; set; }
        public Reliability Reliability { get; set; }
        public List<ElementQuantity> Items { get; } = new List<ElementQuantity>();
    }

    /// <summary>
    /// Recapitulatif « matériaux par couche » : surfaces (m²) et volumes (m³) cumules par materiau
    /// et par fonction de couche, a partir des quantites « Couche » de l'extraction. Informatif :
    /// ces quantites ne sont pas ajoutees au BOQ (le beton, la maconnerie et les finitions y sont
    /// deja, metres par leurs propres methodes).
    /// </summary>
    public static class LayerSummary
    {
        public const string Thickness = "EpaisseurMm";
        public const string Area = "SurfaceM2";
        public const string Index = "CoucheIndex";

        private static readonly Dictionary<string, string> CategoryLabels = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["OST_Walls"] = "Murs", ["OST_Floors"] = "Sols", ["OST_Roofs"] = "Toits", ["OST_Ceilings"] = "Plafonds"
        };

        public static string CategoryLabel(string category) =>
            category != null && CategoryLabels.TryGetValue(category, out string l) ? l : category ?? string.Empty;

        public static List<LayerSummaryRow> Build(IEnumerable<ElementQuantity> layers)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return (layers ?? Enumerable.Empty<ElementQuantity>())
                .Where(q => q.Kind == QuantityKinds.Layer)
                .GroupBy(q => (Material: string.IsNullOrEmpty(q.Material) ? "(matériau non défini)" : q.Material, Function: Get(q, LayerFunctions.Parameter) ?? LayerFunctions.Other))
                .Select(g =>
                {
                    var row = new LayerSummaryRow
                    {
                        Material = g.Key.Material,
                        Function = g.Key.Function,
                        Categories = string.Join(", ", g.Select(q => CategoryLabel(q.Category)).Distinct().OrderBy(c => c, StringComparer.Ordinal)),
                        Thicknesses = string.Join(" / ", g.Select(q => Number(Get(q, Thickness))).Where(t => t > 0).Distinct().OrderBy(t => t)
                                         .Select(t => t.ToString("0.#", fr))) is string t && t.Length > 0 ? t + " mm" : "—",
                        AreaM2 = g.Sum(q => Number(Get(q, Area))),
                        VolumeM3 = g.Sum(q => q.Value),
                        ElementCount = g.Select(q => ElementRef.Of(q)).Distinct().Count(),
                        LayerCount = g.Count(),
                        Reliability = g.Max(q => q.Reliability)
                    };
                    row.Items.AddRange(g);
                    return row;
                })
                .OrderBy(r => LayerFunctions.Order(r.Function))
                .ThenByDescending(r => r.VolumeM3)
                .ThenByDescending(r => r.AreaM2)
                .ToList();
        }

        private static string Get(ElementQuantity q, string key) =>
            q.Parameters != null && q.Parameters.TryGetValue(key, out string v) ? v : null;

        private static double Number(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0;
    }
}
