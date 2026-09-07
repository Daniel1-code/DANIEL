using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Slab
{
    /// <summary>
    /// DanCI Slab Design : dimensionnement d'une bande de 1 metre de dalle pleine portant
    /// dans un sens, selon l'EN 1992-1-1.
    ///
    /// Une dalle se distingue d'une poutre sur trois points, et ce sont eux qui font le
    /// module : elle ne porte jamais d'armatures d'effort tranchant, elle est presque
    /// toujours pilotee par la **fleche** et non par la resistance, et elle exige une
    /// armature de repartition perpendiculaire a la portee.
    /// </summary>
    public sealed class SlabDesignModule
        : IDesignModule<SlabData, SlabDesignSettings, SlabDesignResult>
    {
        private const string Ec0 = "EN 1990:2002";
        private const string Ec2 = "EN 1992-1-1:2004";

        /// <summary>Diametre suppose au premier passage, avant de connaitre la nappe.</summary>
        private const double AssumedDiameterMm = 10.0;

        public string Name { get { return "DanCI Slab Design"; } }

        public SlabDesignResult Design(SlabData slab, SlabDesignSettings settings,
                                       IReadOnlyList<LoadCombination> combinations)
        {
            SlabDesignResult result = DesignOnce(slab, settings, AssumedDiameterMm);

            // La hauteur utile depend du diametre retenu, qui depend de la hauteur utile.
            // Un second passage suffit a lever la circularite ; s'il ne converge pas, la
            // solution du premier passage reste valable, elle est simplement plus prudente.
            if (result.IsValid && result.Reinforcement.BottomMain.DiameterMm > 0
                && Math.Abs(result.Reinforcement.BottomMain.DiameterMm - AssumedDiameterMm) > 0.1)
            {
                SlabDesignResult refined = DesignOnce(slab, settings,
                    result.Reinforcement.BottomMain.DiameterMm);
                if (refined.IsValid) return refined;
            }

            return result;
        }

        private static SlabDesignResult DesignOnce(SlabData slab, SlabDesignSettings settings,
                                                   double assumedDiameterMm)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new SlabDesignResult
            {
                Slab = slab,
                IsValid = false,
                CodeLabel = Ec2 + " et " + Ec0 + " - " + annex.Name
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);
            result.Notes.Add("Calcul mene sur une bande de 1 000 mm de largeur.");

            CheckGeometry(slab, result);

            // --- Enrobage ---
            CoverResult cover = ResolveCover(settings, assumedDiameterMm);
            result.Notes.Add(cover.Justification);

            var r = new SlabReinforcement { CoverMm = cover.NominalCoverMm };
            r.EffectiveDepthMm = slab.ThicknessMm - r.CoverMm - assumedDiameterMm / 2.0;
            r.TopEffectiveDepthMm = r.EffectiveDepthMm;
            if (r.EffectiveDepthMm <= 0)
            {
                result.Warnings.Add("La dalle est trop mince pour l'enrobage demande.");
                return result;
            }

            // --- Charges et moments ---
            ResolveActions(slab, settings, result);

            double spanMoment = UnitConverter.KnmToNmm(result.SpanMomentKnmPerM);
            double supportMoment = UnitConverter.KnmToNmm(result.SupportMomentKnmPerM);
            double shear = UnitConverter.KnToN(result.ShearKnPerM);

            // --- Flexion, sur une bande de 1 000 mm ---
            const double b = SlabData.StripWidthMm;
            string minJustification;
            double asMin = BendingDesign.MinimumTensionSteel(b, r.EffectiveDepthMm, materials,
                                                             out minJustification);

            BendingResult spanBending = BendingDesign.Rectangular(spanMoment, b,
                r.EffectiveDepthMm, r.CoverMm, materials);
            BendingResult supportBending = BendingDesign.Rectangular(supportMoment, b,
                r.TopEffectiveDepthMm, r.CoverMm, materials);

            result.SpanSteelRequiredMm2PerM = spanMoment > 0
                ? Math.Max(spanBending.TensionSteelMm2, asMin) : 0.0;
            result.SupportSteelRequiredMm2PerM = supportMoment > 0
                ? Math.Max(supportBending.TensionSteelMm2, asMin) : 0.0;

            result.Notes.Add(minJustification);
            if (spanMoment > 0)
            {
                result.Notes.Add(string.Format(
                    "Travee : M_Ed = {0:0.0} kN.m/m -> x/d = {1:0.000}, z = {2:0} mm, " +
                    "As = {3:0} mm2/m{4}",
                    result.SpanMomentKnmPerM, spanBending.NeutralAxisRatio, spanBending.LeverArmMm,
                    result.SpanSteelRequiredMm2PerM,
                    result.SpanSteelRequiredMm2PerM > spanBending.TensionSteelMm2
                        ? " (As,min gouverne)" : ""));
            }

            // --- Choix des nappes ---
            // EC2 9.3.1.1(3) : s_max,slabs = min(3h ; 400) pour les armatures principales,
            // min(3,5h ; 450) pour les armatures de repartition.
            double maxMainSpacing = Math.Min(3.0 * slab.ThicknessMm, 400.0);
            double maxTransverseSpacing = Math.Min(3.5 * slab.ThicknessMm, 450.0);
            var optimizer = new MeshOptimizer(settings.AutoMeshDiameter
                ? (double[])null : new[] { settings.ForcedMeshDiameterMm });

            r.BottomMain = optimizer.Select(Math.Max(result.SpanSteelRequiredMm2PerM, asMin),
                                            maxMainSpacing);
            if (r.BottomMain == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : aucune nappe courante ne fournit l'acier requis " +
                    "en travee. Actions possibles : epaissir la dalle, reduire la portee, " +
                    "augmenter la classe de beton, ou autoriser un diametre superieur.");
                return result;
            }

            // EC2 9.3.1.1(2) : armature de repartition >= 20 % de l'armature principale.
            double transverseRequired = 0.2 * r.BottomMain.AreaPerMetreMm2;
            r.BottomTransverse = optimizer.Select(transverseRequired, maxTransverseSpacing);
            if (r.BottomTransverse == null)
            {
                result.Warnings.Add("Aucune armature de repartition constructible n'a ete trouvee.");
                return result;
            }

            bool needsTop = result.SupportSteelRequiredMm2PerM > 0 || settings.TopReinforcement;
            if (needsTop)
            {
                double topRequired = Math.Max(result.SupportSteelRequiredMm2PerM, asMin);
                r.TopMain = optimizer.Select(topRequired, maxMainSpacing);
                r.TopTransverse = r.TopMain != null
                    ? optimizer.Select(0.2 * r.TopMain.AreaPerMetreMm2, maxTransverseSpacing)
                    : null;
                if (r.TopMain == null || r.TopTransverse == null)
                {
                    result.Warnings.Add(
                        "SECTION INSUFFISANTE sur appui : aucune nappe courante ne convient.");
                    return result;
                }
            }

            // Hauteurs utiles reelles, une fois les diametres connus.
            r.EffectiveDepthMm = slab.ThicknessMm - r.CoverMm - r.BottomMain.DiameterMm / 2.0;
            r.TopEffectiveDepthMm = r.HasTopReinforcement
                ? slab.ThicknessMm - r.CoverMm - r.TopMain.DiameterMm / 2.0
                : r.EffectiveDepthMm;

            AddBendingChecks(slab, r, result, asMin, spanBending, materials);
            AddDetailingChecks(slab, r, result, maxMainSpacing, maxTransverseSpacing);
            AddShearCheck(slab, r, materials, annex, shear, result);
            AddDeflectionCheck(slab, r, settings, result);
            AddCrackingCheck(slab, r, settings, materials, result);

            // --- Ancrages, recouvrements et chapeaux ---
            AnchorageResult anchorage = Anchorage.Compute(r.BottomMain.DiameterMm, materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            if (r.HasTopReinforcement)
            {
                // Pratique courante : le chapeau couvre le quart de la portee depuis le nu
                // d'appui, sans descendre sous la longueur d'ancrage.
                r.TopBarLengthMm = RoundUpTo(
                    Math.Max(slab.SpanMm / 4.0, r.AnchorageLengthMm), 50.0);
                result.Notes.Add(string.Format(
                    "Chapeaux : {0}, longueur {1:0} mm depuis le nu d'appui (max(L/4 ; l_bd)). " +
                    "Cette longueur releve de la pratique courante, pas d'un article de l'EC2.",
                    r.TopMain.Label, r.TopBarLengthMm));
            }

            result.Reinforcement = r;
            result.Plan = SlabPlanBuilder.Build(slab, r, MarkPrefix(slab));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        private static void CheckGeometry(SlabData slab, SlabDesignResult result)
        {
            if (slab.SpanKind == SlabSpanKind.Cantilever || slab.WidthMm <= 0) return;

            if (slab.SpanIsTheLongDirection)
            {
                result.Warnings.Add(string.Format(
                    "La portee declaree ({0:0} mm) est plus longue que la dimension " +
                    "perpendiculaire ({1:0} mm). Une dalle porte par le plus court chemin : " +
                    "verifiez le sens porteur avant d'utiliser ce resultat.",
                    slab.SpanMm, slab.WidthMm));
            }
            else if (!slab.IsGenuinelyOneWay)
            {
                result.Warnings.Add(string.Format(
                    "Le panneau a un rapport de cotes de {0:0.00} : en dessous de 2, une dalle " +
                    "appuyee sur ses quatre cotes porte dans les deux sens. Le calcul en bande " +
                    "unique surestime alors le ferraillage porteur et sous-estime celui de " +
                    "l'autre direction. Le module ne traite pas encore les dalles bidirectionnelles.",
                    slab.PanelAspectRatio));
            }
        }

        // ------------------------------------------------------------------
        // Charges et sollicitations
        // ------------------------------------------------------------------

        private static void ResolveActions(SlabData slab, SlabDesignSettings settings,
                                           SlabDesignResult result)
        {
            if (settings.MomentSource == SlabMomentSource.Entered)
            {
                result.SpanMomentKnmPerM = Math.Abs(settings.SpanMomentKnmPerM);
                result.SupportMomentKnmPerM = Math.Abs(settings.SupportMomentKnmPerM);
                result.ShearKnPerM = Math.Abs(settings.ShearKnPerM);
                result.Notes.Add(
                    "Sollicitations saisies par l'utilisateur : elles proviennent d'une analyse " +
                    "exterieure et ne sont pas recalculees.");
                return;
            }

            double selfWeight = settings.IncludeSelfWeight
                ? slab.ThicknessMm / 1000.0 * settings.ConcreteUnitWeightKnM3 : 0.0;
            double permanent = settings.PermanentLoadKnM2 + selfWeight;
            double variable = settings.VariableLoadKnM2;

            result.UltimateLoadKnM2 = ActionCombinations.Ultimate(permanent, variable);
            result.QuasiPermanentLoadKnM2 = ActionCombinations.QuasiPermanent(permanent, variable,
                                                                              settings.Category);
            result.Notes.Add(string.Format(
                "Poids propre {0:0.00} kN/m2 + charges permanentes {1:0.00} kN/m2 = " +
                "g = {2:0.00} kN/m2 ; q = {3:0.00} kN/m2. ",
                selfWeight, settings.PermanentLoadKnM2, permanent, variable)
                + ActionCombinations.Describe(permanent, variable, settings.Category));

            double w = result.UltimateLoadKnM2;          // kN/m2, soit kN/m sur une bande de 1 m
            double l = slab.SpanMm / 1000.0;             // m

            switch (slab.SpanKind)
            {
                case SlabSpanKind.Cantilever:
                    // Statique pure : aucun coefficient n'intervient.
                    result.SupportMomentKnmPerM = w * l * l / 2.0;
                    result.ShearKnPerM = w * l;
                    result.Notes.Add(
                        "Console : M = w l2 / 2 et V = w l, par la statique seule.");
                    break;

                case SlabSpanKind.SimplySupported:
                    result.SpanMomentKnmPerM = w * l * l / 8.0;
                    result.ShearKnPerM = w * l / 2.0;
                    result.Notes.Add(
                        "Travee isostatique : M = w l2 / 8 et V = w l / 2, par la statique seule.");
                    break;

                case SlabSpanKind.EndSpan:
                    result.SpanMomentKnmPerM = w * l * l / 11.0;
                    result.SupportMomentKnmPerM = w * l * l / 9.0;
                    result.ShearKnPerM = 0.6 * w * l;
                    result.Notes.Add(
                        "Travee de rive : M_travee = w l2 / 11, M_appui = w l2 / 9, V = 0,60 w l. " +
                        "ATTENTION : ce sont des coefficients de continuite usuels, PAS des " +
                        "valeurs de l'Eurocode 2, qui ne fournit aucun tableau de ce type. " +
                        "Pour une justification complete, saisissez les moments issus d'une " +
                        "analyse de la structure.");
                    result.Warnings.Add(
                        "Moments de continuite estimes par des coefficients usuels, hors " +
                        "Eurocode. A remplacer par les resultats d'une analyse.");
                    break;

                default:
                    result.SpanMomentKnmPerM = w * l * l / 16.0;
                    result.SupportMomentKnmPerM = w * l * l / 12.0;
                    result.ShearKnPerM = 0.5 * w * l;
                    result.Notes.Add(
                        "Travee intermediaire : M_travee = w l2 / 16, M_appui = w l2 / 12, " +
                        "V = 0,50 w l. ATTENTION : coefficients de continuite usuels, PAS des " +
                        "valeurs de l'Eurocode 2.");
                    result.Warnings.Add(
                        "Moments de continuite estimes par des coefficients usuels, hors " +
                        "Eurocode. A remplacer par les resultats d'une analyse.");
                    break;
            }
        }

        // ------------------------------------------------------------------
        // Verifications
        // ------------------------------------------------------------------

        private static void AddBendingChecks(SlabData slab, SlabReinforcement r,
                                             SlabDesignResult result, double asMin,
                                             BendingResult spanBending,
                                             ConcreteProperties materials)
        {
            if (result.SpanMomentKnmPerM > 0)
            {
                var check = new CheckResult
                {
                    Code = Ec2,
                    Clause = "6.1",
                    Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                    Description = "Flexion en travee",
                    GoverningCombination = "ULS-6.10",
                    Comment = string.Format("x/d = {0:0.000}, limite 0,45{1}",
                        spanBending.NeutralAxisRatio,
                        result.SpanSteelRequiredMm2PerM > spanBending.TensionSteelMm2
                            ? ", As,min gouverne" : "")
                };
                check.Verify(Quantity.Area(result.SpanSteelRequiredMm2PerM),
                             Quantity.Area(r.BottomMain.AreaPerMetreMm2));
                result.Checks.Add(check);
            }

            if (result.SupportMomentKnmPerM > 0 && r.HasTopReinforcement)
            {
                var check = new CheckResult
                {
                    Code = Ec2,
                    Clause = "6.1",
                    Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                    Description = "Flexion sur appui",
                    GoverningCombination = "ULS-6.10"
                };
                check.Verify(Quantity.Area(result.SupportSteelRequiredMm2PerM),
                             Quantity.Area(r.TopMain.AreaPerMetreMm2));
                result.Checks.Add(check);
            }

            var minimum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (1) et 9.2.1.1 (1)",
                Equation = "As >= max(0,26 f_ctm/f_yk b d ; 0,0013 b d)",
                Description = "Section minimale d'armature",
                GoverningCombination = "ULS-6.10"
            };
            minimum.Verify(Quantity.Area(asMin), Quantity.Area(r.BottomMain.AreaPerMetreMm2));
            result.Checks.Add(minimum);

            var maximum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.2.1.1 (3)",
                Equation = "As <= 0,04 A_c",
                Description = "Section maximale d'armature",
                GoverningCombination = "ULS-6.10"
            };
            double provided = r.BottomMain.AreaPerMetreMm2
                              + (r.HasTopReinforcement ? r.TopMain.AreaPerMetreMm2 : 0.0);
            maximum.Verify(Quantity.Area(provided),
                           Quantity.Area(BendingDesign.MaximumSteel(
                               SlabData.StripWidthMm * slab.ThicknessMm)));
            result.Checks.Add(maximum);
        }

        private static void AddDetailingChecks(SlabData slab, SlabReinforcement r,
                                               SlabDesignResult result,
                                               double maxMainSpacing, double maxTransverseSpacing)
        {
            var main = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (3)",
                Equation = "s <= min(3h ; 400 mm)",
                Description = "Espacement des armatures principales",
                GoverningCombination = "ULS-6.10"
            };
            main.Verify(Quantity.Length(r.BottomMain.SpacingMm), Quantity.Length(maxMainSpacing));
            result.Checks.Add(main);

            var transverse = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (3)",
                Equation = "s <= min(3,5h ; 450 mm)",
                Description = "Espacement des armatures de repartition",
                GoverningCombination = "ULS-6.10"
            };
            transverse.Verify(Quantity.Length(r.BottomTransverse.SpacingMm),
                              Quantity.Length(maxTransverseSpacing));
            result.Checks.Add(transverse);

            var ratio = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (2)",
                Equation = "As,trans >= 0,20 As,principal",
                Description = "Armature de repartition",
                GoverningCombination = "ULS-6.10",
                Comment = "La repartition n'est pas decorative : elle diffuse les charges " +
                          "concentrees et reprend le retrait perpendiculairement a la portee."
            };
            ratio.Verify(Quantity.Area(0.2 * r.BottomMain.AreaPerMetreMm2),
                         Quantity.Area(r.BottomTransverse.AreaPerMetreMm2));
            result.Checks.Add(ratio);
        }

        private static void AddShearCheck(SlabData slab, SlabReinforcement r,
                                          ConcreteProperties materials, INationalAnnex annex,
                                          double shearN, SlabDesignResult result)
        {
            if (shearN <= 0) return;

            string justification;
            double resistance = ShearDesign.ShearResistanceWithoutReinforcement(
                SlabData.StripWidthMm, r.EffectiveDepthMm,
                r.BottomMain.AreaPerMetreMm2, 0.0, materials, annex.GammaC, out justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.2",
                Equation = "V_Ed <= V_Rd,c",
                Description = "Effort tranchant",
                GoverningCombination = "ULS-6.10",
                Comment = "Une dalle courante ne porte pas d'armatures d'effort tranchant : " +
                          "si V_Rd,c est depasse, il faut epaissir. " + justification
            };
            check.Verify(Quantity.Force(UnitConverter.NToKn(shearN)),
                         Quantity.Force(UnitConverter.NToKn(resistance)));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE a l'effort tranchant. Une dalle ne se rattrape pas " +
                    "avec des cadres : epaissir, ou reduire la portee.");
            }
        }

        private static void AddDeflectionCheck(SlabData slab, SlabReinforcement r,
                                               SlabDesignSettings settings,
                                               SlabDesignResult result)
        {
            if (slab.SpanMm <= 0 || result.SpanSteelRequiredMm2PerM <= 0
                && slab.SpanKind != SlabSpanKind.Cantilever)
            {
                return;
            }

            double required = Math.Max(result.SpanSteelRequiredMm2PerM,
                                       result.SupportSteelRequiredMm2PerM);
            double provided = slab.SpanKind == SlabSpanKind.Cantilever && r.HasTopReinforcement
                ? r.TopMain.AreaPerMetreMm2 : r.BottomMain.AreaPerMetreMm2;

            DeflectionResult deflection = Deflection.Check(slab.SpanMm, r.EffectiveDepthMm,
                SlabData.StripWidthMm, required, provided, 0.0,
                MapSystem(slab.SpanKind), settings.ConcreteStrengthMPa, 1.0,
                settings.SupportsPartitions);
            result.Deflection = deflection;
            result.Notes.Add(deflection.Justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "7.4.2",
                Equation = "l/d <= K [11 + 1,5 sqrt(f_ck) rho_0/rho + ...] x corrections",
                Description = "Fleche par l'elancement limite",
                GoverningCombination = "SLS-QP",
                Comment = "Methode sans calcul explicite : elle vise l/250 sous combinaison " +
                          "quasi-permanente. Si elle est depassee, le calcul detaille de " +
                          "l'article 7.4.3 devient necessaire — le moteur ne le fait pas."
            };
            check.Verify(Quantity.Ratio(deflection.ActualRatio),
                         Quantity.Ratio(deflection.AllowableRatio));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(string.Format(
                    "FLECHE : l/d = {0:0.0} depasse la limite de {1:0.0}. C'est le critere qui " +
                    "gouverne le plus souvent une dalle. Actions possibles : epaissir " +
                    "(la plus efficace), reduire la portee, ou mener le calcul detaille de " +
                    "l'article 7.4.3.",
                    deflection.ActualRatio, deflection.AllowableRatio));
            }
        }

        private static void AddCrackingCheck(SlabData slab, SlabReinforcement r,
                                             SlabDesignSettings settings,
                                             ConcreteProperties materials,
                                             SlabDesignResult result)
        {
            if (result.SpanMomentKnmPerM <= 0 && result.SupportMomentKnmPerM <= 0) return;

            double crackWidth = settings.CrackWidthLimitMm > 0
                ? settings.CrackWidthLimitMm
                : CrackControl.RecommendedCrackWidthMm(settings.Exposure);

            double ultimateMoment = Math.Max(result.SpanMomentKnmPerM,
                                             result.SupportMomentKnmPerM);
            double quasiPermanentMoment = result.UltimateLoadKnM2 > 0
                ? ultimateMoment * result.QuasiPermanentLoadKnM2 / result.UltimateLoadKnM2
                : ultimateMoment * 0.7;

            double required = Math.Max(result.SpanSteelRequiredMm2PerM,
                                       result.SupportSteelRequiredMm2PerM);
            double stress = CrackControl.SteelStress(materials.Fyd, quasiPermanentMoment,
                ultimateMoment, required, r.BottomMain.AreaPerMetreMm2);

            CrackControlResult cracking = CrackControl.Check(stress, crackWidth,
                r.BottomMain.DiameterMm, r.BottomMain.SpacingMm);
            result.Cracking = cracking;
            result.Notes.Add(cracking.Justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "7.3.3 (2)",
                Equation = "phi <= phi_max (tableau 7.2N) OU s <= s_max (tableau 7.3N)",
                Description = "Maitrise de la fissuration sans calcul direct",
                GoverningCombination = "SLS-QP",
                Comment = string.Format(
                    "sigma_s estimee a {0:0} MPa depuis le rapport des combinaisons et des " +
                    "sections, sans analyse en section fissuree : c'est une estimation, pas " +
                    "un calcul de contrainte.", stress)
            };
            check.WithInput("phi", Quantity.Length(r.BottomMain.DiameterMm))
                 .WithInput("phi_max", Quantity.Length(cracking.MaxBarDiameterMm))
                 .WithInput("s", Quantity.Length(r.BottomMain.SpacingMm))
                 .WithInput("s_max", Quantity.Length(cracking.MaxSpacingMm));

            // Le critere satisfait est celui qui compte : l'article en exige un seul.
            if (cracking.SpacingSatisfied)
            {
                check.Verify(Quantity.Length(r.BottomMain.SpacingMm),
                             Quantity.Length(cracking.MaxSpacingMm));
            }
            else
            {
                check.Verify(Quantity.Length(r.BottomMain.DiameterMm),
                             Quantity.Length(cracking.MaxBarDiameterMm));
            }
            result.Checks.Add(check);
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static StructuralSystem MapSystem(SlabSpanKind kind)
        {
            switch (kind)
            {
                case SlabSpanKind.EndSpan: return StructuralSystem.EndSpan;
                case SlabSpanKind.InteriorSpan: return StructuralSystem.InteriorSpan;
                case SlabSpanKind.Cantilever: return StructuralSystem.Cantilever;
                default: return StructuralSystem.SimplySupported;
            }
        }

        private static CoverResult ResolveCover(SlabDesignSettings settings, double barDiameterMm)
        {
            // 4.4.1.2(5) : la geometrie de dalle reduit la classe structurale d'une unite.
            CoverResult required = ConcreteCover.Compute(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.DesignLife, true, false);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm.", settings.CoverMm);
            return required;
        }

        private static string MarkPrefix(SlabData slab)
        {
            return string.IsNullOrWhiteSpace(slab.Mark) ? "DA" : slab.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
