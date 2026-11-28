using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Dashboard
{
    public enum ColoringMode
    {
        /// <summary>Une couleur par lot (celle du BOQ, du PDF et d'Excel).</summary>
        Lot,
        /// <summary>Mesure vert, estime ambre, forfait violet, manquant rouge.</summary>
        Reliability,
        /// <summary>Prix renseigne vert, prix manquant rouge.</summary>
        Price
    }

    public sealed class ColoringResult
    {
        /// <summary>Elements du modele HOTE et leur couleur (#RRGGBB).</summary>
        public Dictionary<long, string> Colors { get; } = new Dictionary<long, string>();
        /// <summary>Legende : libelle et couleur, dans l'ordre d'affichage.</summary>
        public List<(string Label, string Color, int Count)> Legend { get; } = new List<(string, string, int)>();
        /// <summary>Elements de liens RVT (non colorables un par un dans la vue de l'hote).</summary>
        public int LinkedElements { get; set; }
    }

    /// <summary>
    /// Couleur de chaque element metre pour la vue 3D « DanCI ». Un element present sur plusieurs
    /// lignes prend : le lot de sa ligne au plus gros montant (mode Lot), sa fiabilite la plus
    /// faible (mode Fiabilite), « sans prix » si une de ses lignes n'a pas de prix (mode Prix).
    /// </summary>
    public static class ModelColoring
    {
        public static ColoringResult Build(BoqResult boq, ColoringMode mode)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            var result = new ColoringResult();
            var perElement = new Dictionary<long, List<BoqLine>>();
            var linked = new HashSet<(long?, long)>();
            foreach (BoqLine line in boq.AllLines)
            {
                foreach (LineContribution c in line.Contributions)
                {
                    ElementRef r = ElementRef.Of(c.Element);
                    if (r.IsLinked)
                    {
                        linked.Add((r.LinkInstanceId, r.ElementId));
                        continue;
                    }
                    if (!perElement.TryGetValue(r.ElementId, out List<BoqLine> lines))
                    {
                        perElement[r.ElementId] = lines = new List<BoqLine>();
                    }
                    if (!lines.Contains(line)) lines.Add(line);
                }
            }
            result.LinkedElements = linked.Count;
            var legend = new Dictionary<string, (string Label, string Color, int Count)>(StringComparer.Ordinal);
            foreach (var kv in perElement)
            {
                (string key, string label, string color) = Pick(kv.Value, mode);
                result.Colors[kv.Key] = color;
                legend[key] = legend.TryGetValue(key, out var e) ? (e.Label, e.Color, e.Count + 1) : (label, color, 1);
            }
            foreach (var e in legend.OrderBy(k => k.Key, StringComparer.Ordinal))
            {
                result.Legend.Add(e.Value);
            }
            return result;
        }

        private static (string key, string label, string color) Pick(List<BoqLine> lines, ColoringMode mode)
        {
            switch (mode)
            {
                case ColoringMode.Reliability:
                    Reliability worst = lines.Max(l => l.Reliability);
                    return (((int)worst).ToString(), worst.Label(), Palette.ForReliability(worst));
                case ColoringMode.Price:
                    bool missing = lines.Any(l => !l.Amount.HasValue);
                    return missing ? ("1", "Prix manquant", Palette.Red) : ("0", "Prix renseigné", Palette.Green);
                default:
                    BoqLine main = lines.OrderByDescending(l => l.Amount ?? 0m).ThenBy(l => l.Code, StringComparer.Ordinal).First();
                    return (main.LotCode, "Lot " + main.LotCode, Palette.ForLot(main.LotCode));
            }
        }
    }
}
