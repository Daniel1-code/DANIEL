using System.Collections.Generic;

namespace ArmaturesPoteaux.Core
{
    /// <summary>Une nappe de cadres a espacement constant, reperee sur la hauteur du poteau (mm).</summary>
    public class StirrupZone
    {
        public double StartMm;
        public double EndMm;
        public double SpacingMm;
        public bool IncludeFirst;
        public bool IncludeLast;
        public string Label;

        public double LengthMm
        {
            get { return EndMm - StartMm; }
        }

        /// <summary>Nombre de cadres effectivement poses dans la zone.</summary>
        public int Count
        {
            get
            {
                if (LengthMm <= 0 || SpacingMm <= 0) return 0;
                int intervals = (int)System.Math.Ceiling(LengthMm / SpacingMm);
                int count = intervals + 1;
                if (!IncludeFirst) count--;
                if (!IncludeLast) count--;
                return count > 0 ? count : 0;
            }
        }
    }

    /// <summary>
    /// Decoupe la hauteur du poteau en zones de cadres : pied resserre, zone courante,
    /// tete resserree. Utilise a la fois par le modeleur Revit et par le quantitatif,
    /// pour que les metres cubes d'acier annonces correspondent aux barres posees.
    /// </summary>
    public static class StirrupZones
    {
        public static List<StirrupZone> Compute(ColumnGeometry g, DesignResult d)
        {
            var zones = new List<StirrupZone>();
            double start = d.FirstStirrupOffsetMm;
            double end = g.HeightMm - d.FirstStirrupOffsetMm;
            if (end - start <= 0) return zones;

            double critical = d.CriticalZoneLengthMm;
            bool useCritical = d.UseCriticalZones
                               && critical > 0
                               && d.SpacingCriticalMm < d.SpacingCurrentMm - 1.0
                               && (end - start) > 2.0 * critical + d.SpacingCurrentMm;

            if (!useCritical)
            {
                double spacing = d.UseCriticalZones && critical > 0 && (end - start) <= 2.0 * critical
                    ? d.SpacingCriticalMm     // poteau court : entierement en zone critique
                    : d.SpacingCurrentMm;
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
                SpacingMm = d.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique basse"
            });
            zones.Add(new StirrupZone
            {
                StartMm = start + critical,
                EndMm = end - critical,
                SpacingMm = d.SpacingCurrentMm,
                IncludeFirst = false,
                IncludeLast = false,
                Label = "Zone courante"
            });
            zones.Add(new StirrupZone
            {
                StartMm = end - critical,
                EndMm = end,
                SpacingMm = d.SpacingCriticalMm,
                IncludeFirst = true,
                IncludeLast = true,
                Label = "Zone critique haute"
            });
            return zones;
        }
    }
}
