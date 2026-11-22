using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Dashboard
{
    /// <summary>Grandeur portee par la hauteur des barres.</summary>
    public enum ChartMetric
    {
        /// <summary>Montant HT des lignes (lignes sans prix : 0).</summary>
        Amount,
        /// <summary>Nombre d'elements Revit distincts qui contribuent aux lignes.</summary>
        Elements
    }

    /// <summary>Une barre du diagramme : un lot, un statut ou un etat de prix.</summary>
    public sealed class ChartBar
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public string Designation { get; set; }
        public string Color { get; set; }
        public decimal Amount { get; set; }
        public int LineCount { get; set; }
        public int LinesWithoutPrice { get; set; }
        /// <summary>Elements du modele HOTE (isolables dans la vue).</summary>
        public List<long> HostElementIds { get; } = new List<long>();
        /// <summary>Elements de liens RVT (selectionnables, pas isolables un par un).</summary>
        public List<ElementRef> LinkedElements { get; } = new List<ElementRef>();
        /// <summary>Quantites du BOQ cumulees par unite (articles differents : ordre de grandeur, pas une ligne de devis).</summary>
        public List<(BoqUnit Unit, decimal Value)> Quantities { get; } = new List<(BoqUnit, decimal)>();

        public int ElementCount => HostElementIds.Count + LinkedElements.Count;

        public decimal Value(ChartMetric metric) => metric == ChartMetric.Elements ? ElementCount : Amount;
    }

    /// <summary>
    /// Diagramme des quantites du BOQ par lot, par statut (fiabilite) ou par prix, pour le panneau
    /// « Diagramme » et les barres 3D de la vue. Calcul pur a partir du BOQ : aucune quantite
    /// ajoutee. Un element present dans deux lots compte dans les deux barres (il y est bien metre).
    /// </summary>
    public sealed class ViewChart
    {
        public ColoringMode Mode { get; private set; }
        public List<ChartBar> Bars { get; } = new List<ChartBar>();
        public decimal TotalAmount => Bars.Sum(b => b.Amount);
        public bool HasAmounts => Bars.Any(b => b.Amount > 0m);

        /// <summary>Grandeur par defaut : le montant s'il y a des prix, sinon le nombre d'elements.</summary>
        public ChartMetric DefaultMetric => HasAmounts ? ChartMetric.Amount : ChartMetric.Elements;

        public decimal Max(ChartMetric metric) => Bars.Count == 0 ? 0m : Bars.Max(b => b.Value(metric));

        public decimal Total(ChartMetric metric) =>
            metric == ChartMetric.Amount ? TotalAmount : Bars.Sum(b => b.Value(metric));

        public static ViewChart Build(BoqResult boq, ColoringMode mode)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            var chart = new ViewChart { Mode = mode };
            var bars = new Dictionary<string, ChartBar>(StringComparer.Ordinal);
            var hostSeen = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
            var linkSeen = new Dictionary<string, HashSet<(long?, long)>>(StringComparer.Ordinal);
            var quantities = new Dictionary<string, Dictionary<BoqUnit, decimal>>(StringComparer.Ordinal);

            foreach (BoqLotResult lot in boq.Lots)
            {
                foreach (BoqLine line in lot.Lines)
                {
                    (string key, string label, string designation, string color) = Group(line, lot, mode);
                    if (!bars.TryGetValue(key, out ChartBar bar))
                    {
                        bars[key] = bar = new ChartBar { Key = key, Label = label, Designation = designation, Color = color };
                        hostSeen[key] = new HashSet<long>();
                        linkSeen[key] = new HashSet<(long?, long)>();
                        quantities[key] = new Dictionary<BoqUnit, decimal>();
                    }
                    bar.LineCount++;
                    bar.Amount += line.Amount ?? 0m;
                    if (!line.Amount.HasValue) bar.LinesWithoutPrice++;
                    quantities[key][line.Unit] = (quantities[key].TryGetValue(line.Unit, out decimal q) ? q : 0m) + line.Quantity;
                    foreach (LineContribution c in line.Contributions)
                    {
                        ElementRef r = ElementRef.Of(c.Element);
                        if (r.IsLinked)
                        {
                            if (linkSeen[key].Add((r.LinkInstanceId, r.ElementId))) bar.LinkedElements.Add(r);
                        }
                        else if (hostSeen[key].Add(r.ElementId))
                        {
                            bar.HostElementIds.Add(r.ElementId);
                        }
                    }
                }
            }
            foreach (ChartBar bar in bars.Values.OrderBy(b => b.Key, StringComparer.Ordinal))
            {
                foreach (var q in quantities[bar.Key].Where(q => q.Value != 0m).OrderByDescending(q => q.Value))
                {
                    bar.Quantities.Add((q.Key, q.Value));
                }
                chart.Bars.Add(bar);
            }
            return chart;
        }

        private static (string key, string label, string designation, string color) Group(BoqLine line, BoqLotResult lot, ColoringMode mode)
        {
            switch (mode)
            {
                case ColoringMode.Reliability:
                    return (((int)line.Reliability).ToString(), line.Reliability.Label(), StatusDesignation(line.Reliability),
                        Palette.ForReliability(line.Reliability));
                case ColoringMode.Price:
                    return line.Amount.HasValue
                        ? ("0", "Prix renseigné", "lignes valorisées", Palette.Green)
                        : ("1", "Prix manquant", "lignes à valoriser", Palette.Red);
                default:
                    string code = line.LotCode ?? lot.Code;
                    return (code, "Lot " + code, lot.Designation, Palette.ForLot(code));
            }
        }

        private static string StatusDesignation(Reliability r)
        {
            switch (r)
            {
                case Reliability.Measured: return "quantités lues dans la maquette";
                case Reliability.Manual: return "forfaits et saisies manuelles";
                case Reliability.Estimated: return "quantités estimées : à vérifier";
                default: return "quantités manquantes";
            }
        }
    }

    /// <summary>Boite alignee sur les axes (pieds Revit), avec sa couleur #RRGGBB.</summary>
    public struct ChartBox
    {
        public double MinX, MinY, MinZ, MaxX, MaxY, MaxZ;
        public string Color;

        public ChartBox(double minX, double minY, double minZ, double maxX, double maxY, double maxZ, string color)
        {
            MinX = minX; MinY = minY; MinZ = minZ; MaxX = maxX; MaxY = maxY; MaxZ = maxZ; Color = color;
        }

        public double Height => MaxZ - MinZ;
    }

    /// <summary>
    /// Disposition des barres 3D a cote du modele : une rangee devant la facade sud (Y min),
    /// alignee sur la largeur du modele, posee au niveau bas du modele. Calcul pur (pieds).
    /// </summary>
    public static class ChartLayout
    {
        public const string BaseColor = "#1E3A5F";

        /// <summary>Hauteur minimale d'une barre non nulle (fraction de la hauteur maximale) : reste visible.</summary>
        public const double MinBarFraction = 0.03;

        public static List<ChartBox> Bars(ViewChart chart, ChartMetric metric,
            double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            var boxes = new List<ChartBox>();
            int n = chart?.Bars.Count ?? 0;
            if (n == 0) return boxes;
            double width = Math.Max(maxX - minX, 10.0);
            double depthModel = Math.Max(maxY - minY, 10.0);
            double heightModel = Math.Max(maxZ - minZ, 10.0);
            double pitch = width / n;
            double barWidth = Math.Min(pitch * 0.7, width * 0.25);
            double barDepth = Math.Min(barWidth, depthModel * 0.15);
            double gap = Math.Max(depthModel * 0.15, barDepth);
            double y1 = minY - gap;
            double y0 = y1 - barDepth;
            double z0 = minZ;
            double maxHeight = Math.Max(heightModel, width * 0.3);
            decimal max = chart.Max(metric);

            // Socle sombre sous la rangee : repere visuel du diagramme.
            double plinth = maxHeight * 0.01;
            boxes.Add(new ChartBox(minX, y0 - barDepth * 0.25, z0 - plinth, minX + width, y1 + barDepth * 0.25, z0, BaseColor));
            for (int i = 0; i < n; i++)
            {
                ChartBar bar = chart.Bars[i];
                decimal v = bar.Value(metric);
                double h = max > 0m && v > 0m ? Math.Max((double)(v / max), MinBarFraction) * maxHeight : 0;
                if (h <= 0) continue;
                double x0 = minX + i * pitch + (pitch - barWidth) / 2;
                boxes.Add(new ChartBox(x0, y0, z0, x0 + barWidth, y1, z0 + h, bar.Color));
            }
            return boxes;
        }
    }
}
