using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.Detailing;

namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>
    /// Choix de la disposition des barres longitudinales. Toutes les combinaisons de diametre
    /// et de nombre de barres compatibles sont evaluees, puis notees : l'acier en exces pese
    /// le plus, devant le nombre de barres (cout de mise en oeuvre) et la dissymetrie de la
    /// disposition par rapport aux proportions de la section.
    /// La premiere combinaison qui « passe » n'est jamais retenue telle quelle.
    /// </summary>
    public sealed class RebarOptimizer
    {
        private readonly IColumnDetailingCode _code;
        private readonly ColumnLayoutOptions _options;

        public RebarOptimizer(IColumnDetailingCode code, ColumnLayoutOptions options)
        {
            _code = code;
            _options = options;
        }

        /// <summary>Diametre de cadre retenu pour un diametre longitudinal donne.</summary>
        public double TransverseDiameterFor(double longitudinalDiameterMm)
        {
            if (!_options.AutoTransverse) return _options.ForcedTransverseDiameterMm;
            return BarDatabase.SmallestTransverseAtLeast(
                _code.MinTransverseDiameterMm(longitudinalDiameterMm));
        }

        /// <summary>
        /// Cherche la meilleure disposition atteignant <paramref name="targetSteelAreaMm2"/>
        /// sans depasser <paramref name="maxSteelAreaMm2"/>. Renvoie null si aucune
        /// disposition constructible n'existe.
        /// </summary>
        public ColumnBarLayout Optimize(ColumnData column, double targetSteelAreaMm2,
                                        double maxSteelAreaMm2)
        {
            return column.Shape == SectionShape.Circular
                ? OptimizeCircular(column, targetSteelAreaMm2, maxSteelAreaMm2)
                : OptimizeRectangular(column, targetSteelAreaMm2, maxSteelAreaMm2);
        }

        private IEnumerable<double> CandidateDiameters()
        {
            if (!_options.AutoDiameter)
            {
                yield return _options.ForcedDiameterMm;
                yield break;
            }
            foreach (double diameter in BarDatabase.LongitudinalDiameters)
            {
                if (diameter >= _code.MinLongitudinalDiameterMm) yield return diameter;
            }
        }

        private ColumnBarLayout OptimizeRectangular(ColumnData column, double target, double maximum)
        {
            int minBars = _code.MinBarCount(SectionShape.Rectangular);
            ColumnBarLayout best = null;
            double bestScore = double.MaxValue;

            foreach (double diameter in CandidateDiameters())
            {
                double transverse = TransverseDiameterFor(diameter);
                double barArea = UnitConverter.BarArea(diameter);
                double minClear = _code.MinClearBarSpacingMm(diameter, _options.AggregateSizeMm);

                // Distance entre axes des barres d'angle, dans chaque direction.
                double spanX = column.WidthMm - 2.0 * (_options.CoverMm + transverse) - diameter;
                double spanY = column.DepthMm - 2.0 * (_options.CoverMm + transverse) - diameter;
                if (spanX <= 0 || spanY <= 0) continue;   // section trop petite pour l'enrobage

                int minNx = _options.AutoCount ? 2 : _options.ForcedBarsAlongX;
                int maxNx = _options.AutoCount ? MaxBarsOnFace(spanX, diameter, minClear)
                                               : _options.ForcedBarsAlongX;
                int minNy = _options.AutoCount ? 2 : _options.ForcedBarsAlongY;
                int maxNy = _options.AutoCount ? MaxBarsOnFace(spanY, diameter, minClear)
                                               : _options.ForcedBarsAlongY;

                for (int nx = minNx; nx <= maxNx; nx++)
                {
                    for (int ny = minNy; ny <= maxNy; ny++)
                    {
                        int total = 2 * (nx + ny) - 4;
                        if (total < minBars) continue;

                        double pitchX = nx > 1 ? spanX / (nx - 1) : spanX;
                        double pitchY = ny > 1 ? spanY / (ny - 1) : spanY;
                        double clearX = pitchX - diameter;
                        double clearY = pitchY - diameter;
                        if (nx > 1 && clearX < minClear) continue;
                        if (ny > 1 && clearY < minClear) continue;

                        double steel = total * barArea;
                        if (_options.AutoCount && steel < target) continue;
                        if (_options.AutoCount && steel > maximum) continue;

                        var candidate = new ColumnBarLayout
                        {
                            DiameterMm = diameter,
                            TransverseDiameterMm = transverse,
                            CountAlongX = nx,
                            CountAlongY = ny,
                            TotalBars = total,
                            SteelAreaMm2 = steel,
                            PitchXMm = pitchX,
                            PitchYMm = pitchY,
                            ClearSpacingMm = Math.Min(nx > 1 ? clearX : double.MaxValue,
                                                      ny > 1 ? clearY : double.MaxValue)
                        };

                        if (!_options.AutoCount) return candidate;

                        double score = Score(candidate, target, column);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                        }
                    }
                }
            }
            return best;
        }

        private ColumnBarLayout OptimizeCircular(ColumnData column, double target, double maximum)
        {
            int minBars = _code.MinBarCount(SectionShape.Circular);
            ColumnBarLayout best = null;
            double bestScore = double.MaxValue;

            foreach (double diameter in CandidateDiameters())
            {
                double transverse = TransverseDiameterFor(diameter);
                double barArea = UnitConverter.BarArea(diameter);
                double minClear = _code.MinClearBarSpacingMm(diameter, _options.AggregateSizeMm);
                double radius = column.DiameterMm / 2.0 - _options.CoverMm - transverse - diameter / 2.0;
                if (radius <= 0) continue;

                int minN = _options.AutoCount ? minBars : _options.ForcedCircularBarCount;
                int maxN = _options.AutoCount ? 40 : _options.ForcedCircularBarCount;

                for (int n = minN; n <= maxN; n++)
                {
                    double pitch = 2.0 * radius * Math.Sin(Math.PI / n);   // corde entre axes
                    if (pitch - diameter < minClear) break;

                    double steel = n * barArea;
                    if (_options.AutoCount && steel < target) continue;
                    if (_options.AutoCount && steel > maximum) continue;

                    var candidate = new ColumnBarLayout
                    {
                        DiameterMm = diameter,
                        TransverseDiameterMm = transverse,
                        CountAlongX = n,
                        CountAlongY = 0,
                        TotalBars = n,
                        SteelAreaMm2 = steel,
                        PitchXMm = pitch,
                        PitchYMm = pitch,
                        ClearSpacingMm = pitch - diameter
                    };

                    if (!_options.AutoCount) return candidate;

                    double score = Score(candidate, target, column);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        private static int MaxBarsOnFace(double spanMm, double barDiameterMm, double minClearMm)
        {
            int count = 2 + (int)Math.Floor(spanMm / (barDiameterMm + minClearMm));
            return Math.Min(Math.Max(count, 2), 12);
        }

        private static double Score(ColumnBarLayout layout, double targetSteelAreaMm2, ColumnData column)
        {
            double excess = 100.0 * (layout.SteelAreaMm2 / targetSteelAreaMm2 - 1.0);
            double count = 0.6 * layout.TotalBars;
            double asymmetry = 0.0;
            if (column.Shape == SectionShape.Rectangular && layout.CountAlongY > 0)
            {
                double wanted = column.WidthMm / column.DepthMm;
                double actual = (double)layout.CountAlongX / layout.CountAlongY;
                asymmetry = 6.0 * Math.Abs(actual - wanted);
            }
            return excess + count + asymmetry;
        }
    }
}
