using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Beam
{
    /// <summary>
    /// DanCI Beam Design : flexion, effort tranchant, choix des barres et dispositions
    /// constructives d'une poutre en beton arme, selon l'EN 1992-1-1:2004.
    ///
    /// La travee est traitee en trois zones : appui gauche, zone courante, appui droit.
    /// L'effort tranchant y est evalue a partir de sa variation lineaire entre les deux
    /// appuis, ce qui donne des cadres resserres pres des appuis et espaces en travee,
    /// au lieu d'un espacement unique arbitraire.
    /// </summary>
    public sealed class BeamDesignModule
        : IDesignModule<BeamData, BeamDesignSettings, BeamDesignResult>
    {
        private const string CodeName = "EN 1992-1-1:2004";

        /// <summary>Part de la portee occupee par chaque zone d'appui.</summary>
        private const double SupportZoneRatio = 0.25;

        /// <summary>
        /// Diametre minimal des cadres de poutre. L'Eurocode ne l'impose pas, mais un cadre
        /// HA6 ne se met pas en oeuvre correctement sur une poutre : la pratique retient HA8.
        /// </summary>
        private const double MinimumStirrupDiameterMm = 8.0;

        public string Name { get { return "DanCI Beam Design"; } }

        /// <summary>
        /// Dimensionne la poutre. Le calcul est mene deux fois : l'enrobage et la hauteur
        /// utile dependent du diametre et du nombre de lits finalement retenus, on reprend
        /// donc le calcul avec les valeurs reelles des que celles-ci s'ecartent de
        /// l'hypothese de depart.
        /// </summary>
        public BeamDesignResult Design(BeamData beam, BeamDesignSettings settings,
                                       IReadOnlyList<LoadCombination> combinations)
        {
            double assumedDiameter = settings.AutoLongitudinalDiameter
                ? 20.0 : settings.ForcedLongitudinalDiameterMm;

            BeamDesignResult first = DesignOnce(beam, settings, combinations, assumedDiameter, 1);
            if (!first.IsValid) return first;

            double actualDiameter = first.Reinforcement.BottomSpan.DiameterMm;
            int actualLayers = Math.Max(first.Reinforcement.BottomSpan.Layers, 1);
            bool sameDiameter = Math.Abs(actualDiameter - assumedDiameter) < 0.5;
            if (sameDiameter && actualLayers == 1) return first;

            BeamDesignResult second = DesignOnce(beam, settings, combinations, actualDiameter,
                                                 actualLayers);
            return second.IsValid ? second : first;
        }

        private BeamDesignResult DesignOnce(BeamData beam, BeamDesignSettings settings,
                                            IReadOnlyList<LoadCombination> combinations,
                                            double assumedDiameterMm, int assumedLayers)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            beam = ApplyUserGeometry(beam, settings);
            LoadCombination combination = ResolveCombination(combinations, settings, beam);

            var result = new BeamDesignResult
            {
                Beam = beam,
                IsValid = false,
                CodeLabel = CodeName + " - " + annex.Name
            };
            result.Notes.Add("Norme appliquee : " + result.CodeLabel);
            result.Notes.Add("Combinaison dimensionnante : " + combination.Id);

            // --- Enrobage ---
            double provisionalDiameter = assumedDiameterMm;
            double stirrupDiameter = settings.AutoStirrupDiameter
                ? MinimumStirrupDiameterMm : settings.ForcedStirrupDiameterMm;
            CoverResult cover = ResolveCover(settings, provisionalDiameter);
            result.Notes.Add(cover.Justification);

            var r = new BeamReinforcement
            {
                CoverMm = cover.NominalCoverMm,
                StirrupDiameterMm = stirrupDiameter,
                StirrupLegs = settings.StirrupLegs
            };

            double effectiveDepth = beam.EffectiveDepthMm(r.CoverMm, stirrupDiameter,
                                                          provisionalDiameter, assumedLayers);
            r.EffectiveDepthMm = effectiveDepth;
            if (effectiveDepth <= 0)
            {
                result.Warnings.Add("La hauteur de la poutre est trop faible pour l'enrobage demande.");
                return result;
            }

            double compressionSteelDepth = r.CoverMm + stirrupDiameter + provisionalDiameter / 2.0;
            double minClear = MinimumClearSpacing(provisionalDiameter, settings.AggregateSizeMm, annex);
            double clearWidth = beam.WebWidthMm - 2.0 * (r.CoverMm + stirrupDiameter);

            // --- Largeur participante de la table ---
            double effectiveFlange = beam.WebWidthMm;
            if (beam.Shape == BeamSectionShape.TSection && beam.FlangeWidthMm > beam.WebWidthMm)
            {
                double zeroMoment = EffectiveFlangeWidth.ZeroMomentLengthMm(beam.SpanKind, beam.SpanMm);
                effectiveFlange = EffectiveFlangeWidth.Compute(beam.WebWidthMm,
                    beam.FlangeWidthMm, zeroMoment);
                result.Notes.Add(string.Format(
                    "EC2 5.3.2.1 : l0 = {0:0} mm -> largeur participante b_eff = {1:0} mm",
                    zeroMoment, effectiveFlange));
            }
            result.EffectiveFlangeWidthMm = effectiveFlange;

            // --- Flexion ---
            string minJustification;
            double asMin = BendingDesign.MinimumTensionSteel(beam.WebWidthMm, effectiveDepth,
                materials, out minJustification);
            double asMax = BendingDesign.MaximumSteel(beam.GrossAreaMm2);
            result.MinSteelAreaMm2 = asMin;
            result.MaxSteelAreaMm2 = asMax;
            result.Notes.Add(minJustification);

            // En travee, la table est comprimee : la section en T travaille pleinement.
            BendingResult span = beam.Shape == BeamSectionShape.TSection
                ? BendingDesign.TSection(UnitConverter.KnmToNmm(settings.SpanMomentKnm),
                    effectiveFlange, beam.WebWidthMm, beam.FlangeThicknessMm, effectiveDepth,
                    compressionSteelDepth, materials, settings.RedistributionRatio)
                : BendingDesign.Rectangular(UnitConverter.KnmToNmm(settings.SpanMomentKnm),
                    beam.WebWidthMm, effectiveDepth, compressionSteelDepth, materials,
                    settings.RedistributionRatio);

            // Sur appui, la table est tendue : seule l'ame est comprimee.
            BendingResult left = BendingDesign.Rectangular(
                UnitConverter.KnmToNmm(settings.LeftSupportMomentKnm), beam.WebWidthMm,
                effectiveDepth, compressionSteelDepth, materials, settings.RedistributionRatio);
            BendingResult right = BendingDesign.Rectangular(
                UnitConverter.KnmToNmm(settings.RightSupportMomentKnm), beam.WebWidthMm,
                effectiveDepth, compressionSteelDepth, materials, settings.RedistributionRatio);

            result.SpanSteelRequiredMm2 = Math.Max(span.TensionSteelMm2, asMin);
            result.LeftSteelRequiredMm2 = settings.LeftSupportMomentKnm > 0
                ? Math.Max(left.TensionSteelMm2, asMin) : 0.0;
            result.RightSteelRequiredMm2 = settings.RightSupportMomentKnm > 0
                ? Math.Max(right.TensionSteelMm2, asMin) : 0.0;

            result.Notes.Add("Travee - " + span.Justification);
            if (settings.LeftSupportMomentKnm > 0) result.Notes.Add("Appui gauche - " + left.Justification);
            if (settings.RightSupportMomentKnm > 0) result.Notes.Add("Appui droit - " + right.Justification);

            // --- Choix des barres ---
            var optimizer = new BeamRebarOptimizer(
                settings.AutoLongitudinalDiameter
                    ? null : new[] { settings.ForcedLongitudinalDiameterMm },
                settings.MaxLayers);

            r.BottomSpan = optimizer.Select(result.SpanSteelRequiredMm2, clearWidth, minClear);
            if (r.BottomSpan == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : les barres de travee ne tiennent pas dans la largeur " +
                    "de l'ame. Actions possibles : elargir la poutre, augmenter sa hauteur, " +
                    "augmenter la classe de beton, ou autoriser un lit supplementaire.");
                return result;
            }

            r.TopLeft = result.LeftSteelRequiredMm2 > 0
                ? optimizer.Select(result.LeftSteelRequiredMm2, clearWidth, minClear)
                : BarSelection.None();
            r.TopRight = result.RightSteelRequiredMm2 > 0
                ? optimizer.Select(result.RightSteelRequiredMm2, clearWidth, minClear)
                : BarSelection.None();

            if (r.TopLeft == null || r.TopRight == null)
            {
                result.Warnings.Add("SECTION INSUFFISANTE : les chapeaux ne tiennent pas dans la " +
                                    "largeur de l'ame.");
                return result;
            }

            // Aciers de montage : deux barres filantes, ou les aciers comprimes s'ils sont requis.
            double compressionRequired = span.CompressionSteelMm2;
            if (compressionRequired > 0)
            {
                r.CompressionSpan = optimizer.Select(compressionRequired, clearWidth, minClear)
                                    ?? BarSelection.None();
                result.Notes.Add(string.Format(
                    "Aciers comprimes en travee : A's = {0:0} mm2 requis -> {1}",
                    compressionRequired, r.CompressionSpan.Label));
            }

            double montageArea = Math.Max(compressionRequired, 0.0);
            r.TopContinuous = montageArea > 0
                ? (optimizer.Select(montageArea, clearWidth, minClear) ?? TwoBars(r.BottomSpan))
                : TwoBars(r.BottomSpan);

            // --- Effort tranchant, zone par zone ---
            double axialStress = beam.GrossAreaMm2 > 0
                ? UnitConverter.KnToN(settings.AxialForceKn) / beam.GrossAreaMm2 : 0.0;
            BuildStirrupZones(beam, settings, materials, annex, r, result, axialStress);

            // --- Ancrages et recouvrements ---
            AnchorageResult anchorage = Anchorage.Compute(r.BottomSpan.DiameterMm, materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            // Chapeaux : au moins le quart de la portee, et au moins le decalage plus l'ancrage.
            r.TopBarLengthMm = RoundUpTo(Math.Max(0.25 * beam.SpanMm,
                r.ShiftLengthMm + r.AnchorageLengthMm), 50.0);
            result.Notes.Add(string.Format(
                "Chapeaux prolonges de {0:0} mm depuis le nu d'appui : max(L/4 ; a_l + l_bd) " +
                "= max({1:0} ; {2:0}) mm (EC2 9.2.1.3)",
                r.TopBarLengthMm, 0.25 * beam.SpanMm, r.ShiftLengthMm + r.AnchorageLengthMm));

            result.Reinforcement = r;
            result.Plan = BeamPlanBuilder.Build(beam, r, MarkPrefix(beam));

            AddChecks(beam, settings, materials, r, result, span, left, right, asMin, asMax,
                      combination);

            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Effort tranchant
        // ------------------------------------------------------------------

        private void BuildStirrupZones(BeamData beam, BeamDesignSettings settings,
                                       ConcreteProperties materials, INationalAnnex annex,
                                       BeamReinforcement r, BeamDesignResult result,
                                       double axialStressMPa)
        {
            double span = beam.SpanMm;
            double leftShear = UnitConverter.KnToN(Math.Abs(settings.LeftShearKn));
            double rightShear = UnitConverter.KnToN(Math.Abs(settings.RightShearKn));
            double supportZone = SupportZoneRatio * span;

            // Variation lineaire de l'effort tranchant entre les deux appuis.
            Func<double, double> shearAt = x =>
                Math.Abs(leftShear - (leftShear + rightShear) * x / Math.Max(span, 1.0));

            double middleShear = Math.Max(shearAt(supportZone), shearAt(span - supportZone));

            var zones = new List<Tuple<double, double, double, string>>
            {
                Tuple.Create(0.0, supportZone, leftShear, "appui gauche"),
                Tuple.Create(supportZone, span - supportZone, middleShear, "travee"),
                Tuple.Create(span - supportZone, span, rightShear, "appui droit")
            };

            // Diametre de cadre : impose, ou choisi pour que l'espacement reste constructible.
            double stirrupDiameter = r.StirrupDiameterMm;
            double maxRequired = 0.0;
            foreach (var zone in zones)
            {
                ShearResult probe = ShearDesign.Design(zone.Item3, beam.WebWidthMm,
                    r.EffectiveDepthMm, r.BottomSpan.AreaMm2, axialStressMPa, materials,
                    annex.GammaC);
                maxRequired = Math.Max(maxRequired, probe.AswPerMetreMm2);
                if (!probe.IsWebAdequate)
                {
                    result.Warnings.Add(string.Format(
                        "SECTION INSUFFISANTE en zone {0} : les bielles de beton s'ecrasent " +
                        "(V_Ed = {1:0} kN). Actions possibles : elargir l'ame, augmenter la " +
                        "hauteur, ou augmenter la classe de beton.",
                        zone.Item4, zone.Item3 / 1000.0));
                }
                r.ShiftLengthMm = Math.Max(r.ShiftLengthMm, probe.ShiftLengthMm);
            }

            if (settings.AutoStirrupDiameter)
            {
                // On monte en diametre tant que l'espacement reste sous 75 mm, peu constructible.
                foreach (double diameter in BarDatabase.TransverseDiameters)
                {
                    if (diameter < MinimumStirrupDiameterMm) continue;
                    double area = settings.StirrupLegs * UnitConverter.BarArea(diameter);
                    double spacing = maxRequired > 0 ? area / maxRequired : double.MaxValue;
                    stirrupDiameter = diameter;
                    if (spacing >= 75.0) break;
                }
                r.StirrupDiameterMm = stirrupDiameter;
            }

            double stirrupArea = settings.StirrupLegs * UnitConverter.BarArea(r.StirrupDiameterMm);

            foreach (var zone in zones)
            {
                if (zone.Item2 - zone.Item1 <= 1.0) continue;

                ShearResult shear = ShearDesign.Design(zone.Item3, beam.WebWidthMm,
                    r.EffectiveDepthMm, r.BottomSpan.AreaMm2, axialStressMPa, materials,
                    annex.GammaC);

                double spacing = shear.AswPerMetreMm2 > 0
                    ? stirrupArea / shear.AswPerMetreMm2 : shear.MaxSpacingMm;
                spacing = Math.Min(spacing, shear.MaxSpacingMm);
                spacing = RoundDownTo(spacing, 25.0);
                if (spacing < 50.0) spacing = 50.0;

                r.StirrupZones.Add(new BeamStirrupZone
                {
                    StartMm = zone.Item1,
                    EndMm = zone.Item2,
                    SpacingMm = spacing,
                    Label = zone.Item4,
                    DesignShearN = zone.Item3,
                    RequiredAswPerMmMm2 = shear.AswPerMetreMm2
                });

                result.Notes.Add(string.Format("Zone {0} : ", zone.Item4) + shear.Justification
                    + string.Format(" -> cadre HA{0:0} a {1} brins, espacement {2:0} mm",
                        r.StirrupDiameterMm, settings.StirrupLegs, spacing));
            }
        }

        // ------------------------------------------------------------------
        // Verifications
        // ------------------------------------------------------------------

        private static void AddChecks(BeamData beam, BeamDesignSettings settings,
                                      ConcreteProperties materials, BeamReinforcement r,
                                      BeamDesignResult result, BendingResult span,
                                      BendingResult left, BendingResult right,
                                      double asMin, double asMax, LoadCombination combination)
        {
            AddBendingCheck(result, "Flexion en travee", "6.1", span,
                result.SpanSteelRequiredMm2, r.BottomSpan.AreaMm2, combination);

            if (result.LeftSteelRequiredMm2 > 0)
            {
                AddBendingCheck(result, "Flexion sur appui gauche", "6.1", left,
                    result.LeftSteelRequiredMm2, r.TopLeft.AreaMm2, combination);
            }
            if (result.RightSteelRequiredMm2 > 0)
            {
                AddBendingCheck(result, "Flexion sur appui droit", "6.1", right,
                    result.RightSteelRequiredMm2, r.TopRight.AreaMm2, combination);
            }

            var minCheck = new CheckResult
            {
                Code = CodeName,
                Clause = "9.2.1.1 (1)",
                Equation = "As >= As,min = max(0,26 f_ctm/f_yk b_t d ; 0,0013 b_t d)",
                Description = "Section minimale d'armature tendue",
                GoverningCombination = combination.Id
            };
            minCheck.Verify(Quantity.Area(asMin), Quantity.Area(r.BottomSpan.AreaMm2));
            result.Checks.Add(minCheck);

            var maxCheck = new CheckResult
            {
                Code = CodeName,
                Clause = "9.2.1.1 (3)",
                Equation = "As <= 0,04 A_c",
                Description = "Section maximale d'armature",
                GoverningCombination = combination.Id
            };
            maxCheck.Verify(Quantity.Area(result.ProvidedSteelMm2), Quantity.Area(asMax));
            result.Checks.Add(maxCheck);

            // Effort tranchant : la zone la plus sollicitee gouverne.
            foreach (BeamStirrupZone zone in r.StirrupZones)
            {
                double provided = settings.StirrupLegs * UnitConverter.BarArea(r.StirrupDiameterMm)
                                  / zone.SpacingMm;
                var shearCheck = new CheckResult
                {
                    Code = CodeName,
                    Clause = "6.2.3",
                    Equation = "A_sw/s fourni >= A_sw/s requis",
                    Description = "Effort tranchant - zone " + zone.Label,
                    GoverningCombination = combination.Id,
                    Comment = string.Format("V_Ed = {0:0} kN, espacement {1:0} mm",
                        zone.DesignShearN / 1000.0, zone.SpacingMm)
                };
                shearCheck.Verify(new Quantity(zone.RequiredAswPerMmMm2, "mm2/mm", 3),
                                  new Quantity(provided, "mm2/mm", 3));
                result.Checks.Add(shearCheck);

                var spacingCheck = new CheckResult
                {
                    Code = CodeName,
                    Clause = "9.2.2 (6)",
                    Equation = "s <= 0,75 d",
                    Description = "Espacement des cadres - zone " + zone.Label,
                    GoverningCombination = combination.Id
                };
                spacingCheck.Verify(Quantity.Length(zone.SpacingMm),
                                    Quantity.Length(0.75 * r.EffectiveDepthMm));
                result.Checks.Add(spacingCheck);
            }
        }

        private static void AddBendingCheck(BeamDesignResult result, string description,
                                            string clause, BendingResult bending,
                                            double requiredMm2, double providedMm2,
                                            LoadCombination combination)
        {
            var check = new CheckResult
            {
                Code = CodeName,
                Clause = clause,
                Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                Description = description,
                GoverningCombination = combination.Id,
                Comment = string.Format("x/d = {0:0.000}, z = {1:0} mm{2}",
                    bending.NeutralAxisRatio, bending.LeverArmMm,
                    bending.NeedsCompressionSteel ? ", aciers comprimes necessaires" : "")
            };
            check.WithInput("mu", Quantity.Ratio(bending.Mu))
                 .WithInput("mu_lim", Quantity.Ratio(bending.MuLimit));
            check.Verify(Quantity.Area(requiredMm2), Quantity.Area(providedMm2));
            result.Checks.Add(check);
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static LoadCombination ResolveCombination(IReadOnlyList<LoadCombination> combinations,
                                                          BeamDesignSettings settings, BeamData beam)
        {
            if (combinations != null)
            {
                foreach (LoadCombination candidate in combinations)
                {
                    if (candidate.IsUltimate && candidate.Stations.Count > 0) return candidate;
                }
            }

            // Trois stations : appui gauche, mi-portee, appui droit. Les six composantes
            // restent solidaires en chaque point.
            var combination = new LoadCombination("ULS-COMB-001", DesignSituation.UltimateFundamental);
            double axial = UnitConverter.KnToN(settings.AxialForceKn);
            combination.Stations.Add(new ForceStation(0.0, new InternalForces(
                axial, 0.0, UnitConverter.KnToN(settings.LeftShearKn), 0.0,
                -UnitConverter.KnmToNmm(settings.LeftSupportMomentKnm), 0.0)));
            combination.Stations.Add(new ForceStation(beam.SpanMm / 2.0, new InternalForces(
                axial, 0.0, 0.0, 0.0, UnitConverter.KnmToNmm(settings.SpanMomentKnm), 0.0)));
            combination.Stations.Add(new ForceStation(beam.SpanMm, new InternalForces(
                axial, 0.0, -UnitConverter.KnToN(settings.RightShearKn), 0.0,
                -UnitConverter.KnmToNmm(settings.RightSupportMomentKnm), 0.0)));
            return combination;
        }

        /// <summary>
        /// Applique les choix de l'ingenieur a la geometrie lue : la dalle n'appartient pas a
        /// l'element poutre dans le modele, c'est donc lui qui declare la table collaborante
        /// et les conditions d'appui.
        /// </summary>
        private static BeamData ApplyUserGeometry(BeamData beam, BeamDesignSettings settings)
        {
            bool asTSection = settings.TreatAsTSection
                              && settings.FlangeWidthMm > beam.WebWidthMm
                              && settings.FlangeThicknessMm > 0;
            if (!asTSection && settings.SpanKind == beam.SpanKind) return beam;

            var copy = new BeamData
            {
                Id = beam.Id,
                Name = beam.Name,
                Mark = beam.Mark,
                Shape = asTSection ? BeamSectionShape.TSection : beam.Shape,
                WebWidthMm = beam.WebWidthMm,
                HeightMm = beam.HeightMm,
                FlangeWidthMm = asTSection ? settings.FlangeWidthMm : beam.FlangeWidthMm,
                FlangeThicknessMm = asTSection ? settings.FlangeThicknessMm : beam.FlangeThicknessMm,
                SpanMm = beam.SpanMm,
                SpanKind = settings.SpanKind
            };
            copy.Remarks.AddRange(beam.Remarks);
            return copy;
        }

        private static CoverResult ResolveCover(BeamDesignSettings settings, double barDiameterMm)
        {
            CoverResult required = ConcreteCover.Compute(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.DesignLife, false,
                settings.SpecialQualityControl);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm. Exigence EC2 4.4.1 : {1:0} mm.",
                settings.CoverMm, required.MinCoverMm + required.AllowanceMm);
            return required;
        }

        private static double MinimumClearSpacing(double barDiameterMm, double aggregateSizeMm,
                                                  INationalAnnex annex)
        {
            // EC2 8.2(2) : max(k1 phi ; d_g + k2 ; 20 mm)
            return Math.Max(annex.ClearSpacingBarFactor * barDiameterMm,
                   Math.Max(aggregateSizeMm + annex.ClearSpacingAggregateAdditionMm,
                            annex.ClearSpacingMinimumMm));
        }

        private static BarSelection TwoBars(BarSelection reference)
        {
            double diameter = reference != null && reference.DiameterMm > 0
                ? Math.Min(reference.DiameterMm, 16.0) : 12.0;
            return new BarSelection { DiameterMm = diameter, Count = 2, Layers = 1, BarsPerLayer = 2 };
        }

        private static string MarkPrefix(BeamData beam)
        {
            return string.IsNullOrWhiteSpace(beam.Mark) ? "BM" : beam.Mark;
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
