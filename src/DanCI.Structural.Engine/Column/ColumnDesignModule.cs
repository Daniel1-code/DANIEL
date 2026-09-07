using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.Detailing;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Column
{
    /// <summary>
    /// DanCI Column Design : dimensionne le ferraillage d'un poteau, verifie les dispositions
    /// constructives et, si l'utilisateur le demande, la resistance en flexion composee.
    /// Quand la section ne resiste pas, le ferraillage est repris a la hausse par paliers
    /// jusqu'a la limite reglementaire : c'est le moteur qui converge, pas l'utilisateur.
    /// </summary>
    public sealed class ColumnDesignModule
        : IDesignModule<ColumnData, ColumnDesignSettings, ColumnDesignResult>
    {
        private const int MaxAttempts = 14;
        private const double SteelIncreaseStep = 1.12;

        public string Name { get { return "DanCI Column Design"; } }

        public ColumnDesignResult Design(ColumnData column, ColumnDesignSettings settings,
                                         IReadOnlyList<LoadCombination> combinations)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var codeSettings = new CodeSettings(settings.Generation, annex);
            IColumnDetailingCode detailing = settings.DetailingCode == DetailingCodeKind.Aci318
                ? (IColumnDetailingCode)new Aci318ColumnDetailing()
                : new Ec2ColumnDetailing(annex);

            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            LoadCombination governing = ResolveCombination(combinations, settings);

            double grossArea = column.GrossAreaMm2;
            double axialForceN = governing.Stations[0].Forces.N;

            string minJustification;
            double asMin = detailing.MinSteelArea(grossArea, axialForceN, settings.SteelStrengthMPa,
                                                  out minJustification);
            double asMax = detailing.MaxSteelArea(grossArea);
            double asTarget = Math.Max(asMin, settings.TargetRatioPercent / 100.0 * grossArea);

            ColumnDesignResult last = null;

            for (int attempt = 0; attempt < MaxAttempts; attempt++)
            {
                ColumnDesignResult result = Attempt(column, settings, detailing, materials,
                                                    codeSettings, governing, asMin, asMax, asTarget,
                                                    minJustification);
                if (!result.IsValid) return last ?? result;

                if (!settings.VerifyCapacity) return result;

                var capacity = new ColumnCapacityCheck(materials);
                bool passes = capacity.Verify(column, result.Reinforcement, governing,
                                              settings.BucklingFactor, settings.CreepCoefficient,
                                              result.Checks, result.Notes);
                if (passes)
                {
                    if (attempt > 0)
                    {
                        result.Notes.Add(string.Format(
                            "Ferraillage augmente {0} fois pour satisfaire la verification de " +
                            "resistance (taux de travail final {1:0.00}).",
                            attempt, capacity.Utilization));
                    }
                    return result;
                }

                last = result;
                double next = result.Reinforcement.SteelAreaMm2 * SteelIncreaseStep;
                if (next > asMax || !settings.AutoBarCount || !settings.AutoLongitudinalDiameter) break;
                asTarget = next;
            }

            if (last != null)
            {
                last.Warnings.Add(
                    "SECTION INSUFFISANTE : la section ne resiste pas meme avec le ferraillage " +
                    "maximal admissible. Actions possibles : augmenter la largeur, augmenter la " +
                    "hauteur, augmenter la classe de beton, reduire la longueur de flambement, " +
                    "ou reprendre le modele structurel.");
            }
            return last;
        }

        /// <summary>
        /// Retient la combinaison a utiliser. En l'absence de combinaisons fournies, une
        /// combinaison ELU unique est construite a partir des efforts saisis : les six
        /// composantes restent ainsi solidaires des le depart.
        /// </summary>
        private static LoadCombination ResolveCombination(IReadOnlyList<LoadCombination> combinations,
                                                          ColumnDesignSettings settings)
        {
            if (combinations != null)
            {
                foreach (LoadCombination combination in combinations)
                {
                    if (combination.IsUltimate && combination.Stations.Count > 0) return combination;
                }
            }

            var forces = InternalForces.Column(
                UnitSystem.KnToN(settings.AxialLoadKn),
                UnitSystem.KnmToNmm(settings.MomentAboutXKnm),
                UnitSystem.KnmToNmm(settings.MomentAboutYKnm));
            return LoadCombination.Single("ULS-COMB-001", DesignSituation.UltimateFundamental, forces);
        }

        private ColumnDesignResult Attempt(ColumnData column, ColumnDesignSettings settings,
                                           IColumnDetailingCode detailing, ConcreteProperties materials,
                                           CodeSettings codeSettings, LoadCombination combination,
                                           double asMin, double asMax, double asTarget,
                                           string minJustification)
        {
            var result = new ColumnDesignResult
            {
                Column = column,
                IsValid = false,
                MinSteelAreaMm2 = asMin,
                MaxSteelAreaMm2 = asMax,
                CodeLabel = detailing.Name
            };
            result.Notes.Add("Norme appliquee : " + detailing.Name);
            result.Notes.Add("Combinaison dimensionnante : " + combination.Id);
            result.Notes.Add(minJustification);

            var optimizer = new RebarOptimizer(detailing, settings.ToLayoutOptions());
            ColumnBarLayout layout = optimizer.Optimize(column, asTarget, asMax);
            if (layout == null)
            {
                result.Warnings.Add(
                    "Aucune disposition de barres ne satisfait a la fois la section d'acier " +
                    "requise et les espacements minimaux. Augmentez la section du poteau, " +
                    "reduisez le taux vise ou reduisez l'enrobage.");
                return result;
            }

            ColumnReinforcement reinforcement = BuildReinforcement(column, settings, detailing,
                                                                   materials, layout, result);
            result.Reinforcement = reinforcement;
            result.Plan = ColumnPlanBuilder.Build(column, reinforcement, MarkPrefix(column));

            AddDetailingChecks(column, settings, detailing, layout, reinforcement, asMin, asMax,
                               combination, result);

            result.IsValid = true;
            return result;
        }

        private static string MarkPrefix(ColumnData column)
        {
            return "COL";
        }

        private ColumnReinforcement BuildReinforcement(ColumnData column, ColumnDesignSettings settings,
                                                       IColumnDetailingCode detailing,
                                                       ConcreteProperties materials,
                                                       ColumnBarLayout layout, ColumnDesignResult result)
        {
            var r = new ColumnReinforcement
            {
                BarDiameterMm = layout.DiameterMm,
                BarsAlongX = layout.CountAlongX,
                BarsAlongY = layout.CountAlongY,
                TotalBars = layout.TotalBars,
                SteelAreaMm2 = layout.SteelAreaMm2,
                StirrupDiameterMm = layout.TransverseDiameterMm,
                CoverMm = settings.CoverMm,
                FirstStirrupOffsetMm = settings.FirstStirrupOffsetMm,
                UseCriticalZones = settings.UseCriticalZones,
                BottomOffsetMm = settings.BottomOffsetMm
            };

            result.Notes.Add(string.Format(
                "Armatures longitudinales : {0} HA{1:0} => As = {2:0} mm2 ({3:0.00} % Ac)",
                layout.TotalBars, layout.DiameterMm, layout.SteelAreaMm2,
                100.0 * layout.SteelAreaMm2 / column.GrossAreaMm2));
            if (column.Shape == SectionShape.Rectangular)
            {
                result.Notes.Add(string.Format(
                    "Repartition : {0} barres par face suivant X, {1} par face suivant Y " +
                    "(entraxe {2:0} x {3:0} mm)",
                    layout.CountAlongX, layout.CountAlongY, layout.PitchXMm, layout.PitchYMm));
            }

            // --- Espacement des cadres ---
            string spacingJustification;
            double maxSpacing = detailing.MaxStirrupSpacingMm(layout.DiameterMm,
                layout.TransverseDiameterMm, column.MinDimensionMm, out spacingJustification);
            result.Notes.Add(spacingJustification);

            double spacing;
            if (settings.AutoTransverse)
            {
                spacing = RoundDownTo(maxSpacing, 25.0);
                result.Notes.Add(string.Format(
                    "Espacement retenu en zone courante : {0:0} mm (multiple de 25 mm inferieur)",
                    spacing));
            }
            else
            {
                spacing = settings.ForcedSpacingMm;
            }
            r.SpacingCurrentMm = spacing;

            // --- Zones critiques ---
            if (settings.UseCriticalZones)
            {
                double factor = detailing.CriticalZoneSpacingFactor;
                double critical = Math.Max(RoundDownTo(spacing * factor, 25.0), 50.0);
                r.SpacingCriticalMm = critical;
                r.CriticalZoneLengthMm = detailing.CriticalZoneLengthMm(
                    column.MaxDimensionMm, column.HeightMm, settings.Seismic);
                result.Notes.Add(string.Format(
                    "Zones critiques en pied et en tete sur {0:0} mm : espacement reduit a {1:0} mm " +
                    "(facteur {2:0.0}){3}",
                    r.CriticalZoneLengthMm, critical, factor,
                    settings.Seismic ? " - dispositions sismiques EN 1998-1 5.4.3.2.2" : ""));
            }
            else
            {
                r.SpacingCriticalMm = spacing;
                r.CriticalZoneLengthMm = 0.0;
            }

            // --- Epingles ---
            if (settings.AddCrossTies && column.Shape == SectionShape.Rectangular)
            {
                double limit = detailing.MaxDistanceToRestrainedBarMm;
                if (layout.CountAlongX > 2 && layout.PitchXMm > limit)
                {
                    r.CrossTiesAlongY = layout.CountAlongX - 2;
                }
                if (layout.CountAlongY > 2 && layout.PitchYMm > limit)
                {
                    r.CrossTiesAlongX = layout.CountAlongY - 2;
                }
                int total = r.CrossTiesAlongX + r.CrossTiesAlongY;
                if (total > 0)
                {
                    result.Notes.Add(string.Format(
                        "{0} epingle(s) HA{1:0} par lit : l'entraxe des barres depasse {2:0} mm, " +
                        "les barres intermediaires doivent etre tenues.",
                        total, r.StirrupDiameterMm, limit));
                }
            }

            // --- Ancrage et recouvrement ---
            AnchorageResult anchorage = Anchorage.Compute(layout.DiameterMm, materials,
                NationalAnnexFactory.Create(settings.NationalAnnex));
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            r.TopExtensionMm = settings.TopExtensionMm < 0 ? r.LapLengthMm : settings.TopExtensionMm;
            result.Notes.Add(string.Format(
                "Attentes en tete de poteau : {0:0} mm au-dessus du nu superieur.", r.TopExtensionMm));

            return r;
        }

        private static void AddDetailingChecks(ColumnData column, ColumnDesignSettings settings,
                                               IColumnDetailingCode detailing, ColumnBarLayout layout,
                                               ColumnReinforcement r, double asMin, double asMax,
                                               LoadCombination combination, ColumnDesignResult result)
        {
            string code = detailing.ShortName == "ACI" ? "ACI 318-19" : "EN 1992-1-1:2004";

            var minSteel = new CheckResult
            {
                Code = code,
                Clause = detailing.ShortName == "ACI" ? "10.6.1.1" : "9.5.2 (2)",
                Equation = "As >= As,min",
                Description = "Section minimale d'armature longitudinale",
                GoverningCombination = combination.Id
            };
            minSteel.Verify(Quantity.Area(asMin), Quantity.Area(r.SteelAreaMm2));
            result.Checks.Add(minSteel);

            var maxSteel = new CheckResult
            {
                Code = code,
                Clause = detailing.ShortName == "ACI" ? "10.6.1.1" : "9.5.2 (3)",
                Equation = "As <= As,max",
                Description = "Section maximale d'armature longitudinale",
                GoverningCombination = combination.Id
            };
            maxSteel.Verify(Quantity.Area(r.SteelAreaMm2), Quantity.Area(asMax));
            result.Checks.Add(maxSteel);

            int minBars = detailing.MinBarCount(column.Shape);
            var barCount = new CheckResult
            {
                Code = code,
                Clause = detailing.ShortName == "ACI" ? "10.7.3.1" : "9.5.2 (4)",
                Equation = "n >= n_min",
                Description = "Nombre minimal de barres longitudinales"
            };
            barCount.Verify(Quantity.Ratio(minBars), Quantity.Ratio(r.TotalBars));
            result.Checks.Add(barCount);

            double requiredTie = detailing.MinTransverseDiameterMm(layout.DiameterMm);
            var tieDiameter = new CheckResult
            {
                Code = code,
                Clause = detailing.ShortName == "ACI" ? "25.7.2.2" : "9.5.3 (1)",
                Equation = "phi_t >= max(6 mm ; phi_l / 4)",
                Description = "Diametre des armatures transversales"
            };
            tieDiameter.Verify(Quantity.Length(requiredTie), Quantity.Length(r.StirrupDiameterMm));
            result.Checks.Add(tieDiameter);

            string ignored;
            double maxSpacing = detailing.MaxStirrupSpacingMm(layout.DiameterMm,
                r.StirrupDiameterMm, column.MinDimensionMm, out ignored);
            var tieSpacing = new CheckResult
            {
                Code = code,
                Clause = detailing.ShortName == "ACI" ? "25.7.2.1" : "9.5.3 (3)",
                Equation = "s <= scl,tmax",
                Description = "Espacement des cadres en zone courante"
            };
            tieSpacing.Verify(Quantity.Length(r.SpacingCurrentMm), Quantity.Length(maxSpacing));
            result.Checks.Add(tieSpacing);

            double minClear = detailing.MinClearBarSpacingMm(layout.DiameterMm, settings.AggregateSizeMm);
            if (layout.ClearSpacingMm < double.MaxValue)
            {
                var clear = new CheckResult
                {
                    Code = code,
                    Clause = detailing.ShortName == "ACI" ? "25.2.3" : "8.2 (2)",
                    Equation = "espacement libre >= max(phi ; dg + 5 ; 20 mm)",
                    Description = "Espacement libre entre barres longitudinales"
                };
                clear.Verify(Quantity.Length(minClear), Quantity.Length(layout.ClearSpacingMm));
                result.Checks.Add(clear);
            }

            if (column.Shape == SectionShape.Rectangular)
            {
                double limit = detailing.MaxDistanceToRestrainedBarMm;
                double maxPitch = Math.Max(layout.PitchXMm, layout.PitchYMm);
                bool restrained = maxPitch <= limit || r.CrossTiesAlongX + r.CrossTiesAlongY > 0;
                var restraint = new CheckResult
                {
                    Code = code,
                    Clause = detailing.ShortName == "ACI" ? "25.7.2.3" : "9.5.3 (6)",
                    Equation = "distance a une barre tenue <= 150 mm",
                    Description = "Maintien des barres comprimees",
                    Demand = Quantity.Length(maxPitch),
                    Resistance = Quantity.Length(limit),
                    Utilization = restrained ? Math.Min(maxPitch / limit, 1.0) : maxPitch / limit,
                    Status = restrained ? CheckStatus.Pass : CheckStatus.Fail,
                    Comment = restrained && maxPitch > limit
                        ? "Barres intermediaires tenues par epingles."
                        : null
                };
                result.Checks.Add(restraint);
            }
        }

        private static double RoundDownTo(double value, double step)
        {
            double rounded = Math.Floor(value / step) * step;
            return rounded < step ? step : rounded;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
