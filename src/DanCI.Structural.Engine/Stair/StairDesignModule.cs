using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.EC1;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Stair
{
    /// <summary>
    /// DanCI Stair Design : dimensionnement d'une bande de 1 metre de volee d'escalier
    /// droit, selon l'EN 1992-1-1 pour la resistance et l'EN 1991-1-1 pour les actions.
    ///
    /// Une volee est une dalle inclinee qui porte des marches. Trois choses la distinguent
    /// d'une dalle, et ce sont elles qui font le module :
    ///
    /// 1. SON POIDS PROPRE n'est pas gamma t. La paillasse est mesuree perpendiculairement
    ///    a la pente et les marches pesent gamma R / 2 : 42 % de plus sur la volee de
    ///    reference, toujours du cote non securitaire.
    /// 2. SA PORTEE porte deux charges differentes, celle de la volee et celle du palier.
    ///    Le calcul exact est ferme et le moteur le fait.
    /// 3. SON NOEUD volee-palier est un angle rentrant tendu. Une barre qui suivrait le pli
    ///    ferait sauter l'enrobage. Le moteur croise les barres et le dit.
    /// </summary>
    public sealed class StairDesignModule
        : IDesignModule<StairData, StairDesignSettings, StairDesignResult>
    {
        private const string Ec0 = "EN 1990:2002";
        private const string Ec1 = "EN 1991-1-1:2002";
        private const string Ec2 = "EN 1992-1-1:2004";

        private const double AssumedDiameterMm = 12.0;

        /// <summary>Pente au-dela de laquelle une volee courante devient inconfortable.</summary>
        private const double SteepSlopeDegrees = 35.0;

        public string Name { get { return "DanCI Stair Design"; } }

        public StairDesignResult Design(StairData stair, StairDesignSettings settings,
                                        IReadOnlyList<LoadCombination> combinations)
        {
            StairDesignResult result = DesignOnce(stair, settings, AssumedDiameterMm);

            if (result.IsValid && result.Reinforcement.BottomMain.DiameterMm > 0
                && Math.Abs(result.Reinforcement.BottomMain.DiameterMm - AssumedDiameterMm) > 0.1)
            {
                StairDesignResult refined = DesignOnce(stair, settings,
                    result.Reinforcement.BottomMain.DiameterMm);
                if (refined.IsValid) return refined;
            }

            return result;
        }

        private static StairDesignResult DesignOnce(StairData stair, StairDesignSettings settings,
                                                    double assumedDiameterMm)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new StairDesignResult
            {
                Stair = stair,
                IsValid = false,
                CodeLabel = Ec2 + ", " + Ec1 + " et " + Ec0 + " - " + annex.Name
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);
            result.Notes.Add("Calcul mene sur une bande de 1 000 mm de largeur de volee.");

            CheckGeometry(stair, result);

            // --- Enrobage et hauteur utile ---
            CoverResult cover = ResolveCover(settings, assumedDiameterMm);
            result.Notes.Add(cover.Justification);

            var r = new StairReinforcement { CoverMm = cover.NominalCoverMm };

            // La hauteur utile se mesure sur l'epaisseur de paillasse, qui est deja
            // perpendiculaire a la pente : aucune correction supplementaire.
            r.EffectiveDepthMm = stair.WaistThicknessMm - r.CoverMm - assumedDiameterMm / 2.0;
            if (r.EffectiveDepthMm <= 0)
            {
                result.Warnings.Add("La paillasse est trop mince pour l'enrobage demande.");
                return result;
            }

            // --- Charges et sollicitations ---
            ResolveActions(stair, settings, result);

            double spanMoment = UnitConverter.KnmToNmm(result.SpanMomentKnmPerM);
            double supportMoment = UnitConverter.KnmToNmm(result.SupportMomentKnmPerM);
            double shear = UnitConverter.KnToN(result.ShearKnPerM);

            // --- Flexion ---
            const double b = StairData.StripWidthMm;
            string minJustification;
            double asMin = BendingDesign.MinimumTensionSteel(b, r.EffectiveDepthMm, materials,
                                                             out minJustification);
            result.Notes.Add(minJustification);

            BendingResult spanBending = BendingDesign.Rectangular(spanMoment, b,
                r.EffectiveDepthMm, r.CoverMm, materials);
            BendingResult supportBending = BendingDesign.Rectangular(supportMoment, b,
                r.EffectiveDepthMm, r.CoverMm, materials);

            result.SpanSteelRequiredMm2PerM = spanMoment > 0
                ? Math.Max(spanBending.TensionSteelMm2, asMin) : 0.0;
            result.SupportSteelRequiredMm2PerM = supportMoment > 0
                ? Math.Max(supportBending.TensionSteelMm2, asMin) : 0.0;

            result.Notes.Add(
                "Le moment est calcule sur la projection horizontale, avec des charges " +
                "ramenees au metre carre de projection, puis applique a la section de " +
                "paillasse dont la hauteur utile est mesuree perpendiculairement a la pente. " +
                "La composante reellement portee par la section vaut M cos alpha : retenir M " +
                "entier est securitaire, et c'est ce que fait le moteur.");

            // --- Nappes ---
            double maxMainSpacing = Math.Min(3.0 * stair.WaistThicknessMm, 400.0);
            double maxTransverseSpacing = Math.Min(3.5 * stair.WaistThicknessMm, 450.0);
            var optimizer = new MeshOptimizer(settings.AutoMeshDiameter
                ? (double[])null : new[] { settings.ForcedMeshDiameterMm });

            r.BottomMain = optimizer.Select(Math.Max(result.SpanSteelRequiredMm2PerM, asMin),
                                            maxMainSpacing);
            if (r.BottomMain == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : aucune nappe courante ne fournit l'acier requis " +
                    "en travee. Actions possibles : epaissir la paillasse, reduire la portee " +
                    "en portant les paliers, ou augmenter la classe de beton.");
                return result;
            }

            r.BottomTransverse = optimizer.Select(0.2 * r.BottomMain.AreaPerMetreMm2,
                                                  maxTransverseSpacing);
            if (r.BottomTransverse == null)
            {
                result.Warnings.Add("Aucune armature de repartition constructible n'a ete trouvee.");
                return result;
            }

            if (result.SupportSteelRequiredMm2PerM > 0 || settings.TopReinforcement)
            {
                double topRequired = Math.Max(result.SupportSteelRequiredMm2PerM, asMin);
                r.TopMain = optimizer.Select(topRequired, maxMainSpacing);
                r.TopTransverse = r.TopMain != null
                    ? optimizer.Select(0.2 * r.TopMain.AreaPerMetreMm2, maxTransverseSpacing)
                    : null;
                if (r.TopMain == null || r.TopTransverse == null)
                {
                    result.Warnings.Add("SECTION INSUFFISANTE sur appui : aucune nappe ne convient.");
                    return result;
                }
            }

            r.EffectiveDepthMm = stair.WaistThicknessMm - r.CoverMm - r.BottomMain.DiameterMm / 2.0;

            // --- Ancrages ---
            AnchorageResult anchorage = Anchorage.Compute(r.BottomMain.DiameterMm, materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            r.KneeAnchorageMm = r.AnchorageLengthMm;
            result.Notes.Add(anchorage.Justification);

            if (r.HasTopReinforcement)
            {
                r.TopBarLengthMm = RoundUpTo(
                    Math.Max(stair.SpanMm / 4.0, r.AnchorageLengthMm), 50.0);
                result.Notes.Add(string.Format(
                    "Chapeaux : {0}, longueur {1:0} mm depuis le nu d'appui (max(L/4 ; l_bd)). " +
                    "Une volee declaree isostatique est en realite toujours partiellement " +
                    "encastree dans ses paliers : sans chapeaux, la fissuration se declare en " +
                    "face superieure des appuis. Cette longueur releve de la pratique courante, " +
                    "pas d'un article de l'EC2.",
                    r.TopMain.Label, r.TopBarLengthMm));
            }

            ResolveKneeJoint(stair, r, result);

            AddBendingChecks(stair, r, result, asMin, spanBending, materials);
            AddDetailingChecks(stair, r, result, maxMainSpacing, maxTransverseSpacing);
            AddShearCheck(stair, r, materials, annex, shear, result);
            AddDeflectionCheck(stair, r, settings, result);
            AddKneeJointCheck(stair, r, result);

            result.Reinforcement = r;
            result.Plan = StairPlanBuilder.Build(stair, r, MarkPrefix(stair));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        private static void CheckGeometry(StairData stair, StairDesignResult result)
        {
            if (stair.RiserCount < 2)
            {
                result.Warnings.Add(
                    "Une volee de moins de deux contremarches n'a pas de giron : la geometrie " +
                    "ne permet aucun calcul de portee.");
                return;
            }

            result.Notes.Add(string.Format(
                "Geometrie : {0} contremarches de {1:0} mm, {2} girons de {3:0} mm, " +
                "denivele {4:0} mm, projection horizontale {5:0} mm, pente {6:0.0} degres " +
                "(cos alpha = {7:0.0000}).",
                stair.RiserCount, stair.RiserHeightMm, stair.RiserCount - 1, stair.TreadDepthMm,
                stair.TotalRiseMm, stair.TotalGoingMm, stair.SlopeAngleDegrees,
                stair.SlopeCosine));

            // Blondel : regle de confort, pas de resistance. Elle est rendue pour ce
            // qu'elle est, et son non-respect ne fait echouer aucune verification.
            double blondel = stair.BlondelValueMm;
            string comfort = blondel >= 600.0 && blondel <= 650.0
                ? "dans la plage confortable"
                : "HORS de la plage confortable de 600 a 650 mm";
            result.Notes.Add(string.Format(
                "Formule de Blondel : 2R + G = {0:0} mm, {1}. C'est une regle d'ERGONOMIE, " +
                "elle ne releve d'aucun Eurocode et n'entre dans aucune verification de " +
                "resistance. Les hauteurs et girons admissibles relevent par ailleurs de la " +
                "reglementation de construction nationale, que le moteur ne connait pas.",
                blondel, comfort));

            if (stair.SlopeAngleDegrees > SteepSlopeDegrees)
            {
                result.Warnings.Add(string.Format(
                    "La pente atteint {0:0.0} degres. Au-dela de {1:0} degres une volee " +
                    "courante devient inconfortable et releve souvent d'un escalier de " +
                    "service : verifiez la reglementation applicable, que le moteur " +
                    "n'applique pas.",
                    stair.SlopeAngleDegrees, SteepSlopeDegrees));
            }

            if (stair.SpanKind == StairSpanKind.AlongFlightWithLanding
                && stair.LandingSpanMm <= 0)
            {
                result.Warnings.Add(
                    "Le mode d'appui declare fait participer le palier a la portee, mais " +
                    "aucune longueur de palier n'est renseignee. La portee se reduit alors a " +
                    "la volee seule : verifiez le mode d'appui.");
            }
        }

        // ------------------------------------------------------------------
        // Charges et sollicitations
        // ------------------------------------------------------------------

        private static void ResolveActions(StairData stair, StairDesignSettings settings,
                                           StairDesignResult result)
        {
            if (settings.MomentSource == StairMomentSource.Entered)
            {
                result.SpanMomentKnmPerM = Math.Abs(settings.SpanMomentKnmPerM);
                result.SupportMomentKnmPerM = Math.Abs(settings.SupportMomentKnmPerM);
                result.ShearKnPerM = Math.Abs(settings.ShearKnPerM);
                result.Notes.Add(
                    "Sollicitations saisies par l'utilisateur : elles proviennent d'une " +
                    "analyse exterieure et ne sont pas recalculees.");
                return;
            }

            StairLoadBreakdown flight = StairActions.FlightSelfWeight(
                settings.IncludeSelfWeight ? stair.WaistThicknessMm : 0.0,
                settings.IncludeSelfWeight ? stair.RiserHeightMm : 0.0,
                stair.SlopeCosine, settings.ConcreteUnitWeightKnM3,
                settings.TreadFinishKnM2, settings.SoffitFinishKnM2);

            StairLoadBreakdown landing = StairActions.LandingSelfWeight(
                settings.IncludeSelfWeight ? stair.LandingThicknessMm : 0.0,
                settings.ConcreteUnitWeightKnM3,
                settings.TreadFinishKnM2, settings.SoffitFinishKnM2);

            result.FlightLoad = flight;
            result.LandingLoad = landing;
            result.Notes.Add(flight.Justification);
            result.Notes.Add(landing.Justification);
            result.Notes.Add(StairActions.CategoryRule);
            result.Notes.Add(StairActions.ConcentratedLoadReminder);

            double error = StairActions.NaiveSelfWeightErrorPercent(stair.WaistThicknessMm,
                stair.RiserHeightMm, stair.SlopeCosine, settings.ConcreteUnitWeightKnM3);
            result.Notes.Add(string.Format(
                "Prendre gamma t comme poids propre, sans corriger la pente ni compter les " +
                "marches, aurait sous-estime le poids propre de la volee de {0:0.0} %.", error));

            double q = settings.VariableLoadKnM2;
            result.FlightUltimateLoadKnM2 = ActionCombinations.Ultimate(flight.PermanentKnM2, q);
            result.LandingUltimateLoadKnM2 = ActionCombinations.Ultimate(landing.PermanentKnM2, q);
            result.Notes.Add("Volee : "
                + ActionCombinations.Describe(flight.PermanentKnM2, q, settings.Category));

            double spanM = stair.SpanMm / 1000.0;
            double flightM = Math.Min(stair.TotalGoingMm / 1000.0, spanM);

            if (stair.SpanKind == StairSpanKind.TransverseBetweenWalls)
            {
                // La volee porte en travers : la charge de volee regne sur toute la portee,
                // le palier ne participe pas.
                double w = result.FlightUltimateLoadKnM2;
                result.SpanMomentKnmPerM = w * spanM * spanM / 8.0;
                result.ShearKnPerM = w * spanM / 2.0;
                result.Notes.Add(
                    "Portee transversale : la volee franchit sa largeur entre deux limons ou " +
                    "deux voiles. M = w l2 / 8 et V = w l / 2, par la statique seule, sous la " +
                    "seule charge de volee. Les armatures principales sont perpendiculaires a " +
                    "la montee et le pli volee-palier ne les concerne pas.");
            }
            else
            {
                StairStaticsResult statics = StairStatics.Solve(spanM, flightM,
                    result.FlightUltimateLoadKnM2, result.LandingUltimateLoadKnM2);
                result.Statics = statics;
                result.SpanMomentKnmPerM = statics.SpanMomentKnmPerM;
                result.ShearKnPerM = statics.ShearKnPerM;
                result.Notes.Add(statics.Justification);

                if (statics.UniformFlightLoadMomentKnmPerM > statics.SpanMomentKnmPerM * 1.001)
                {
                    result.Notes.Add(string.Format(
                        "Etaler la charge de volee sur toute la portee aurait donne " +
                        "{0:0.00} kN.m/m au lieu de {1:0.00}, soit {2:0.0} % de plus : " +
                        "securitaire, mais faux. Le moteur retient le calcul exact.",
                        statics.UniformFlightLoadMomentKnmPerM, statics.SpanMomentKnmPerM,
                        (statics.UniformFlightLoadMomentKnmPerM / statics.SpanMomentKnmPerM - 1.0)
                        * 100.0));
                }
            }

            // Une volee isostatique reste partiellement encastree : le moment sur appui
            // n'est pas calcule, il est couvert par les chapeaux constructifs.
            result.SupportMomentKnmPerM = Math.Abs(settings.SupportMomentKnmPerM);
        }

        // ------------------------------------------------------------------
        // Le noeud volee-palier
        // ------------------------------------------------------------------

        private static void ResolveKneeJoint(StairData stair, StairReinforcement r,
                                             StairDesignResult result)
        {
            bool hasJoint = stair.SpanKind == StairSpanKind.AlongFlightWithLanding
                            && stair.LandingSpanMm > 0
                            && stair.RiserCount > 1;

            if (!hasJoint)
            {
                r.KneeJoint = KneeJointKind.None;
                if (stair.SpanKind == StairSpanKind.TransverseBetweenWalls)
                {
                    result.Notes.Add(
                        "Portee transversale : les armatures principales ne traversent pas le " +
                        "pli volee-palier, il n'y a donc pas de noeud a ferrailler dans le sens " +
                        "porteur.");
                }
                return;
            }

            r.KneeJoint = KneeJointKind.ReentrantInTension;
            result.Notes.Add(
                "NOEUD VOLEE-PALIER. La sous-face y forme un angle RENTRANT, et la nappe " +
                "inferieure y est tendue. Une barre qui suivrait le pli developperait a " +
                "l'interieur du coude une resultante dirigee vers l'exterieur du beton : elle " +
                "ferait sauter l'enrobage et le noeud cederait avant la section courante. " +
                "Les deux nappes inferieures sont donc CROISEES : celle de la volee s'ancre " +
                "dans la face superieure du palier, celle du palier dans la face superieure de " +
                "la paillasse, sur " + string.Format("{0:0}", r.KneeAnchorageMm) + " mm " +
                "au-dela du pli. Le moteur ne propose aucune variante suivant le pli.");
            result.Warnings.Add(
                "L'EFFICACITE DU NOEUD N'EST PAS CALCULEE. L'EN 1992-1-1 ne donne aucun " +
                "article propre aux noeuds d'escalier : leur justification releve du modele " +
                "bielles-tirants des articles 5.6.4 et 6.5, que le moteur ne construit pas. " +
                "Le detail pose est celui de la pratique etablie, la longueur d'ancrage est " +
                "verifiee, mais le rendement du noeud reste a la charge de l'ingenieur.");
        }

        private static void AddKneeJointCheck(StairData stair, StairReinforcement r,
                                              StairDesignResult result)
        {
            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "8.4.4 et 5.6.4",
                Equation = "longueur disponible au-dela du pli >= l_bd",
                Description = "Ancrage des barres croisees au noeud volee-palier",
                GoverningCombination = "ULS-6.10"
            };

            if (!r.HasKneeJoint)
            {
                check.Status = CheckStatus.NotApplicable;
                check.Comment = "Aucun noeud volee-palier dans le sens porteur : la volee " +
                                "porte sans palier, ou porte transversalement. La " +
                                "verification est sans objet, et le dire vaut mieux que de " +
                                "l'omettre.";
                result.Checks.Add(check);
                return;
            }

            // Cote palier, la barre de volee dispose de la longueur du palier ; cote volee,
            // la barre de palier dispose de la projection de la paillasse. La plus courte
            // des deux gouverne.
            double availableOnLanding = stair.LandingSpanMm;
            double availableOnFlight = stair.TotalGoingMm * stair.SlopeCosine;
            double available = Math.Min(availableOnLanding, availableOnFlight);

            check.Comment = string.Format(
                "Cote palier {0:0} mm, cote paillasse {1:0} mm en projection : la plus courte " +
                "gouverne. Si elle ne suffit pas, la barre doit etre repliee ou le palier " +
                "allonge — jamais la longueur d'ancrage reduite.",
                availableOnLanding, availableOnFlight);
            check.Verify(Quantity.Length(r.KneeAnchorageMm), Quantity.Length(available));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(string.Format(
                    "ANCRAGE AU NOEUD INSUFFISANT : il faut {0:0} mm au-dela du pli et il n'y " +
                    "en a que {1:0}. Actions possibles : allonger le palier, reduire le " +
                    "diametre de la nappe inferieure, ou replier les barres en retour " +
                    "d'equerre — le moteur ne reduit jamais la longueur d'ancrage requise.",
                    r.KneeAnchorageMm, available));
            }
        }

        // ------------------------------------------------------------------
        // Verifications courantes
        // ------------------------------------------------------------------

        private static void AddBendingChecks(StairData stair, StairReinforcement r,
                                             StairDesignResult result, double asMin,
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
                    Comment = string.Format("x/d = {0:0.000}{1}",
                        spanBending.NeutralAxisRatio,
                        result.SpanSteelRequiredMm2PerM > spanBending.TensionSteelMm2
                            ? ", As,min gouverne" : "")
                };
                check.Verify(Quantity.Area(result.SpanSteelRequiredMm2PerM),
                             Quantity.Area(r.BottomMain.AreaPerMetreMm2));
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
                               StairData.StripWidthMm * stair.WaistThicknessMm)));
            result.Checks.Add(maximum);
        }

        private static void AddDetailingChecks(StairData stair, StairReinforcement r,
                                               StairDesignResult result,
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

            var ratio = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (2)",
                Equation = "As,trans >= 0,20 As,principal",
                Description = "Armature de repartition",
                GoverningCombination = "ULS-6.10",
                Comment = "Sur une volee, la repartition diffuse en outre la charge " +
                          "concentree d'une marche vers les bandes voisines."
            };
            ratio.Verify(Quantity.Area(0.2 * r.BottomMain.AreaPerMetreMm2),
                         Quantity.Area(r.BottomTransverse.AreaPerMetreMm2));
            result.Checks.Add(ratio);

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
        }

        private static void AddShearCheck(StairData stair, StairReinforcement r,
                                          ConcreteProperties materials, INationalAnnex annex,
                                          double shearN, StairDesignResult result)
        {
            if (shearN <= 0) return;

            string justification;
            double resistance = ShearDesign.ShearResistanceWithoutReinforcement(
                StairData.StripWidthMm, r.EffectiveDepthMm,
                r.BottomMain.AreaPerMetreMm2, 0.0, materials, annex.GammaC, out justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.2",
                Equation = "V_Ed <= V_Rd,c",
                Description = "Effort tranchant",
                GoverningCombination = "ULS-6.10",
                Comment = "Une volee ne porte pas d'armatures d'effort tranchant : si V_Rd,c " +
                          "est depasse, il faut epaissir la paillasse. " + justification
            };
            check.Verify(Quantity.Force(UnitConverter.NToKn(shearN)),
                         Quantity.Force(UnitConverter.NToKn(resistance)));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE a l'effort tranchant. Une volee ne se rattrape pas " +
                    "avec des cadres : epaissir la paillasse, ou porter les paliers pour " +
                    "raccourcir la portee.");
            }
        }

        private static void AddDeflectionCheck(StairData stair, StairReinforcement r,
                                               StairDesignSettings settings,
                                               StairDesignResult result)
        {
            if (stair.SpanMm <= 0 || result.SpanSteelRequiredMm2PerM <= 0) return;

            DeflectionResult deflection = Deflection.Check(stair.SpanMm, r.EffectiveDepthMm,
                StairData.StripWidthMm, result.SpanSteelRequiredMm2PerM,
                r.BottomMain.AreaPerMetreMm2, 0.0,
                StructuralSystem.SimplySupported, settings.ConcreteStrengthMPa, 1.0,
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
                Comment = "C'est le critere qui gouverne une volee courante. Il est applique " +
                          "SANS majoration : la tolerance de 15 % souvent accordee aux " +
                          "escaliers vient de la BS 8110, elle n'existe pas dans " +
                          "l'EN 1992-1-1 et le moteur ne l'applique pas."
            };
            check.Verify(Quantity.Ratio(deflection.ActualRatio),
                         Quantity.Ratio(deflection.AllowableRatio));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(string.Format(
                    "FLECHE : l/d = {0:0.0} depasse la limite de {1:0.0}. C'est le critere qui " +
                    "gouverne une volee. Actions possibles : epaissir la paillasse (la plus " +
                    "efficace), porter les paliers pour reduire la portee, ou mener le calcul " +
                    "detaille de l'article 7.4.3, que le moteur ne fait pas.",
                    deflection.ActualRatio, deflection.AllowableRatio));
            }
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static CoverResult ResolveCover(StairDesignSettings settings, double barDiameterMm)
        {
            CoverResult required = ConcreteCover.Compute(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.DesignLife, true, false);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm.", settings.CoverMm);
            return required;
        }

        private static string MarkPrefix(StairData stair)
        {
            return string.IsNullOrWhiteSpace(stair.Mark) ? "EC" : stair.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
