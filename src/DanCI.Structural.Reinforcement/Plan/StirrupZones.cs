using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>Une nappe de cadres a espacement constant, reperee sur la hauteur (mm).</summary>
    public sealed class StirrupZone
    {
        public double StartMm { get; set; }
        public double EndMm { get; set; }
        public double SpacingMm { get; set; }
        public bool IncludeFirst { get; set; }
        public bool IncludeLast { get; set; }
        public string Label { get; set; }

        public double LengthMm { get { return EndMm - StartMm; } }

        /// <summary>Nombre de cadres effectivement poses dans la zone.</summary>
        public int Count
        {
            get
            {
                if (LengthMm <= 0 || SpacingMm <= 0) return 0;
                int count = (int)Math.Ceiling(LengthMm / SpacingMm) + 1;
                if (!IncludeFirst) count--;
                if (!IncludeLast) count--;
                return count > 0 ? count : 0;
            }
        }
    }

    /// <summary>
    /// Decoupe la hauteur du poteau en zones de cadres : pied resserre, zone courante, tete
    /// resserree. Les zones critiques sont abandonnees si le poteau est trop court pour les
    /// accueillir, auquel cas l'espacement resserre s'applique sur toute la hauteur.
    /// </summary>
    public static class StirrupZones
    {
        public static List<StirrupZone> Compute(ColumnData column, ColumnReinforcement r)
        {
            var zones = new List<StirrupZone>();
            double start = r.FirstStirrupOffsetMm;
            double end = column.HeightMm - r.FirstStirrupOffsetMm;
            if (end - start <= 0) return zones;

            double critical = r.CriticalZoneLengthMm;
            bool useCritical = r.UseCriticalZones
                               && critical > 0
                               && r.SpacingCriticalMm < r.SpacingCurrentMm - 1.0
                               && (end - start) > 2.0 * critical + r.SpacingCurrentMm;

            if (!useCritical)
            {
                double spacing = r.UseCriticalZones && critical > 0 && (end - start) <= 2.0 * critical
                    ? r.SpacingCriticalMm
                    : r.SpacingCurrentMm;
                zones.Add(new StirrupZone
                {
                    StartMm = start,
                    EndMm = end,
                    SpacingMm = spacing,
                    IncludeFirst = true,
                    IncludeLast = true,
                    Label = "Cadres"
                });
                return zones;
            }

            zones.Add(new StirrupZone
            {
                StartMm = start,
                EndMm = start + critical,
                SpacingMm = r.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique basse"
            });
            zones.Add(new StirrupZone
            {
                StartMm = start + critical,
                EndMm = end - critical,
                SpacingMm = r.SpacingCurrentMm,
                IncludeFirst = false,
                IncludeLast = false,
                Label = "Zone courante"
            });
            zones.Add(new StirrupZone
            {
                StartMm = end - critical,
                EndMm = end,
                SpacingMm = r.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique haute"
            });
            return zones;
        }
    }
}
