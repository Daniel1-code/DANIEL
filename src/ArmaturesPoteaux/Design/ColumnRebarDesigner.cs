using System;
using System.Collections.Generic;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// Choisit automatiquement le ferraillage d'un poteau : diametre et nombre de barres
    /// longitudinales, diametre et espacement des cadres, epingles et recouvrement.
    /// La selection cherche la combinaison qui satisfait la norme avec le moins d'acier
    /// en exces, tout en restant constructible (espacements libres, nombre de barres pair
    /// par face, barres tenues transversalement).
    /// </summary>
    public class ColumnRebarDesigner
    {
        private readonly IDesignCode _code;
        private readonly DesignInput _input;

        public ColumnRebarDesigner(IDesignCode code, DesignInput input)
        {
            _code = code;
            _input = input;
        }

        /// <summary>
        /// Dimensionne le poteau. Quand la verification de resistance est demandee, le
        /// ferraillage est repris a la hausse tant que la section ne resiste pas, jusqu'a
        /// la limite reglementaire As,max : c'est le poteau qui converge, pas l'utilisateur.
        /// </summary>
        public DesignResult Design(ColumnGeometry geometry)
        {
            double ac = geometry.GrossAreaMm2;
            string ignored;
            double asMin = _code.MinSteelArea(ac, _input.AxialLoadKn * 1000.0,
                                              _input.SteelStrengthMPa, out ignored);
            double asMax = _code.MaxSteelArea(ac);
            double asTarget = Math.Max(asMin, _input.TargetRatioPercent / 100.0 * ac);

            DesignResult last = null;
            const int maxAttempts = 14;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                DesignResult result = Attempt(geometry, asTarget);
                if (!result.IsValid) return last ?? result;

                result.Quantities = QuantityCalculator.Compute(geometry, result);

                if (!_input.VerifyCapacity) return result;

                result.Check = new SectionCapacity(geometry, result, _input).Verify();
                if (!result.Check.Performed)
                {
                    result.Warnings.Add("Verification de resistance impossible : renseignez " +
                                        "l'effort normal NEd.");
                    return result;
                }

                if (result.Check.Passes)
                {
                    if (attempt > 0)
                    {
                        result.Notes.Add(string.Format(
                            "Ferraillage augmente {0} fois pour satisfaire la verification de " +
                            "resistance (taux de travail final {1:0.00}).",
                            attempt, result.Check.Utilisation));
                    }
                    return result;
                }

                last = result;
                double next = result.AsProvidedMm2 * 1.12;
                if (next > asMax || !_input.AutoBarCount || !_input.AutoLongitudinalDiameter) break;
                asTarget = next;
            }

            if (last != null)
            {
                last.Warnings.Add(string.Format(
                    "La section ne resiste pas meme avec le ferraillage maximal admissible " +
                    "(taux de travail {0:0.00}). Augmentez la section de beton, la resistance du " +
                    "beton ou reduisez la longueur de flambement.", last.Check.Utilisation));
            }
            return last ?? Attempt(geometry, asTarget);
        }

        private DesignResult Attempt(ColumnGeometry geometry, double requestedSteelArea)
        {
            var result = new DesignResult { Geometry = geometry, IsValid = false };
            result.Notes.Add("Norme appliquee : " + _code.Name);

            double ac = geometry.GrossAreaMm2;
            double axialN = _input.AxialLoadKn * 1000.0;

            string minJustification;
            double asMin = _code.MinSteelArea(ac, axialN, _input.SteelStrengthMPa, out minJustification);
            double asMax = _code.MaxSteelArea(ac);
            result.AsMinMm2 = asMin;
            result.AsMaxMm2 = asMax;
            result.Notes.Add(minJustification);
            result.Notes.Add(string.Format("As,max = {0:0} mm2 ({1:0.0} % Ac) hors zone de recouvrement",
                asMax, 100.0 * asMax / ac));

            double asTarget = Math.Max(asMin, requestedSteelArea);
            if (asTarget > asMin)
            {
                result.Notes.Add(string.Format("As vise = {0:0} mm2 ({1:0.00} % Ac)",
                    asTarget, 100.0 * asTarget / ac));
            }

            BarLayout layout = geometry.Kind == SectionKind.Circular
                ? SelectCircularLayout(geometry, asTarget, asMax, result)
                : SelectRectangularLayout(geometry, asTarget, asMax, result);

            if (layout == null)
            {
                result.Warnings.Add("Aucune disposition de barres ne satisfait a la fois la section " +
                                    "d'acier requise et les espacements minimaux. Augmentez la section " +
                                    "du poteau ou reduisez le taux vise.");
                return result;
            }

            result.BarDiameterMm = layout.Diameter;
            result.BarsAlongX = layout.CountAlongX;
            result.BarsAlongY = layout.CountAlongY;
            result.TotalBars = layout.TotalBars;
            result.AsProvidedMm2 = layout.SteelArea;

            result.Notes.Add(string.Format(
                "Armatures longitudinales : {0} HA{1:0} => As = {2:0} mm2 ({3:0.00} % Ac), As >= As,min OK",
                layout.TotalBars, layout.Diameter, layout.SteelArea, result.RatioPercent));
            if (geometry.Kind == SectionKind.Rectangular)
            {
                result.Notes.Add(string.Format(
                    "Repartition : {0} barres par face suivant X, {1} barres par face suivant Y " +
                    "(entraxe {2:0} x {3:0} mm)",
                    layout.CountAlongX, layout.CountAlongY, layout.PitchX, layout.PitchY));
            }
            if (layout.SteelArea > asMax)
            {
                result.Warnings.Add(string.Format(
                    "As = {0:0} mm2 depasse As,max = {1:0} mm2 : la section de beton est insuffisante.",
                    layout.SteelArea, asMax));
            }

            DesignTransverse(geometry, layout, result);
            DesignCrossTies(geometry, layout, result);
            DesignLaps(layout, result);

            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Armatures longitudinales
        // ------------------------------------------------------------------

        private class BarLayout
        {
            public double Diameter;
            public double TransverseDiameter;
            public int CountAlongX;
            public int CountAlongY;
            public int TotalBars;
            public double SteelArea;
            public double PitchX;
            public double PitchY;
            public double ClearSpacing;
        }

        private static double BarArea(double diameterMm)
        {
            return Math.PI * diameterMm * diameterMm / 4.0;
        }

        private IEnumerable<double> CandidateDiameters()
        {
            if (!_input.AutoLongitudinalDiameter)
            {
                yield return _input.ForcedLongitudinalDiameterMm;
                yield break;
            }
            foreach (double d in DesignInput.LongitudinalDiameters)
            {
                if (d >= _code.MinLongitudinalDiameter) yield return d;
            }
        }

        /// <summary>Diametre de cadre retenu pour un diametre longitudinal donne.</summary>
        private double TransverseDiameterFor(double longitudinalDiameter)
        {
            if (!_input.AutoTransverse) return _input.ForcedStirrupDiameterMm;
            double required = _code.MinTransverseDiameter(longitudinalDiameter);
            foreach (double d in DesignInput.TransverseDiameters)
            {
                if (d >= required - 1e-9) return d;
            }
            return DesignInput.TransverseDiameters[DesignInput.TransverseDiameters.Length - 1];
        }

        private BarLayout SelectRectangularLayout(ColumnGeometry g, double asTarget, double asMax,
                                                  DesignResult result)
        {
            int minBars = _code.MinBarCount(SectionKind.Rectangular);
            BarLayout best = null;
            double bestScore = double.MaxValue;

            foreach (double d in CandidateDiameters())
            {
                double dt = TransverseDiameterFor(d);
                double area = BarArea(d);
                double minClear = _code.MinClearBarSpacing(d, _input.AggregateSizeMm);

                // Distance entre axes des barres d'angle, dans chaque direction.
                double spanX = g.WidthMm - 2.0 * (_input.CoverMm + dt) - d;
                double spanY = g.DepthMm - 2.0 * (_input.CoverMm + dt) - d;
                if (spanX <= 0 || spanY <= 0) continue; // section trop petite pour l'enrobage demande

                int minNx = _input.AutoBarCount ? 2 : _input.ForcedBarsAlongX;
                int maxNx = _input.AutoBarCount ? MaxBarsOnFace(spanX, d, minClear) : _input.ForcedBarsAlongX;
                int minNy = _input.AutoBarCount ? 2 : _input.ForcedBarsAlongY;
                int maxNy = _input.AutoBarCount ? MaxBarsOnFace(spanY, d, minClear) : _input.ForcedBarsAlongY;

                for (int nx = minNx; nx <= maxNx; nx++)
                {
                    for (int ny = minNy; ny <= maxNy; ny++)
                    {
                        int total = 2 * (nx + ny) - 4;
                        if (total < minBars) continue;

                        double pitchX = nx > 1 ? spanX / (nx - 1) : spanX;
                        double pitchY = ny > 1 ? spanY / (ny - 1) : spanY;
                        double clearX = pitchX - d;
                        double clearY = pitchY - d;
                        if (nx > 1 && clearX < minClear) continue;
                        if (ny > 1 && clearY < minClear) continue;

                        double steel = total * area;
                        if (_input.AutoBarCount && steel < asTarget) continue;
                        if (_input.AutoBarCount && steel > asMax) continue;

                        var candidate = new BarLayout
                        {
                            Diameter = d,
                            TransverseDiameter = dt,
                            CountAlongX = nx,
                            CountAlongY = ny,
                            TotalBars = total,
                            SteelArea = steel,
                            PitchX = pitchX,
                            PitchY = pitchY,
                            ClearSpacing = Math.Min(nx > 1 ? clearX : double.MaxValue,
                                                    ny > 1 ? clearY : double.MaxValue)
                        };

                        if (!_input.AutoBarCount) return candidate;

                        double score = Score(candidate, asTarget, g);
                        if (score < bestScore)
                        {
                            bestScore = score;
                            best = candidate;
                        }
                    }
                }
            }

            if (best == null && !_input.AutoBarCount)
            {
                result.Warnings.Add("La disposition imposee ne respecte pas les espacements libres " +
                                    "minimaux ; verifiez le nombre de barres ou l'enrobage.");
            }
            return best;
        }

        private BarLayout SelectCircularLayout(ColumnGeometry g, double asTarget, double asMax,
                                               DesignResult result)
        {
            int minBars = _code.MinBarCount(SectionKind.Circular);
            BarLayout best = null;
            double bestScore = double.MaxValue;

            foreach (double d in CandidateDiameters())
            {
                double dt = TransverseDiameterFor(d);
                double area = BarArea(d);
                double minClear = _code.MinClearBarSpacing(d, _input.AggregateSizeMm);
                double radius = g.DiameterMm / 2.0 - _input.CoverMm - dt - d / 2.0;
                if (radius <= 0) continue;

                int minN = _input.AutoBarCount ? minBars : _input.ForcedCircularBarCount;
                int maxN = _input.AutoBarCount ? 40 : _input.ForcedCircularBarCount;

                for (int n = minN; n <= maxN; n++)
                {
                    double pitch = 2.0 * radius * Math.Sin(Math.PI / n); // corde entre axes de barres
                    if (pitch - d < minClear) break;

                    double steel = n * area;
                    if (_input.AutoBarCount && steel < asTarget) continue;
                    if (_input.AutoBarCount && steel > asMax) continue;

                    var candidate = new BarLayout
                    {
                        Diameter = d,
                        TransverseDiameter = dt,
                        CountAlongX = n,
                        CountAlongY = 0,
                        TotalBars = n,
                        SteelArea = steel,
                        PitchX = pitch,
                        PitchY = pitch,
                        ClearSpacing = pitch - d
                    };

                    if (!_input.AutoBarCount) return candidate;

                    double score = Score(candidate, asTarget, g);
                    if (score < bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }
            }
            return best;
        }

        private static int MaxBarsOnFace(double spanMm, double barDiameter, double minClear)
        {
            int n = 2 + (int)Math.Floor(spanMm / (barDiameter + minClear));
            return Math.Min(Math.Max(n, 2), 12);
        }

        /// <summary>
        /// Plus le score est faible, meilleure est la solution : on penalise d'abord l'acier
        /// en exces, puis le nombre de barres (cout de mise en oeuvre) et enfin les
        /// dispositions dissymetriques par rapport aux proportions de la section.
        /// </summary>
        private double Score(BarLayout layout, double asTarget, ColumnGeometry g)
        {
            double excess = 100.0 * (layout.SteelArea / asTarget - 1.0);
            double count = 0.6 * layout.TotalBars;
            double asymmetry = 0.0;
            if (g.Kind == SectionKind.Rectangular)
            {
                double wanted = g.WidthMm / g.DepthMm;
                double actual = (double)layout.CountAlongX / layout.CountAlongY;
                asymmetry = 6.0 * Math.Abs(actual - wanted);
            }
            return excess + count + asymmetry;
        }

        // ------------------------------------------------------------------
        // Armatures transversales
        // ------------------------------------------------------------------

        private void DesignTransverse(ColumnGeometry g, BarLayout layout, DesignResult result)
        {
            double dt = layout.TransverseDiameter;
            result.StirrupDiameterMm = dt;

            double required = _code.MinTransverseDiameter(layout.Diameter);
            if (dt < required - 1e-9)
            {
                result.Warnings.Add(string.Format(
                    "Le diametre de cadre impose (HA{0:0}) est inferieur au minimum reglementaire " +
                    "de {1:0.0} mm.", dt, required));
            }
            else
            {
                result.Notes.Add(string.Format(
                    "Cadres : phi_t = {0:0} mm >= max(6 ; phi_l/4) = {1:0.0} mm", dt, required));
            }

            string spacingJustification;
            double maxSpacing = _code.MaxStirrupSpacing(layout.Diameter, dt, g.MinDimensionMm,
                                                        out spacingJustification);

            double spacing;
            if (_input.AutoTransverse)
            {
                spacing = RoundDownTo(maxSpacing, 25.0);
                result.Notes.Add(spacingJustification);
                result.Notes.Add(string.Format("Espacement retenu en zone courante : {0:0} mm " +
                                               "(arrondi au multiple de 25 mm inferieur)", spacing));
            }
            else
            {
                spacing = _input.ForcedSpacingMm;
                if (spacing > maxSpacing + 1e-9)
                {
                    result.Warnings.Add(string.Format(
                        "L'espacement impose ({0:0} mm) depasse le maximum reglementaire de {1:0} mm.",
                        spacing, maxSpacing));
                }
                result.Notes.Add(spacingJustification);
            }
            result.SpacingCurrentMm = spacing;

            if (_input.UseCriticalZones)
            {
                double factor = _code.CriticalZoneSpacingFactor;
                double critical = RoundDownTo(spacing * factor, 25.0);
                if (critical < 50.0) critical = 50.0;
                result.SpacingCriticalMm = critical;
                result.CriticalZoneLengthMm = _code.CriticalZoneLength(
                    g.MaxDimensionMm, g.HeightMm, _input.Seismic);
                result.Notes.Add(string.Format(
                    "Zones critiques en pied et en tete sur {0:0} mm : espacement reduit a {1:0} mm " +
                    "(facteur {2:0.0}){3}",
                    result.CriticalZoneLengthMm, critical, factor,
                    _input.Seismic ? " - dispositions sismiques EN 1998-1 5.4.3.2.2" : ""));
            }
            else
            {
                result.SpacingCriticalMm = spacing;
                result.CriticalZoneLengthMm = 0.0;
            }
        }

        private void DesignCrossTies(ColumnGeometry g, BarLayout layout, DesignResult result)
        {
            result.CrossTiesAlongX = 0;
            result.CrossTiesAlongY = 0;
            if (!_input.AddCrossTies || g.Kind != SectionKind.Rectangular) return;

            double limit = _code.MaxDistanceToRestrainedBar;

            // Une barre intermediaire doit etre tenue si elle est a plus de "limit" d'une barre
            // deja tenue (barre d'angle ou barre reprise par une epingle).
            if (layout.CountAlongX > 2 && layout.PitchX > limit)
            {
                result.CrossTiesAlongY = layout.CountAlongX - 2; // epingles orientees suivant Y
            }
            if (layout.CountAlongY > 2 && layout.PitchY > limit)
            {
                result.CrossTiesAlongX = layout.CountAlongY - 2; // epingles orientees suivant X
            }

            int total = result.CrossTiesAlongX + result.CrossTiesAlongY;
            if (total > 0)
            {
                result.Notes.Add(string.Format(
                    "{0} epingle(s) HA{1:0} par lit : l'entraxe des barres ({2:0} / {3:0} mm) depasse " +
                    "{4:0} mm, les barres intermediaires doivent etre tenues.",
                    total, layout.TransverseDiameter, layout.PitchX, layout.PitchY, limit));
            }
            else if (layout.CountAlongX > 2 || layout.CountAlongY > 2)
            {
                result.Notes.Add(string.Format(
                    "Pas d'epingle necessaire : l'entraxe des barres reste inferieur a {0:0} mm.", limit));
            }
        }

        private void DesignLaps(BarLayout layout, DesignResult result)
        {
            string justification;
            double lap = _code.LapLength(layout.Diameter, _input.ConcreteStrengthMPa,
                                         _input.SteelStrengthMPa, out justification);
            lap = RoundUpTo(lap, 50.0);
            result.LapLengthMm = lap;
            result.Notes.Add(justification);

            result.CoverMm = _input.CoverMm;
            result.FirstStirrupOffsetMm = _input.FirstStirrupOffsetMm;
            result.UseCriticalZones = _input.UseCriticalZones;
            result.BottomOffsetMm = _input.BottomOffsetMm;
            result.TopExtensionMm = _input.TopExtensionMm < 0 ? lap : _input.TopExtensionMm;
            result.Notes.Add(string.Format(
                "Attentes en tete de poteau : {0:0} mm au-dessus du nu superieur.", result.TopExtensionMm));
        }

        private static double RoundDownTo(double value, double step)
        {
            double r = Math.Floor(value / step) * step;
            return r < step ? step : r;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
