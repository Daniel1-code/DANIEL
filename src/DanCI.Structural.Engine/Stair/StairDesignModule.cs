using System;
using System.Collections.Generic;
using System.Text;
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

            if (!CheckGeometry(stair, result)) return result;
            AddGeometryOriginCheck(stair, result);

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

            bool wantsTopMesh = settings.TopReinforcement
                                || (settings.Detailing != null
                                    && settings.Detailing.ContinuousTopMesh);
            // ART. 9.3.1.2(2). L'encastrement partiel n'est PAS pris en compte dans
            // l'analyse — la volee est calculee isostatique — donc la nappe superieure doit
            // pouvoir reprendre au moins 25 % du moment de travee. Jusqu'ici, quand aucun
            // moment sur appui n'etait declare, les chapeaux etaient poses au seul
            // A_s,min : securitaire par hasard sur une volee courante, insuffisant des que
            // la travee est chargee.
            BendingResult fixityBending = BendingDesign.Rectangular(
                PartialFixity.RequiredSupportMomentNmm(spanMoment), b,
                r.EffectiveDepthMm, r.CoverMm, materials);
            result.PartialFixitySteelMm2PerM = spanMoment > 0
                ? Math.Max(fixityBending.TensionSteelMm2, asMin) : 0.0;

            if (result.SupportSteelRequiredMm2PerM > 0 || wantsTopMesh)
            {
                double topRequired = Math.Max(
                    Math.Max(result.SupportSteelRequiredMm2PerM, asMin),
                    result.PartialFixitySteelMm2PerM);
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
            StairDetailingRules rules = (settings.Detailing ?? new StairDetailingRules()).Clone();
            r.Rules = rules;

            r.AnchorageLengthMm = rules.Round(anchorage.DesignAnchorageMm);
            r.LapLengthMm = rules.Round(anchorage.LapLengthMm);
            result.Notes.Add(anchorage.Justification);

            // LES DECISIONS DE FERRAILLAGE SONT DES PARAMETRES, pas des constantes. Chacune
            // sort avec sa valeur ET sa raison : une longueur sans sa raison n'est pas un
            // parametre, c'est un nombre magique.
            DetailingDecision kneeAnchorage = rules.ResolveKneeAnchorage(
                anchorage.DesignAnchorageMm);
            r.KneeAnchorageMm = kneeAnchorage.ValueMm;
            result.Decisions.Add(kneeAnchorage);

            if (r.HasTopReinforcement)
            {
                DetailingDecision topLength = rules.ResolveTopBarLength(
                    stair.SpanMm, anchorage.DesignAnchorageMm);
                r.TopBarLengthMm = topLength.ValueMm;
                result.Decisions.Add(topLength);

                result.Notes.Add(string.Format(
                    "Chapeaux : {0}, longueur {1:0} mm depuis le nu d'appui. {2} Une volee " +
                    "declaree isostatique est en realite toujours partiellement encastree " +
                    "dans ses paliers : sans chapeaux, la fissuration se declare en face " +
                    "superieure des appuis.",
                    r.TopMain.Label, r.TopBarLengthMm, topLength.Reason));
            }

            ResolveKneeJoint(stair, r, result);

            AddBendingChecks(stair, r, result, asMin, spanBending, materials);
            AddDetailingChecks(stair, r, result, maxMainSpacing, maxTransverseSpacing);
            AddShearCheck(stair, r, materials, annex, shear, result);
            AddDeflectionCheck(stair, r, settings, result);
            AddPartialFixityCheck(stair, r, result);
            AddCrackingCheck(stair, r, settings, materials, result);
            AddConcentratedLoadCheck(settings, result);
            AddKneeJointCheck(stair, r, result);

            result.Reinforcement = r;
            result.Plan = StairPlanBuilder.Build(stair, r, MarkPrefix(stair));

            // Apres la construction du plan : le controle de longueur de barre a besoin des
            // developpes reels.
            AddDetailingDecisions(stair, r, rules, result);

            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        /// <summary>
        /// Controle prealable de la geometrie. Renvoie faux quand le moteur REFUSE de
        /// calculer : une geometrie impossible ou une forme de volee qu'il ne sait pas
        /// traiter ne donne pas lieu a un ferraillage approximatif, elle donne lieu a un
        /// refus motive.
        /// </summary>
        /// <summary>
        /// D'OU VIENT LA GEOMETRIE QU'ON VIENT DE CALCULER.
        ///
        /// Quand l'escalier est deja dessine, c'est le dessin qui decide, et le formulaire
        /// ne sert qu'a ce que le dessin ne porte pas. Encore faut-il savoir lequel des
        /// deux a parle : une valeur par defaut qui survit a la lecture ressemble trait
        /// pour trait a une valeur lue.
        ///
        /// La verification n'est pas un rappel de style. Le MODE D'APPUI et la LONGUEUR DE
        /// PALIER fixent la portee, donc le moment, donc la section d'acier et la fleche :
        /// les supposer, c'est supposer le resultat. L'EPAISSEUR DE PAILLASSE, elle, pilote
        /// tout le poids propre. Ces trois-la sortent en avertissement tant qu'elles n'ont
        /// pas ete etablies ; les autres sont seulement signalees.
        /// </summary>
        private static void AddGeometryOriginCheck(StairData stair, StairDesignResult result)
        {
            StairGeometryProvenance provenance = stair.Provenance
                ?? new StairGeometryProvenance();

            var check = new CheckResult
            {
                Code = "DanCI",
                Clause = "-",
                Equation = "geometrie lue sur l'element dessine",
                Description = "Origine de la geometrie calculee"
            };

            var assumed = new List<StairDimension>(provenance.Assumptions());
            if (assumed.Count == 0)
            {
                check.Status = CheckStatus.Pass;
                check.Comment = "Toute la geometrie a ete lue sur l'element dessine : le " +
                                "calcul porte sur l'escalier du modele, pas sur une saisie.";
                result.Checks.Add(check);
                return;
            }

            var names = new List<string>();
            foreach (StairDimension dimension in assumed)
            {
                names.Add(StairGeometryProvenance.Label(dimension));
            }

            bool spanIsAssumed =
                !provenance.IsEstablished(StairDimension.Support)
                || (stair.SpanKind == StairSpanKind.AlongFlightWithLanding
                    && !provenance.IsEstablished(StairDimension.LandingSpan));
            bool weightIsAssumed = !provenance.IsEstablished(StairDimension.WaistThickness);

            check.Status = spanIsAssumed || weightIsAssumed
                ? CheckStatus.Warning : CheckStatus.Pass;

            var comment = new StringBuilder();
            comment.Append("Valeur(s) non lues sur le modele, donc SUPPOSEES : ");
            comment.Append(string.Join(", ", names.ToArray()));
            comment.Append(".");

            if (spanIsAssumed)
            {
                comment.Append(" Le mode d'appui n'est pas etabli : c'est lui qui fixe la ");
                comment.Append("portee de ");
                comment.Append(string.Format("{0:0} mm", stair.SpanMm));
                comment.Append(" retenue ici, donc le moment et la fleche. Confirmez-le ");
                comment.Append("avant de retenir ce ferraillage.");
            }
            if (weightIsAssumed)
            {
                comment.Append(" L'epaisseur de paillasse n'est pas etablie : elle pilote ");
                comment.Append("tout le poids propre.");
            }

            check.Comment = comment.ToString();
            result.Checks.Add(check);
        }

        private static bool CheckGeometry(StairData stair, StairDesignResult result)
        {
            if (!stair.IsCalculable)
            {
                result.Warnings.Add(
                    "GEOMETRIE INCALCULABLE. Une volee de moins de deux contremarches n'a " +
                    "aucun giron, donc aucune portee ; sans giron, sans epaisseur de " +
                    "paillasse ou sans largeur, il n'y a rien a dimensionner. Aucun " +
                    "ferraillage n'est produit : corrigez la geometrie plutot que de lire un " +
                    "resultat qui n'aurait aucun sens.");
                return false;
            }

            if (stair.Shape == StairFlightShape.Winder || stair.Shape == StairFlightShape.Spiral)
            {
                result.Warnings.Add(string.Format(
                    "VOLEE {0} : LE MOTEUR NE SAIT PAS LA CALCULER. Une volee balancee ou " +
                    "helicoidale porte en flexion ET en torsion, et sa portee n'est pas la " +
                    "projection d'une droite. La traiter comme une volee droite de memes " +
                    "contremarches donnerait un resultat d'apparence normale et faux. Aucun " +
                    "ferraillage n'est produit. Ces volees relevent d'une analyse par " +
                    "elements finis ou d'un modele de poutre helicoidale, que le moteur ne " +
                    "fait pas.",
                    stair.Shape == StairFlightShape.Winder ? "BALANCEE" : "HELICOIDALE"));
                return false;
            }

            if (stair.Shape == StairFlightShape.Undetermined)
            {
                result.Warnings.Add(
                    "La forme de la volee n'a pas pu etre determinee a la lecture du modele. " +
                    "Le calcul est mene comme pour une volee DROITE : verifiez que c'en est " +
                    "bien une avant d'utiliser ce resultat. Le moteur ne sait pas traiter les " +
                    "volees balancees ni helicoidales.");
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

            return true;
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

            result.QuasiPermanentLoadKnM2 = ActionCombinations.QuasiPermanent(
                flight.PermanentKnM2, q, settings.Category);

            ResolveConcentratedLoad(stair, settings, result, flight, landing, spanM, flightM);

            // Une volee isostatique reste partiellement encastree : le moment sur appui
            // n'est pas calcule, il est couvert par les chapeaux constructifs.
            result.SupportMomentKnmPerM = Math.Abs(settings.SupportMomentKnmPerM);
        }

        /// <summary>
        /// Situation ALTERNATIVE de l'EN 1991-1-1 6.3.1.2(1) : charge concentree Q_k au lieu
        /// de la charge repartie q_k. Les deux situations sont evaluees et la plus
        /// defavorable est retenue.
        ///
        /// Jusqu'a la 3.8.0 le moteur se contentait de rappeler l'existence de Q_k sans la
        /// verifier, en affirmant que la charge repartie gouverne une volee courante. C'etait
        /// vrai, mais c'etait une hypothese non verifiee — exactement ce que ce projet
        /// s'interdit ailleurs.
        /// </summary>
        private static void ResolveConcentratedLoad(StairData stair, StairDesignSettings settings,
                                                    StairDesignResult result,
                                                    StairLoadBreakdown flight,
                                                    StairLoadBreakdown landing,
                                                    double spanM, double flightM)
        {
            if (settings.ConcentratedLoadKn <= 0 || spanM <= 0)
            {
                result.Notes.Add(
                    "Aucune charge concentree Q_k n'est declaree : la situation alternative de " +
                    "l'article 6.3.1.2(1) n'est pas evaluee. Renseignez Q_k pour qu'elle le soit.");
                return;
            }

            // Situation alternative : permanentes ponderees SEULES, plus Q_k ponderee.
            // La charge repartie q_k ne s'y ajoute pas, c'est l'une OU l'autre.
            double gammaG = ActionCombinations.GammaGSup;
            double gammaQ = ActionCombinations.GammaQ;

            double permanentMoment;
            if (stair.SpanKind == StairSpanKind.TransverseBetweenWalls)
            {
                double w = gammaG * flight.PermanentKnM2;
                permanentMoment = w * spanM * spanM / 8.0;
            }
            else
            {
                StairStaticsResult permanentOnly = StairStatics.Solve(spanM, flightM,
                    gammaG * flight.PermanentKnM2, gammaG * landing.PermanentKnM2);
                permanentMoment = permanentOnly.SpanMomentKnmPerM;
            }

            double spread = StairActions.ConcentratedLoadSpreadMm(stair.WaistThicknessMm,
                                                                  stair.WidthMm);
            double pointMoment = StairActions.ConcentratedLoadMomentKnmPerM(
                gammaQ * settings.ConcentratedLoadKn, spanM, spread);

            result.ConcentratedLoadMomentKnmPerM = permanentMoment + pointMoment;
            result.ConcentratedLoadGoverns =
                result.ConcentratedLoadMomentKnmPerM > result.SpanMomentKnmPerM;

            result.Notes.Add(string.Format(
                "Situation alternative a charge concentree (art. 6.3.1.2(1)) : " +
                "Q_k = {0:0.00} kN sur 50 x 50 mm, diffusee a 45 degres a travers la seule " +
                "paillasse sur b = 50 + 2 x {1:0} = {2:0} mm. M = {3:0.00} (permanentes) + " +
                "{4:0.00} x {5:0.000} / (4 x {6:0.000}) = {7:0.00} kN.m/m, contre {8:0.00} " +
                "sous charge repartie. {9}",
                settings.ConcentratedLoadKn, stair.WaistThicknessMm, spread,
                permanentMoment, gammaQ * settings.ConcentratedLoadKn, spanM, spread / 1000.0,
                result.ConcentratedLoadMomentKnmPerM, result.SpanMomentKnmPerM,
                result.ConcentratedLoadGoverns
                    ? "LA CHARGE CONCENTREE GOUVERNE : c'est elle qui est retenue."
                    : "La charge repartie gouverne."));

            result.Notes.Add(
                "Aucun article de l'EN 1992-1-1 ne fixe la largeur de diffusion d'une charge " +
                "concentree sur une dalle portant dans un sens. Le moteur retient la diffusion " +
                "la plus DEFAVORABLE physiquement raisonnable : 45 degres a travers la seule " +
                "epaisseur de paillasse. Toute diffusion plus large — revetement, marches, " +
                "etalement longitudinal — donnerait un moment plus faible. La conclusion est " +
                "donc du cote de la securite, quelle que soit la regle de diffusion retenue " +
                "par ailleurs.");

            if (result.ConcentratedLoadGoverns)
            {
                // Le message compare aux deux valeurs D'ORIGINE : il est ecrit avant que le
                // moment de calcul ne soit remplace.
                result.Warnings.Add(string.Format(
                    "La charge concentree Q_k gouverne le dimensionnement ({0:0.00} contre " +
                    "{1:0.00} kN.m/m sous charge repartie). C'est le cas des volees courtes. " +
                    "Verifiez la valeur de Q_k retenue au tableau 6.2 et dans l'annexe " +
                    "nationale, et le poinconnement local de la marche, que le moteur ne " +
                    "calcule pas.",
                    result.ConcentratedLoadMomentKnmPerM, result.SpanMomentKnmPerM));
                result.SpanMomentKnmPerM = result.ConcentratedLoadMomentKnmPerM;
            }
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

        /// <summary>
        /// Consigne les decisions de disposition qui ne se lisent pas dans une verification :
        /// le detail de noeud retenu, l'ordre des nappes, la longueur de barre disponible.
        ///
        /// Elles sont dans le resultat parce qu'elles font partie du calcul. Un plan de
        /// ferraillage produit sans dire quelles decisions l'ont forme n'est pas verifiable.
        /// </summary>
        private static void AddDetailingDecisions(StairData stair, StairReinforcement r,
                                                  StairDetailingRules rules,
                                                  StairDesignResult result)
        {
            if (r.HasKneeJoint)
            {
                result.Decisions.Add(new DetailingDecision(
                    "Detail du noeud", 0.0,
                    rules.KneeJoint == KneeJointDetail.CrossedBars
                        ? "Nappes CROISEES : chacune s'ancre dans la face opposee. Detail de "
                          + "la pratique etablie pour un angle rentrant tendu."
                        : "EPINGLE DIAGONALE : les nappes s'arretent au pli et une epingle "
                          + "separee franchit l'angle. Coute une barre de plus, encombre "
                          + "moins le noeud quand les diametres sont gros."));
            }

            result.Decisions.Add(new DetailingDecision(
                "Position de la repartition", 0.0,
                rules.DistributionAboveMainBars
                    ? "Repartition AU-DESSUS des porteuses : ce sont elles qui doivent avoir "
                      + "la plus grande hauteur utile."
                    : "Repartition SOUS les porteuses, a la demande de l'ingenieur : la "
                      + "hauteur utile des porteuses en est reduite d'un diametre."));

            // Une barre plus longue que la barre de stock n'existe pas : il faut recouvrir,
            // et repartir les recouvrements est une decision de plan, pas un automatisme.
            double longest = 0.0;
            string longestLabel = null;
            foreach (RebarGroup group in result.Plan.Groups)
            {
                if (group.BarLengthMm > longest)
                {
                    longest = group.BarLengthMm;
                    longestLabel = group.Label;
                }
            }

            if (longest > rules.StockLengthMm)
            {
                result.Warnings.Add(string.Format(
                    "BARRE PLUS LONGUE QUE LE STOCK : {0} developpe {1:0} mm pour une barre "
                    + "de {2:0} mm. Elle doit etre recouverte sur {3:0} mm. Le moteur ne "
                    + "decoupe pas : repartir les recouvrements en quinconce est une decision "
                    + "de plan, et les placer tous au meme endroit creerait une section "
                    + "affaiblie sur toute la largeur de la volee.",
                    longestLabel, longest, rules.StockLengthMm, r.LapLengthMm));
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

        /// <summary>
        /// EN 1992-1-1 art. 9.3.1.2(2) : encastrement partiel non pris en compte dans
        /// l'analyse. Le moteur calcule la volee en travee isostatique — c'est securitaire
        /// pour la travee, mais cela ne fait pas disparaitre le moment negatif qui se
        /// developpe reellement sur des appuis coules en continuite. L'article ne demande
        /// pas de le calculer : il impose un minimum forfaitaire, en SECTION et en
        /// LONGUEUR, et les deux sont verifies ici.
        /// </summary>
        private static void AddPartialFixityCheck(StairData stair, StairReinforcement r,
                                                  StairDesignResult result)
        {
            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.2 (2)",
                Equation = "As,sup >= As(0,25 M_travee) et longueur >= 0,2 l",
                Description = "Encastrement partiel non pris en compte dans l'analyse",
                GoverningCombination = "ULS-6.10"
            };

            if (result.PartialFixitySteelMm2PerM <= 0)
            {
                check.Status = CheckStatus.NotApplicable;
                check.Comment = "Aucun moment de travee : rien a brider sur appui.";
                result.Checks.Add(check);
                return;
            }

            if (r.TopMain == null)
            {
                check.Status = CheckStatus.Fail;
                check.Demand = Quantity.Area(result.PartialFixitySteelMm2PerM);
                check.Resistance = Quantity.Area(0.0);
                check.Utilization = 99.0;
                check.Comment =
                    "AUCUNE NAPPE SUPERIEURE n'est posee. L'article l'exige des que la "
                    + "volee est coulee en continuite avec ses appuis, et c'est le cas "
                    + "courant : sans chapeau, la face superieure fissure sur appui. "
                    + "Cochez les chapeaux, ou declarez un appui reellement libre.";
                result.Checks.Add(check);
                return;
            }

            double provided = r.TopMain.AreaPerMetreMm2;
            check.Demand = Quantity.Area(result.PartialFixitySteelMm2PerM);
            check.Resistance = Quantity.Area(provided);
            check.Utilization = provided > 0
                ? result.PartialFixitySteelMm2PerM / provided : 99.0;
            check.Status = provided >= result.PartialFixitySteelMm2PerM - 1.0
                ? CheckStatus.Pass : CheckStatus.Fail;

            double floor = PartialFixity.MinimumExtentMm(stair.SpanMm);
            bool longEnough = PartialFixity.ExtentIsSufficient(r.TopBarLengthMm, stair.SpanMm);
            if (!longEnough) check.Status = CheckStatus.Fail;

            check.Comment = string.Format(
                "0,25 M_travee demande {0:0} mm2/m, la nappe superieure en fournit {1:0}. "
                + "Longueur retenue {2:0} mm pour un minimum de 0,2 l = {3:0} mm{4}",
                result.PartialFixitySteelMm2PerM, provided, r.TopBarLengthMm, floor,
                longEnough ? "." : " : INSUFFISANT.");

            result.Checks.Add(check);
        }

        private static void AddCrackingCheck(StairData stair, StairReinforcement r,
                                             StairDesignSettings settings,
                                             ConcreteProperties materials,
                                             StairDesignResult result)
        {
            if (result.SpanMomentKnmPerM <= 0 || result.SpanSteelRequiredMm2PerM <= 0) return;

            double crackWidth = settings.CrackWidthLimitMm > 0
                ? settings.CrackWidthLimitMm
                : CrackControl.RecommendedCrackWidthMm(settings.Exposure);

            // Rapport des combinaisons : le moment quasi-permanent se deduit du moment ELU
            // dans le rapport des charges, faute d'analyse en section fissuree.
            double quasiPermanentMoment = result.FlightUltimateLoadKnM2 > 0
                ? result.SpanMomentKnmPerM * result.QuasiPermanentLoadKnM2
                  / result.FlightUltimateLoadKnM2
                : result.SpanMomentKnmPerM * 0.7;

            double stress = CrackControl.SteelStress(materials.Fyd, quasiPermanentMoment,
                result.SpanMomentKnmPerM, result.SpanSteelRequiredMm2PerM,
                r.BottomMain.AreaPerMetreMm2);

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
                    "un calcul de contrainte. Sur une volee interieure en XC1 ce critere est " +
                    "rarement determinant, mais l'omettre revenait a le supposer.", stress)
            };
            check.WithInput("phi", Quantity.Length(r.BottomMain.DiameterMm))
                 .WithInput("phi_max", Quantity.Length(cracking.MaxBarDiameterMm))
                 .WithInput("s", Quantity.Length(r.BottomMain.SpacingMm))
                 .WithInput("s_max", Quantity.Length(cracking.MaxSpacingMm));

            // L'article n'exige qu'un seul des deux criteres : celui qui est satisfait compte.
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

        private static void AddConcentratedLoadCheck(StairDesignSettings settings,
                                                     StairDesignResult result)
        {
            var check = new CheckResult
            {
                Code = Ec1,
                Clause = "6.3.1.2 (1)",
                Equation = "M sous Q_k concentree <= M retenu pour le dimensionnement",
                Description = "Situation alternative a charge concentree",
                GoverningCombination = "ULS-6.10"
            };

            if (settings.MomentSource == StairMomentSource.Entered)
            {
                check.Status = CheckStatus.NotApplicable;
                check.Comment = "Les sollicitations sont saisies : le moteur ne sait pas quelles " +
                                "situations l'analyse exterieure a enveloppees, et ne peut donc " +
                                "pas ajouter celle-ci. Verifiez que Q_k y figure.";
                result.Checks.Add(check);
                return;
            }

            if (settings.ConcentratedLoadKn <= 0)
            {
                check.Status = CheckStatus.NotApplicable;
                check.Comment = "Aucune charge concentree Q_k n'est declaree. La verification " +
                                "est sans objet, et le dire vaut mieux que de l'omettre : " +
                                "l'article existe, c'est sa valeur qui manque.";
                result.Checks.Add(check);
                return;
            }

            check.Comment = result.ConcentratedLoadGoverns
                ? "La charge concentree gouverne : c'est son moment qui a ete retenu pour le " +
                  "dimensionnement, et le taux vaut donc 1,00."
                : "La charge repartie gouverne, ET C'EST DESORMAIS VERIFIE plutot que suppose. " +
                  "La diffusion retenue est la plus defavorable raisonnable.";
            check.Verify(Quantity.Moment(result.ConcentratedLoadMomentKnmPerM),
                         Quantity.Moment(result.SpanMomentKnmPerM));
            result.Checks.Add(check);
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
