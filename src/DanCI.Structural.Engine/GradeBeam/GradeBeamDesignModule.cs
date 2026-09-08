using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.EC8;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.GradeBeam
{
    /// <summary>
    /// DanCI Grade Beam : longrine entre fondations.
    ///
    /// Une longrine n'est pas une poutre posee bas. Trois choses la distinguent, et ce
    /// sont elles qui font le module :
    ///
    /// - C'est d'abord un **tirant**. L'effort qu'elle reprend est axial et alterne, et il
    ///   s'ajoute a la flexion dans les deux nappes a la fois.
    /// - Ses **deux nappes filent** d'un appui a l'autre. L'article 5.8.2(5) de
    ///   l'EN 1998-1 exige 0,4 % en haut ET en bas, ce qui gouverne souvent la nappe
    ///   superieure a lui seul.
    /// - Ce sur quoi elle **repose** change tout. Suspendue, elle porte l'integralite de
    ///   sa charge ; posee sur le sol, presque rien. Le moteur la calcule toujours comme
    ///   suspendue, ce qui est securitaire, et refuse de prendre credit d'un appui du sol
    ///   sans analyse de poutre sur sol elastique.
    /// </summary>
    public sealed class GradeBeamDesignModule
        : IDesignModule<GradeBeamData, GradeBeamDesignSettings, GradeBeamDesignResult>
    {
        private const string Ec2 = "EN 1992-1-1:2004";
        private const string Ec8 = "EN 1998-1:2004";

        private const double AssumedLongitudinalMm = 16.0;

        public string Name { get { return "DanCI Grade Beam"; } }

        public GradeBeamDesignResult Design(GradeBeamData beam, GradeBeamDesignSettings settings,
                                            IReadOnlyList<LoadCombination> combinations)
        {
            GradeBeamDesignResult result = DesignOnce(beam, settings, AssumedLongitudinalMm);

            // La hauteur utile depend du diametre retenu, qui depend de la hauteur utile.
            if (result.IsValid && result.Reinforcement.BottomBars.DiameterMm > 0
                && Math.Abs(result.Reinforcement.BottomBars.DiameterMm - AssumedLongitudinalMm)
                   > 0.1)
            {
                GradeBeamDesignResult refined = DesignOnce(beam, settings,
                    result.Reinforcement.BottomBars.DiameterMm);
                if (refined.IsValid) return refined;
            }

            return result;
        }

        private static GradeBeamDesignResult DesignOnce(GradeBeamData beam,
                                                        GradeBeamDesignSettings settings,
                                                        double assumedDiameterMm)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new GradeBeamDesignResult
            {
                Beam = beam,
                IsValid = false,
                CodeLabel = Ec2 + (settings.SeismicDesign ? " et " + Ec8 : "")
                            + " - " + annex.Name
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);

            CheckMinimumSection(beam, settings, result);

            // --- Enrobage ---
            CoverResult cover = ResolveCover(settings, assumedDiameterMm);
            result.Notes.Add(cover.Justification);

            var r = new GradeBeamReinforcement
            {
                CoverMm = cover.NominalCoverMm,
                StirrupLegs = settings.StirrupLegs,
                // Pratique courante : jamais moins de HA8 sur une longrine, un cadre plus
                // fin ne se tient pas a la mise en oeuvre.
                StirrupDiameterMm = settings.AutoStirrupDiameter
                    ? Math.Max(8.0,
                        BarDatabase.SmallestTransverseAtLeast(assumedDiameterMm / 4.0))
                    : settings.ForcedStirrupDiameterMm
            };
            r.EffectiveDepthMm = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm
                                 - assumedDiameterMm / 2.0;
            if (r.EffectiveDepthMm <= 0)
            {
                result.Warnings.Add("La longrine est trop basse pour l'enrobage demande.");
                return result;
            }

            // --- Charges et sollicitations ---
            ResolveActions(beam, settings, result);

            double moment = UnitConverter.KnmToNmm(result.SpanMomentKnm);
            double shear = UnitConverter.KnToN(result.ShearKn);

            // --- Effort de liaison ---
            ResolveTieForce(settings, result);
            double tension = UnitConverter.KnToN(Math.Max(result.TieForceKn, 0.0));
            double tensionSteel = FoundationTies.TensionSteel(tension, materials.Fyd);

            // --- Flexion ---
            BendingResult bending = BendingDesign.Rectangular(moment, beam.WidthMm,
                r.EffectiveDepthMm, r.CoverMm + r.StirrupDiameterMm, materials);

            string ec2MinJustification;
            double ec2Min = BendingDesign.MinimumTensionSteel(beam.WidthMm, r.EffectiveDepthMm,
                materials, out ec2MinJustification);

            // EN 1998-1 5.8.2(5) : 0,4 % en haut ET en bas. Ce minimum-la est souvent
            // beaucoup plus exigeant que celui de l'EC2, et il s'applique aux deux nappes.
            double tieMin = 0.0;
            string tieMinJustification = null;
            if (settings.SeismicDesign)
            {
                tieMin = FoundationTies.MinimumSteelPerFace(beam.GrossAreaMm2,
                    out tieMinJustification);
                result.Notes.Add(tieMinJustification);
            }

            double minimumPerFace = Math.Max(ec2Min, tieMin);
            result.Notes.Add(ec2MinJustification);

            // La traction axiale se repartit a parts egales entre les deux nappes.
            double tensionPerFace = tensionSteel / 2.0;
            result.BottomSteelRequiredMm2 =
                Math.Max(bending.TensionSteelMm2 + tensionPerFace, minimumPerFace);
            result.TopSteelRequiredMm2 = Math.Max(tensionPerFace, minimumPerFace);

            if (tensionSteel > 0)
            {
                result.Notes.Add(string.Format(
                    "Traction axiale : As,N = N / f_yd = {0:0} mm2, repartis a parts egales " +
                    "entre les deux nappes ({1:0} mm2 chacune). C'est la methode manuelle " +
                    "usuelle pour une traction faible combinee a de la flexion : elle est " +
                    "securitaire et evite une analyse en flexion composee tendue.",
                    tensionSteel, tensionPerFace));
            }

            // --- Choix des barres ---
            double clearWidth = beam.WidthMm - 2.0 * (r.CoverMm + r.StirrupDiameterMm);
            var optimizer = new BeamRebarOptimizer(
                settings.AutoLongitudinalDiameter
                    ? null : new[] { settings.ForcedLongitudinalDiameterMm },
                settings.MaxLayers);

            // EC2 8.2(2) : espacement libre minimal entre barres.
            double minClear = Math.Max(annex.ClearSpacingBarFactor * assumedDiameterMm,
                Math.Max(settings.AggregateSizeMm + annex.ClearSpacingAggregateAdditionMm,
                         annex.ClearSpacingMinimumMm));

            r.BottomBars = optimizer.Select(result.BottomSteelRequiredMm2, clearWidth, minClear);
            r.TopBars = optimizer.Select(result.TopSteelRequiredMm2, clearWidth, minClear);

            if (r.BottomBars == null || r.BottomBars.Count <= 0
                || r.TopBars == null || r.TopBars.Count <= 0)
            {
                result.Warnings.Add(string.Format(
                    "SECTION INSUFFISANTE : les barres requises ne tiennent pas dans une " +
                    "largeur libre de {0:0} mm. Actions possibles : elargir la longrine, " +
                    "l'approfondir, ou augmenter le nombre de lits.", clearWidth));
                return result;
            }

            // Hauteur utile reelle, une fois le diametre connu.
            r.EffectiveDepthMm = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm
                                 - r.BottomBars.DiameterMm / 2.0;

            // --- Effort tranchant ---
            ShearResult shearResult = ShearDesign.Design(shear, beam.WidthMm,
                r.EffectiveDepthMm, r.BottomBars.AreaMm2, 0.0, materials, annex.GammaC);
            result.Notes.Add(shearResult.Justification);

            double legArea = settings.StirrupLegs * UnitConverter.BarArea(r.StirrupDiameterMm);
            // A_sw/s sort deja en mm2 par millimetre : aucune conversion.
            double requiredPerMm = Math.Max(shearResult.AswPerMillimetreMm2,
                                            shearResult.MinimumAswPerMillimetreMm2);
            r.StirrupSpacingMm = requiredPerMm > 0
                ? RoundDownTo(Math.Min(legArea / requiredPerMm, shearResult.MaxSpacingMm), 25.0)
                : RoundDownTo(shearResult.MaxSpacingMm, 25.0);
            if (r.StirrupSpacingMm < 50.0) r.StirrupSpacingMm = 50.0;

            result.Notes.Add(string.Format(
                "Cadres a espacement constant sur toute la longrine : l'effort de liaison " +
                "est constant d'un appui a l'autre, et zoner l'espacement pour un element " +
                "aussi court n'apporterait rien. e = {0:0} mm, plafonne par 0,75 d = {1:0} mm.",
                r.StirrupSpacingMm, shearResult.MaxSpacingMm));

            AddChecks(beam, settings, r, materials, result, bending, shearResult,
                      minimumPerFace, ec2Min, tieMin, legArea, requiredPerMm, tension);

            // --- Ancrages ---
            AnchorageResult anchorage = Anchorage.Compute(r.BottomBars.DiameterMm,
                materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);
            result.Notes.Add(string.Format(
                "Les deux nappes filent d'un appui a l'autre et sont ancrees de {0:0} mm " +
                "au-dela de chaque nu : un tirant ne se justifie que s'il est ancre a ses " +
                "deux extremites.", r.AnchorageLengthMm));

            result.Reinforcement = r;
            result.Plan = GradeBeamPlanBuilder.Build(beam, r, MarkPrefix(beam));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        private static void CheckMinimumSection(GradeBeamData beam,
                                                GradeBeamDesignSettings settings,
                                                GradeBeamDesignResult result)
        {
            if (!settings.SeismicDesign) return;

            double minHeight = FoundationTies.MinimumHeightMm(settings.StoreyCount);
            if (beam.WidthMm < FoundationTies.MinimumWidthMm - 1e-6
                || beam.HeightMm < minHeight - 1e-6)
            {
                result.Warnings.Add(string.Format(
                    "EN 1998-1 5.8.1(4) : une longrine doit mesurer au moins {0:0} mm de " +
                    "large et {1:0} mm de haut pour un batiment de {2} niveau(x). La section " +
                    "declaree fait {3:0} x {4:0} mm.",
                    FoundationTies.MinimumWidthMm, minHeight, settings.StoreyCount,
                    beam.WidthMm, beam.HeightMm));
            }
        }

        // ------------------------------------------------------------------
        // Charges
        // ------------------------------------------------------------------

        private static void ResolveActions(GradeBeamData beam, GradeBeamDesignSettings settings,
                                           GradeBeamDesignResult result)
        {
            double selfWeight = settings.IncludeSelfWeight
                ? beam.GrossAreaMm2 * 1e-6 * settings.ConcreteUnitWeightKnM3 : 0.0;
            // Le poids propre est une action permanente : il entre pondere.
            result.DesignLoadKnPerM = settings.WallLoadKnPerM + 1.35 * selfWeight;

            result.Notes.Add(string.Format(
                "Charge de calcul : {0:0.00} kN/m du mur porte + 1,35 x {1:0.00} kN/m de " +
                "poids propre = {2:0.00} kN/m.",
                settings.WallLoadKnPerM, selfWeight, result.DesignLoadKnPerM));

            double w = result.DesignLoadKnPerM;
            double l = beam.SpanMm / 1000.0;

            switch (beam.SpanKind)
            {
                case GradeBeamSpanKind.EndSpan:
                    result.SpanMomentKnm = w * l * l / 11.0;
                    result.ShearKn = 0.6 * w * l;
                    result.Notes.Add(
                        "Travee de rive : M = w l2 / 11, V = 0,60 w l. ATTENTION : " +
                        "coefficients de continuite usuels, PAS des valeurs de l'Eurocode 2.");
                    result.Warnings.Add(
                        "Moments de continuite estimes par des coefficients usuels, hors " +
                        "Eurocode. A remplacer par les resultats d'une analyse.");
                    break;

                case GradeBeamSpanKind.InteriorSpan:
                    result.SpanMomentKnm = w * l * l / 16.0;
                    result.ShearKn = 0.5 * w * l;
                    result.Notes.Add(
                        "Travee intermediaire : M = w l2 / 16, V = 0,50 w l. ATTENTION : " +
                        "coefficients de continuite usuels, PAS des valeurs de l'Eurocode 2.");
                    result.Warnings.Add(
                        "Moments de continuite estimes par des coefficients usuels, hors " +
                        "Eurocode. A remplacer par les resultats d'une analyse.");
                    break;

                default:
                    result.SpanMomentKnm = w * l * l / 8.0;
                    result.ShearKn = w * l / 2.0;
                    result.Notes.Add(
                        "Travee isostatique : M = w l2 / 8 et V = w l / 2, par la statique seule.");
                    break;
            }

            if (settings.Bedding == GradeBeamBedding.SoilBearing)
            {
                // Prendre credit de l'appui du sol demande une analyse de poutre sur sol
                // elastique. Le moteur ne la fait pas et ne fait pas semblant.
                double contact = result.DesignLoadKnPerM / (beam.WidthMm / 1000.0);
                result.Notes.Add(string.Format(
                    "Longrine declaree posee sur le sol. Le moteur la calcule NEANMOINS comme " +
                    "suspendue, ce qui est securitaire : prendre credit de la reaction du sol " +
                    "demande une analyse de poutre sur sol elastique que ce module ne fait " +
                    "pas. Contrainte de contact indicative si le sol reprenait tout : " +
                    "{0:0} kPa contre {1:0} kPa admissibles.",
                    contact, settings.AllowableBearingPressureKpa));

                if (contact > settings.AllowableBearingPressureKpa)
                {
                    result.Warnings.Add(string.Format(
                        "La contrainte de contact indicative ({0:0} kPa) depasse la contrainte " +
                        "admissible ({1:0} kPa) : le sol ne peut de toute facon pas reprendre " +
                        "la charge, la longrine doit porter.",
                        contact, settings.AllowableBearingPressureKpa));
                }
            }
            else
            {
                result.Notes.Add(
                    "Longrine suspendue entre appuis : elle porte l'integralite de sa charge. " +
                    "C'est le cas obligatoire au-dessus d'un sol gonflant, ou une forme " +
                    "perdue doit menager un vide sous la longrine.");
            }
        }

        // ------------------------------------------------------------------
        // Effort de liaison
        // ------------------------------------------------------------------

        private static void ResolveTieForce(GradeBeamDesignSettings settings,
                                            GradeBeamDesignResult result)
        {
            if (settings.SeismicDesign)
            {
                TieForceResult tie = FoundationTies.Compute(
                    UnitConverter.KnToN(settings.MeanColumnAxialLoadKn),
                    settings.GroundAccelerationRatio, settings.SoilFactor, settings.Ground);
                result.Tie = tie;
                result.TieForceKn = UnitConverter.NToKn(tie.AxialForceN);
                result.Notes.Add(tie.Justification);
                return;
            }

            result.TieForceKn = settings.ManualTieForceKn;
            if (settings.ManualTieForceKn > 0)
            {
                result.Notes.Add(string.Format(
                    "Effort de liaison impose par l'utilisateur : +- {0:0.0} kN. Il ne " +
                    "provient d'aucun article de l'EN 1992.", settings.ManualTieForceKn));
            }
            else
            {
                result.Notes.Add(
                    "Aucun effort de liaison retenu. Hors dimensionnement sismique, " +
                    "l'EN 1992 seul n'en impose aucun : la longrine est alors calculee en " +
                    "flexion et effort tranchant seuls. Si le projet exige une liaison au " +
                    "titre de la robustesse (EN 1991-1-7) ou d'un DTU, saisissez-la.");
            }
        }

        // ------------------------------------------------------------------
        // Verifications
        // ------------------------------------------------------------------

        private static void AddChecks(GradeBeamData beam, GradeBeamDesignSettings settings,
                                      GradeBeamReinforcement r, ConcreteProperties materials,
                                      GradeBeamDesignResult result, BendingResult bending,
                                      ShearResult shearResult, double minimumPerFace,
                                      double ec2Min, double tieMin, double legArea,
                                      double requiredPerMm, double tensionN)
        {
            var flexure = new CheckResult
            {
                Code = Ec2,
                Clause = "6.1",
                Equation = "As fourni >= As requis = M_Ed / (z f_yd) + N_Ed / (2 f_yd)",
                Description = "Flexion et traction, nappe inferieure",
                GoverningCombination = "ULS-COMB-001",
                Comment = string.Format("x/d = {0:0.000}, z = {1:0} mm{2}",
                    bending.NeutralAxisRatio, bending.LeverArmMm,
                    result.BottomSteelRequiredMm2 > bending.TensionSteelMm2 + 1e-6
                        ? ", un minimum ou la traction gouverne" : "")
            };
            flexure.Verify(Quantity.Area(result.BottomSteelRequiredMm2),
                           Quantity.Area(r.BottomBars.AreaMm2));
            result.Checks.Add(flexure);

            var top = new CheckResult
            {
                Code = settings.SeismicDesign ? Ec8 : Ec2,
                Clause = settings.SeismicDesign ? "5.8.2 (5)" : "9.2.1.1 (1)",
                Equation = settings.SeismicDesign
                    ? "As,haut >= 0,4 % Ac" : "As,haut >= As,min",
                Description = "Nappe superieure filante",
                GoverningCombination = "ULS-COMB-001",
                Comment = settings.SeismicDesign
                    ? "L'effort de liaison est alterne : la nappe superieure travaille en " +
                      "traction quand le sens s'inverse. Elle n'est pas constructive."
                    : "Nappe superieure posee sur toute la longueur, la longrine etant un " +
                      "element de liaison."
            };
            top.Verify(Quantity.Area(result.TopSteelRequiredMm2),
                       Quantity.Area(r.TopBars.AreaMm2));
            result.Checks.Add(top);

            if (settings.SeismicDesign)
            {
                var ratio = new CheckResult
                {
                    Code = Ec8,
                    Clause = "5.8.2 (5)",
                    Equation = "rho >= 0,4 % en haut ET en bas",
                    Description = "Pourcentage minimal des deux nappes",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = string.Format(
                        "Ce minimum sismique ({0:0} mm2) est {1} celui de l'EC2 ({2:0} mm2).",
                        tieMin, tieMin > ec2Min ? "plus exigeant que" : "moins exigeant que",
                        ec2Min)
                };
                ratio.Verify(Quantity.Area(tieMin),
                             Quantity.Area(Math.Min(r.BottomBars.AreaMm2, r.TopBars.AreaMm2)));
                result.Checks.Add(ratio);
            }

            var maximum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.2.1.1 (3)",
                Equation = "As <= 0,04 Ac",
                Description = "Section maximale d'armature",
                GoverningCombination = "ULS-COMB-001"
            };
            maximum.Verify(Quantity.Area(r.BottomBars.AreaMm2 + r.TopBars.AreaMm2),
                           Quantity.Area(BendingDesign.MaximumSteel(beam.GrossAreaMm2)));
            result.Checks.Add(maximum);

            var shear = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.3",
                Equation = "A_sw/s fourni >= A_sw/s requis",
                Description = "Effort tranchant",
                GoverningCombination = "ULS-COMB-001",
                Comment = string.Format(
                    "V_Rd,c = {0:0.0} kN {1} V_Ed = {2:0.0} kN ; cot theta = {3:0.00}",
                    shearResult.VrdcN / 1000.0,
                    shearResult.RequiresShearReinforcement ? "<" : ">=",
                    result.ShearKn, shearResult.CotTheta)
            };
            double provided = r.StirrupSpacingMm > 0 ? legArea / r.StirrupSpacingMm : 0.0;
            shear.Verify(new Quantity(requiredPerMm * 1000.0, "mm2/m", 0),
                         new Quantity(provided * 1000.0, "mm2/m", 0));
            result.Checks.Add(shear);

            var crushing = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.3 (3)",
                Equation = "V_Ed <= V_Rd,max",
                Description = "Ecrasement des bielles",
                GoverningCombination = "ULS-COMB-001",
                Comment = "Si les bielles cedent, aucune armature ne rattrape : il faut " +
                          "elargir ou approfondir la longrine."
            };
            crushing.Verify(Quantity.Force(result.ShearKn),
                            Quantity.Force(shearResult.VrdmaxN / 1000.0));
            result.Checks.Add(crushing);

            if (crushing.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(
                    "BIELLES ECRASEES : la section ne reprend pas l'effort tranchant. " +
                    "Elargir ou approfondir la longrine.");
            }

            var spacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.2.2 (6)",
                Equation = "s <= 0,75 d",
                Description = "Espacement des cadres",
                GoverningCombination = "ULS-COMB-001"
            };
            spacing.Verify(Quantity.Length(r.StirrupSpacingMm),
                           Quantity.Length(shearResult.MaxSpacingMm));
            result.Checks.Add(spacing);

            if (tensionN > 0)
            {
                double resistance = FoundationTies.CompressionResistance(beam.GrossAreaMm2,
                    r.BottomBars.AreaMm2 + r.TopBars.AreaMm2, materials.Fcd, materials.Fyd);

                var compression = new CheckResult
                {
                    Code = Ec2,
                    Clause = "6.1",
                    Equation = "N_Ed <= Ac f_cd + As f_yd",
                    Description = "Effort de liaison en compression",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = "L'effort de liaison est alterne : la longrine doit aussi le " +
                              "reprendre en compression. Enterree, elle est maintenue " +
                              "lateralement par le sol sur toute sa longueur, et le " +
                              "flambement ne la concerne pas."
                };
                compression.Verify(Quantity.Force(UnitConverter.NToKn(tensionN)),
                                   Quantity.Force(UnitConverter.NToKn(resistance)));
                result.Checks.Add(compression);
            }
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static CoverResult ResolveCover(GradeBeamDesignSettings settings,
                                                double barDiameterMm)
        {
            // Une longrine est un element de fondation : le plancher de l'article
            // 4.4.1.3(4) s'applique.
            CoverResult required = ConcreteCover.ForFooting(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.CastDirectlyAgainstSoil,
                settings.DesignLife);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm.", settings.CoverMm);
            return required;
        }

        private static string MarkPrefix(GradeBeamData beam)
        {
            return string.IsNullOrWhiteSpace(beam.Mark) ? "LG" : beam.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }

        private static double RoundDownTo(double value, double step)
        {
            return Math.Floor(value / step) * step;
        }
    }
}
