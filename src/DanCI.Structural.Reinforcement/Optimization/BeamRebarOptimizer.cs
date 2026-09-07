using System;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>
    /// Choix des barres longitudinales d'une poutre pour une section d'acier requise.
    /// Toutes les combinaisons diametre / nombre / lits compatibles avec la largeur
    /// disponible sont evaluees, puis notees : l'acier en exces pese le plus, devant le
    /// nombre de barres et le nombre de lits, qui compliquent la mise en oeuvre.
    /// </summary>
    public sealed class BeamRebarOptimizer
    {
        private readonly double[] _diameters;
        private readonly int _maxLayers;

        public BeamRebarOptimizer(double[] diameters = null, int maxLayers = 2)
        {
            _diameters = diameters ?? BarDatabase.LongitudinalDiameters;
            _maxLayers = Math.Max(1, maxLayers);
        }

        /// <summary>
        /// Nombre maximal de barres tenant sur un lit.
        /// </summary>
        /// <param name="clearWidthMm">Largeur disponible entre les faces interieures des cadres.</param>
        /// <param name="diameterMm">Diametre des barres.</param>
        /// <param name="minClearSpacingMm">Espacement libre minimal entre barres.</param>
        public static int MaxBarsPerLayer(double clearWidthMm, double diameterMm,
                                          double minClearSpacingMm)
        {
            if (clearWidthMm < diameterMm) return 0;
            double available = clearWidthMm - diameterMm;
            int extra = (int)Math.Floor(available / (diameterMm + minClearSpacingMm));
            return 1 + Math.Max(extra, 0);
        }

        /// <summary>
        /// Retient la meilleure disposition atteignant <paramref name="requiredAreaMm2"/>.
        /// Renvoie null si aucune disposition ne tient dans la largeur disponible.
        /// </summary>
        /// <param name="requiredAreaMm2">Section d'acier requise.</param>
        /// <param name="clearWidthMm">Largeur disponible entre cadres.</param>
        /// <param name="minClearSpacingMm">Espacement libre minimal.</param>
        /// <param name="minimumBars">Nombre minimal de barres (2 pour un lit courant).</param>
        public BarSelection Select(double requiredAreaMm2, double clearWidthMm,
                                   double minClearSpacingMm, int minimumBars = 2)
        {
            if (requiredAreaMm2 <= 0) return BarSelection.None();

            BarSelection best = null;
            double bestScore = double.MaxValue;

            foreach (double diameter in _diameters)
            {
                int perLayer = MaxBarsPerLayer(clearWidthMm, diameter, minClearSpacingMm);
                if (perLayer < 1) continue;

                double barArea = UnitConverter.BarArea(diameter);

                for (int layers = 1; layers <= _maxLayers; layers++)
                {
                    int maxCount = perLayer * layers;
                    for (int count = Math.Max(minimumBars, 2); count <= maxCount; count++)
                    {
                        // Le nombre de lits doit etre le plus petit possible pour ce nombre
                        // de barres : sinon la meme solution est evaluee plusieurs fois.
                        int neededLayers = (int)Math.Ceiling((double)count / perLayer);
                        if (neededLayers != layers) continue;

                        double provided = count * barArea;
                        if (provided < requiredAreaMm2) continue;

                        var candidate = new BarSelection
                        {
                            DiameterMm = diameter,
                            Count = count,
                            Layers = layers,
                            BarsPerLayer = Math.Min(perLayer, count)
                        };

                        double score = Score(candidate, requiredAreaMm2);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                        }
                        break;   // au-dela, le meme diametre ne fait qu'ajouter de l'acier
                    }
                }
            }

            return best;
        }

        private static double Score(BarSelection selection, double requiredAreaMm2)
        {
            double excess = 100.0 * (selection.AreaMm2 / requiredAreaMm2 - 1.0);
            double count = 1.5 * selection.Count;
            double layers = 12.0 * (selection.Layers - 1);
            return excess + count + layers;
        }
    }
}
