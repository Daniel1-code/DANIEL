using System;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>Une nappe : un diametre et un espacement.</summary>
    public sealed class MeshSelection
    {
        public double DiameterMm { get; set; }
        public double SpacingMm { get; set; }

        /// <summary>Section fournie par metre lineaire (mm2/m).</summary>
        public double AreaPerMetreMm2
        {
            get
            {
                if (SpacingMm <= 0) return 0.0;
                return 1000.0 * UnitConverter.BarArea(DiameterMm) / SpacingMm;
            }
        }

        public string Label
        {
            get
            {
                if (DiameterMm <= 0) return "-";
                return string.Format("HA{0:0} e={1:0}", DiameterMm, SpacingMm);
            }
        }

        /// <summary>Nombre de barres pour une largeur donnee, la premiere posee a mi-espacement.</summary>
        public int CountOver(double widthMm)
        {
            if (SpacingMm <= 0 || widthMm <= 0) return 0;
            return Math.Max((int)Math.Floor(widthMm / SpacingMm), 1);
        }
    }

    /// <summary>
    /// Choix d'une nappe d'armatures pour une section requise par metre. Les combinaisons
    /// diametre / espacement sont notees : l'acier en exces pese le plus, devant les
    /// espacements serres, penibles a mettre en oeuvre.
    /// </summary>
    public sealed class MeshOptimizer
    {
        /// <summary>Espacements courants sur chantier (mm).</summary>
        public static readonly double[] Spacings = { 100, 125, 150, 175, 200, 250, 300 };

        /// <summary>Diametres courants pour une nappe (mm).</summary>
        public static readonly double[] Diameters = { 8, 10, 12, 14, 16, 20, 25 };

        private readonly double[] _diameters;

        public MeshOptimizer(double[] diameters = null)
        {
            _diameters = diameters ?? Diameters;
        }

        /// <summary>
        /// Retient la meilleure nappe fournissant <paramref name="requiredAreaPerMetreMm2"/>.
        /// </summary>
        /// <param name="requiredAreaPerMetreMm2">Section requise (mm2/m).</param>
        /// <param name="maxSpacingMm">Espacement maximal reglementaire.</param>
        public MeshSelection Select(double requiredAreaPerMetreMm2, double maxSpacingMm)
        {
            MeshSelection best = null;
            double bestScore = double.MaxValue;

            foreach (double diameter in _diameters)
            {
                foreach (double spacing in Spacings)
                {
                    if (spacing > maxSpacingMm + 1e-9) continue;

                    var candidate = new MeshSelection { DiameterMm = diameter, SpacingMm = spacing };
                    if (candidate.AreaPerMetreMm2 < requiredAreaPerMetreMm2) continue;

                    double excess = requiredAreaPerMetreMm2 > 0
                        ? 100.0 * (candidate.AreaPerMetreMm2 / requiredAreaPerMetreMm2 - 1.0)
                        : 0.0;
                    // Un espacement serre multiplie les barres a poser.
                    double handling = 1200.0 / spacing;
                    double score = excess + handling;

                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }

            return best;
        }
    }
}
