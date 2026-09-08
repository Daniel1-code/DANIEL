using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.EC7;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.StripFooting
{
    /// <summary>
    /// DanCI Strip Footing : semelle filante sous voile, dimensionnee au metre courant.
    ///
    /// Elle differe de la semelle isolee sur trois points, et ce sont eux qui font le
    /// module. Elle ne poinconne pas, parce que la charge est repartie sur toute sa
    /// longueur et non concentree sur un poteau. Elle est presque toujours pilotee par la
    /// section minimale, parce que le debord est court. Et sur un debord court, la
    /// longueur d'ancrage disponible au-dela du nu ne suffit souvent pas, ce qui impose
    /// un crochet d'extremite que le moteur doit exiger explicitement.
    /// </summary>
    public sealed class StripFootingDesignModule
        : IDesignModule<StripFootingData, StripFootingDesignSettings, StripFootingDesignResult>
    {
        private const string Ec2 = "EN 1992-1-1:2004";
        private const string Ec7 = "EN 1997-1:2004";

        private const double AssumedDiameterMm = 12.0;

        public string Name { get { return "DanCI Strip Footing"; } }

        public StripFootingDesignResult Design(StripFootingData footing,
                                               StripFootingDesignSettings settings,
                                               IReadOnlyList<LoadCombination> combinations)
        {
            StripFootingDesignResult result = DesignOnce(footing, settings, AssumedDiameterMm);

            // La hauteur utile depend du diametre retenu, qui depend de la hauteur utile.
            if (result.IsValid && result.Reinforcement.Transverse.DiameterMm > 0
                && Math.Abs(result.Reinforcement.Transverse.DiameterMm - AssumedDiameterMm) > 0.1)
            {
                StripFootingDesignResult refined = DesignOnce(footing, settings,
                    result.Reinforcement.Transverse.DiameterMm);
                if (refined.IsValid) return refined;
            }

            return result;
        }

        private static StripFootingDesignResult DesignOnce(StripFootingData footing,
                                                           StripFootingDesignSettings settings,
                                                           double assumedDiameterMm)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new StripFootingDesignResult
            {
                Footing = footing,
                IsValid = false,
                CodeLabel = Ec2 + " et " + Ec7 + " - " + annex.Name
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);
            result.Notes.Add("Calcul mene sur un metre courant de semelle.");

            CheckGeometry(footing, result);

            // --- Enrobage ---
            CoverResult cover = ResolveCover(settings, assumedDiameterMm);
            result.Notes.Add(cover.Justification);

            var r = new StripFootingReinforcement { CoverMm = cover.NominalCoverMm };
            r.EffectiveDepthMm = footing.ThicknessMm - r.CoverMm - assumedDiameterMm / 2.0;
            if (r.EffectiveDepthMm <= 0)
            {
                result.Warnings.Add("La semelle est trop mince pour l'enrobage demande.");
                return result;
            }

            // --- Charges, par metre courant ---
            double wallLoad = UnitConverter.KnToN(settings.AxialLoadKnPerM);
            double selfWeight = settings.IncludeSelfWeight
                ? footing.VolumePerRunMm3 * 1e-9 * settings.ConcreteUnitWeightKnM3 * 1000.0
                : 0.0;
            double totalLoad = wallLoad + selfWeight;

            // L'effort horizontal en tete cree un moment supplementaire sur la hauteur.
            double moment = UnitConverter.KnmToNmm(settings.MomentKnmPerM)
                            + UnitConverter.KnToN(settings.HorizontalLoadKnPerM)
                              * footing.ThicknessMm;

            result.Notes.Add(string.Format(
                "Charges a la base : N = {0:0.0} kN/m (dont {1:0.0} kN/m de poids propre), " +
                "M = {2:0.0} kN.m/m",
                totalLoad / 1000.0, selfWeight / 1000.0, moment / 1e6));

            // --- Contraintes : la tranche de calcul est un rectangle B x 1 000 ---
            SoilPressureResult pressure = SoilPressure.Compute(footing.WidthMm,
                StripFootingData.RunLengthMm, totalLoad, moment, 0.0, wallLoad);
            result.Pressure = pressure;
            result.Notes.Add(pressure.Justification);

            AddGeotechnicalChecks(footing, settings, pressure, totalLoad, moment, result);

            // --- Flexion de la console ---
            double netPressure = pressure.NetPressureKpa / 1000.0;
            double designPressure = Math.Max(netPressure,
                pressure.MaxPressureKpa / 1000.0 * (wallLoad / Math.Max(totalLoad, 1.0)));

            double cantileverMoment = StripFootingData.RunLengthMm
                                      * footing.OverhangMm * footing.OverhangMm
                                      * designPressure / 2.0;
            result.CantileverMomentKnmPerM = cantileverMoment / 1e6;

            BendingResult bending = BendingDesign.Rectangular(cantileverMoment,
                StripFootingData.RunLengthMm, r.EffectiveDepthMm, r.CoverMm, materials);

            string minJustification;
            double asMin = BendingDesign.MinimumTensionSteel(StripFootingData.RunLengthMm,
                r.EffectiveDepthMm, materials, out minJustification);
            result.Notes.Add(minJustification);

            result.TransverseSteelRequiredMm2PerM = Math.Max(bending.TensionSteelMm2, asMin);
            result.Notes.Add(string.Format(
                "Console transversale : debord {0:0} mm, M = {1:0.00} kN.m/m -> " +
                "As de flexion {2:0} mm2/m, As retenu {3:0} mm2/m{4}",
                footing.OverhangMm, result.CantileverMomentKnmPerM, bending.TensionSteelMm2,
                result.TransverseSteelRequiredMm2PerM,
                asMin > bending.TensionSteelMm2
                    ? string.Format(" (As,min gouverne, facteur {0:0.0})",
                                    asMin / Math.Max(bending.TensionSteelMm2, 1.0))
                    : ""));

            // --- Choix des nappes ---
            // EC2 9.3.1.1(3), applicable aux semelles : s <= min(3h ; 400).
            double maxSpacing = Math.Min(3.0 * footing.ThicknessMm, 400.0);
            double maxLongitudinalSpacing = Math.Min(3.5 * footing.ThicknessMm, 450.0);
            var optimizer = new MeshOptimizer(settings.AutoMeshDiameter
                ? (double[])null : new[] { settings.ForcedMeshDiameterMm });

            r.Transverse = optimizer.Select(result.TransverseSteelRequiredMm2PerM, maxSpacing);
            if (r.Transverse == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : aucune nappe courante ne fournit l'acier " +
                    "transversal requis. Actions possibles : epaissir la semelle, " +
                    "l'elargir, ou augmenter la classe de beton.");
                return result;
            }

            // EC2 9.3.1.1(2) : repartition >= 20 % de l'armature principale.
            double longitudinalRequired = 0.2 * r.Transverse.AreaPerMetreMm2;
            r.Longitudinal = optimizer.Select(longitudinalRequired, maxLongitudinalSpacing);
            if (r.Longitudinal == null)
            {
                result.Warnings.Add("Aucune repartition longitudinale constructible.");
                return result;
            }

            if (settings.TopMesh)
            {
                r.TopTransverse = optimizer.Select(asMin, maxSpacing);
                result.Notes.Add("Nappe transversale superieure posee au minimum reglementaire.");
            }

            // Hauteur utile reelle, une fois le diametre connu.
            r.EffectiveDepthMm = footing.ThicknessMm - r.CoverMm - r.Transverse.DiameterMm / 2.0;

            AddBendingChecks(footing, r, result, asMin, maxSpacing, maxLongitudinalSpacing,
                             longitudinalRequired);
            AddShearCheck(footing, r, materials, annex, designPressure, result);
            AddNoPunchingNote(footing, result);

            // --- Ancrages ---
            AnchorageResult anchorage = Anchorage.Compute(r.Transverse.DiameterMm,
                materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            AddTransverseAnchorageCheck(footing, r, anchorage.DesignAnchorageMm, result);

            // --- Attentes du voile ---
            if (settings.Starters)
            {
                r.StarterSpacingMm = settings.StarterSpacingMm;
                r.StarterDiameterMm = settings.StarterDiameterMm;
                AnchorageResult starterAnchorage = Anchorage.Compute(r.StarterDiameterMm,
                    materials, annex);
                r.StarterProjectionMm = RoundUpTo(starterAnchorage.LapLengthMm, 50.0);
                r.StarterReturnMm = RoundUpTo(
                    Math.Max(starterAnchorage.DesignAnchorageMm * 0.3,
                             10.0 * r.StarterDiameterMm), 50.0);
                result.Notes.Add(string.Format(
                    "Attentes de voile : deux files HA{0:0} a e = {1:0} mm, retour horizontal " +
                    "de {2:0} mm en pied, depassement de {3:0} mm pour le recouvrement.",
                    r.StarterDiameterMm, r.StarterSpacingMm, r.StarterReturnMm,
                    r.StarterProjectionMm));
            }

            result.Reinforcement = r;
            result.Plan = StripFootingPlanBuilder.Build(footing, r, MarkPrefix(footing));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        private static void CheckGeometry(StripFootingData footing,
                                          StripFootingDesignResult result)
        {
            if (footing.OverhangMm <= 0)
            {
                result.Warnings.Add(
                    "Le voile est plus large que la semelle : verifiez les dimensions.");
                return;
            }

            if (!footing.IsRigid)
            {
                result.Warnings.Add(string.Format(
                    "Debord de {0:0} mm pour une epaisseur de {1:0} mm, soit un rapport de " +
                    "{2:0.0}. Au-dela de 2, la semelle cesse d'etre rigide : la repartition " +
                    "lineaire des contraintes et le modele de console encastree ne sont plus " +
                    "representatifs. Epaississez la semelle, ou menez une analyse de " +
                    "l'interaction sol-structure.",
                    footing.OverhangMm, footing.ThicknessMm,
                    footing.OverhangMm / footing.ThicknessMm));
            }
        }

        // ------------------------------------------------------------------
        // Verifications
        // ------------------------------------------------------------------

        private static void AddGeotechnicalChecks(StripFootingData footing,
                                                  StripFootingDesignSettings settings,
                                                  SoilPressureResult pressure,
                                                  double totalLoadN, double momentNmm,
                                                  StripFootingDesignResult result)
        {
            var bearing = new CheckResult
            {
                Code = Ec7,
                Clause = "6.5.2 et annexe D",
                Equation = "sigma' = V / B' <= sigma_adm",
                Description = "Capacite portante du sol",
                GoverningCombination = "ULS-COMB-001",
                Comment = "Largeur effective B' = B - 2e, methode de Meyerhof, appliquee au " +
                          "metre courant."
            };
            bearing.WithInput("B'", Quantity.Length(pressure.EffectiveWidthMm));
            bearing.Verify(new Quantity(pressure.EffectivePressureKpa, "kPa", 0),
                           new Quantity(settings.AllowableBearingPressureKpa, "kPa", 0));
            result.Checks.Add(bearing);

            var uplift = new CheckResult
            {
                Code = Ec7,
                Clause = "6.5.4",
                Equation = "e <= B/6",
                Description = "Absence de soulevement sous la semelle",
                Demand = Quantity.Length(pressure.EccentricityXMm),
                Resistance = Quantity.Length(footing.WidthMm / 6.0),
                GoverningCombination = "ULS-COMB-001"
            };
            uplift.Utilization = uplift.Resistance.Value > 0
                ? uplift.Demand.Value / uplift.Resistance.Value : 0.0;
            uplift.Status = pressure.WithinCore ? CheckStatus.Pass : CheckStatus.Fail;
            uplift.Comment = pressure.WithinCore
                ? "La resultante reste dans le noyau central : toute la largeur est comprimee."
                : "La resultante sort du noyau central : la semelle decolle sur un bord.";
            result.Checks.Add(uplift);

            double horizontal = Math.Abs(UnitConverter.KnToN(settings.HorizontalLoadKnPerM));
            if (horizontal > 0)
            {
                double effectiveArea = pressure.EffectiveWidthMm * StripFootingData.RunLengthMm;
                StabilityResult sliding = StabilityChecks.Sliding(horizontal, totalLoadN,
                    settings.InterfaceFrictionAngleDeg, settings.InterfaceAdhesionKpa,
                    effectiveArea);

                var check = new CheckResult
                {
                    Code = Ec7,
                    Clause = "6.5.3",
                    Equation = "H_Ed <= V' tan(delta_d) + A' c_a,d",
                    Description = "Glissement a la base",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = sliding.Justification
                };
                check.Verify(Quantity.Force(UnitConverter.NToKn(sliding.Destabilising)),
                             Quantity.Force(UnitConverter.NToKn(sliding.Stabilising)));
                result.Checks.Add(check);
            }

            if (Math.Abs(momentNmm) > 0)
            {
                StabilityResult over = StabilityChecks.Overturning(Math.Abs(momentNmm),
                    totalLoadN, footing.WidthMm);

                var check = new CheckResult
                {
                    Code = Ec7,
                    Clause = "2.4.7.2 (EQU)",
                    Equation = "M_dst <= 0,9 N B/2",
                    Description = "Renversement",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = over.Justification
                };
                check.Verify(Quantity.Moment(over.Destabilising / 1e6),
                             Quantity.Moment(over.Stabilising / 1e6));
                result.Checks.Add(check);
            }
        }

        private static void AddBendingChecks(StripFootingData footing,
                                             StripFootingReinforcement r,
                                             StripFootingDesignResult result,
                                             double asMin, double maxSpacing,
                                             double maxLongitudinalSpacing,
                                             double longitudinalRequired)
        {
            var bending = new CheckResult
            {
                Code = Ec2,
                Clause = "6.1",
                Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                Description = "Flexion de la console transversale",
                GoverningCombination = "ULS-COMB-001"
            };
            bending.Verify(Quantity.Area(result.TransverseSteelRequiredMm2PerM),
                           Quantity.Area(r.Transverse.AreaPerMetreMm2));
            result.Checks.Add(bending);

            var minimum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (1) et 9.2.1.1 (1)",
                Equation = "As >= max(0,26 f_ctm/f_yk b d ; 0,0013 b d)",
                Description = "Section transversale minimale",
                GoverningCombination = "ULS-COMB-001"
            };
            minimum.Verify(Quantity.Area(asMin), Quantity.Area(r.Transverse.AreaPerMetreMm2));
            result.Checks.Add(minimum);

            var spacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (3)",
                Equation = "s <= min(3h ; 400 mm)",
                Description = "Espacement des armatures transversales",
                GoverningCombination = "ULS-COMB-001"
            };
            spacing.Verify(Quantity.Length(r.Transverse.SpacingMm), Quantity.Length(maxSpacing));
            result.Checks.Add(spacing);

            var distribution = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (2)",
                Equation = "As,long >= 0,20 As,transversale",
                Description = "Repartition longitudinale",
                GoverningCombination = "ULS-COMB-001",
                Comment = "Elle repartit les charges le long de la semelle et reprend le " +
                          "retrait : sans elle, une semelle filante fissure transversalement."
            };
            distribution.Verify(Quantity.Area(longitudinalRequired),
                                Quantity.Area(r.Longitudinal.AreaPerMetreMm2));
            result.Checks.Add(distribution);

            var longitudinalSpacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (3)",
                Equation = "s <= min(3,5h ; 450 mm)",
                Description = "Espacement de la repartition longitudinale",
                GoverningCombination = "ULS-COMB-001"
            };
            longitudinalSpacing.Verify(Quantity.Length(r.Longitudinal.SpacingMm),
                                       Quantity.Length(maxLongitudinalSpacing));
            result.Checks.Add(longitudinalSpacing);
        }

        private static void AddShearCheck(StripFootingData footing,
                                          StripFootingReinforcement r,
                                          ConcreteProperties materials, INationalAnnex annex,
                                          double designPressureMPa,
                                          StripFootingDesignResult result)
        {
            // Section a la distance d du nu du voile, article 6.2.1(8).
            double distanceFromEdge = footing.OverhangMm - r.EffectiveDepthMm;
            if (distanceFromEdge <= 0)
            {
                result.Notes.Add(string.Format(
                    "Semelle compacte : la section a d du nu ({0:0} mm) tombe au-dela du bord " +
                    "(debord {1:0} mm). L'effort tranchant unidirectionnel n'est pas " +
                    "dimensionnant, c'est le cas courant d'une semelle filante epaisse.",
                    r.EffectiveDepthMm, footing.OverhangMm));
                return;
            }

            double shear = designPressureMPa * StripFootingData.RunLengthMm * distanceFromEdge;
            double ratio = r.Transverse.AreaPerMetreMm2
                           / (StripFootingData.RunLengthMm * r.EffectiveDepthMm);
            string justification;
            double resistance = ShearDesign.ShearResistanceWithoutReinforcement(
                StripFootingData.RunLengthMm, r.EffectiveDepthMm,
                ratio * StripFootingData.RunLengthMm * r.EffectiveDepthMm, 0.0,
                materials, annex.GammaC, out justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.2",
                Equation = "V_Ed <= V_Rd,c a la distance d du nu",
                Description = "Effort tranchant transversal",
                GoverningCombination = "ULS-COMB-001",
                Comment = "Une semelle ne porte pas d'armatures d'effort tranchant : " +
                          "si V_Rd,c est depasse, il faut epaissir. " + justification
            };
            check.Verify(Quantity.Force(UnitConverter.NToKn(shear)),
                         Quantity.Force(UnitConverter.NToKn(resistance)));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE a l'effort tranchant. Actions possibles : epaissir " +
                    "la semelle, ou reduire le debord.");
            }
        }

        /// <summary>
        /// Une semelle filante sous voile ne poinconne pas : la charge arrive repartie sur
        /// toute sa longueur, pas concentree sur un poteau. Le dire explicitement evite de
        /// laisser croire a un oubli.
        /// </summary>
        private static void AddNoPunchingNote(StripFootingData footing,
                                              StripFootingDesignResult result)
        {
            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "6.4.1 (2)",
                Equation = "sans objet",
                Description = "Poinconnement",
                Status = CheckStatus.NotApplicable,
                GoverningCombination = "ULS-COMB-001",
                Comment = "Sans objet : la charge d'un voile arrive repartie sur toute la " +
                          "longueur de la semelle, pas concentree sur une aire chargee. " +
                          "Si la semelle porte aussi des poteaux, ils doivent etre verifies " +
                          "separement au poinconnement, ce que ce module ne fait pas."
            };
            result.Checks.Add(check);
        }

        /// <summary>
        /// Sur un debord court, la longueur disponible au-dela du nu du voile ne suffit
        /// souvent pas a ancrer droit la barre transversale. C'est un detail que l'on
        /// oublie sur plan et qui se paie au ferraillage.
        ///
        /// L'article 8.4.4 permet un coefficient alpha_1 = 0,70 pour une barre coudee,
        /// mais **seulement** si l'enrobage lateral depasse trois diametres (tableau 8.2).
        /// Sur une semelle, cette condition n'est presque jamais remplie pour les gros
        /// diametres : le moteur la verifie au lieu de l'appliquer d'office.
        /// </summary>
        private static void AddTransverseAnchorageCheck(StripFootingData footing,
                                                        StripFootingReinforcement r,
                                                        double straightAnchorageMm,
                                                        StripFootingDesignResult result)
        {
            double available = footing.OverhangMm - r.CoverMm;
            double diameter = r.Transverse.DiameterMm;

            // Tableau 8.2 : alpha_1 = 0,7 pour une barre autre que droite, si c_d > 3 phi.
            bool hookIsEffective = r.CoverMm > 3.0 * diameter;
            double alpha1 = hookIsEffective ? 0.7 : 1.0;
            double requiredMm = alpha1 * straightAnchorageMm;

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "8.4.4 et tableau 8.2",
                Equation = "longueur disponible au-dela du nu >= alpha_1 l_b,rqd",
                Description = "Ancrage des armatures transversales",
                GoverningCombination = "ULS-COMB-001"
            };
            check.WithInput("l_bd droit", Quantity.Length(straightAnchorageMm))
                 .WithInput("alpha_1", Quantity.Ratio(alpha1));
            check.Verify(Quantity.Length(requiredMm), Quantity.Length(available));

            if (check.Status == CheckStatus.Fail)
            {
                check.Status = CheckStatus.Warning;
                r.TransverseNeedsHook = true;
                check.Comment = string.Format(
                    "Le debord ne laisse que {0:0} mm au-dela du nu, contre {1:0} mm requis. " +
                    "Un CROCHET D'EXTREMITE est pose, et le plan de ferraillage le porte. " +
                    "{2} Au-dela, l'ancrage doit etre justifie par le modele bielles-tirants " +
                    "de l'article 9.8.2.2, que ce module ne fait pas : c'est une verification " +
                    "manuelle a votre charge.",
                    available, requiredMm,
                    hookIsEffective
                        ? "Le coefficient alpha_1 = 0,70 du tableau 8.2 est deja pris en compte."
                        : string.Format("Le coefficient alpha_1 = 0,70 n'est PAS applicable : " +
                                        "l'enrobage de {0:0} mm ne depasse pas 3 phi = {1:0} mm.",
                                        r.CoverMm, 3.0 * diameter));
                result.Notes.Add(
                    "Les armatures transversales sont posees avec crochets d'extremite : " +
                    "l'ancrage droit ne tient pas dans le debord.");
                result.Warnings.Add(
                    "ANCRAGE : le debord est trop court pour ancrer les armatures " +
                    "transversales selon l'article 8.4.4. Le crochet est pose, mais " +
                    "l'ancrage reste a justifier par le modele bielles-tirants de " +
                    "l'article 9.8.2.2, ou en elargissant la semelle.");
            }
            else
            {
                check.Comment = "L'ancrage tient dans le debord, aucun crochet n'est necessaire.";
            }

            result.Checks.Add(check);
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static CoverResult ResolveCover(StripFootingDesignSettings settings,
                                                double barDiameterMm)
        {
            CoverResult required = ConcreteCover.ForFooting(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.CastDirectlyAgainstSoil,
                settings.DesignLife);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm.", settings.CoverMm);
            return required;
        }

        private static string MarkPrefix(StripFootingData footing)
        {
            return string.IsNullOrWhiteSpace(footing.Mark) ? "SF" : footing.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
