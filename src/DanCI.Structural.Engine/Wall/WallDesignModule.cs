using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Loads;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.Detailing;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Wall
{
    /// <summary>
    /// DanCI Wall Design : voile en beton arme comprime, verifie a deux echelles qu'il ne
    /// faut jamais confondre.
    ///
    /// **Hors plan**, le voile se comporte comme un poteau de section 1 000 x t : c'est
    /// l'echelle de la bande verticale de 1 metre, et c'est la que jouent l'elancement et
    /// le second ordre.
    ///
    /// **Dans son plan**, le voile est une console verticale de grande hauteur : c'est
    /// l'echelle du voile entier, et c'est la que jouent l'effort tranchant de
    /// contreventement et les barres de rive.
    /// </summary>
    public sealed class WallDesignModule
        : IDesignModule<WallData, WallDesignSettings, WallDesignResult>
    {
        private const string Ec2 = "EN 1992-1-1:2004";

        /// <summary>Diametre suppose au premier passage.</summary>
        private const double AssumedDiameterMm = 10.0;

        private static readonly IWallDetailingCode Detailing = new Ec2WallDetailing();

        public string Name { get { return "DanCI Wall Design"; } }

        public WallDesignResult Design(WallData wall, WallDesignSettings settings,
                                       IReadOnlyList<LoadCombination> combinations)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new WallDesignResult
            {
                Wall = wall,
                IsValid = false,
                CodeLabel = Ec2 + " - " + annex.Name + " - " + Detailing.ShortName
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);
            result.Notes.Add(
                "Le comportement hors plan est traite sur une bande verticale de 1 000 mm ; " +
                "le contreventement porte sur le voile entier.");

            CheckGeometry(wall, result);

            // --- Enrobage ---
            CoverResult cover = ResolveCover(settings, AssumedDiameterMm);
            result.Notes.Add(cover.Justification);

            var r = new WallReinforcement { CoverMm = cover.NominalCoverMm };
            r.EffectiveDepthMm = wall.ThicknessMm - r.CoverMm - AssumedDiameterMm / 2.0;
            if (r.EffectiveDepthMm <= 0)
            {
                result.Warnings.Add("Le voile est trop mince pour l'enrobage demande.");
                return result;
            }

            // --- Flambement hors plan ---
            double restraintSpacing = settings.RestraintSpacingMm > 0
                ? settings.RestraintSpacingMm : wall.LengthMm;
            WallBucklingResult buckling = WallEffectiveLength.Compute(wall.ClearHeightMm,
                restraintSpacing, settings.Restraint);
            result.Buckling = buckling;
            result.Notes.Add(buckling.Justification);
            if (!buckling.LateralRestraintEffective)
            {
                result.Warnings.Add(
                    "Le maintien lateral declare est trop eloigne pour raidir la partie " +
                    "courante du voile. Verifiez les conditions de maintien avant d'utiliser " +
                    "ce resultat.");
            }

            double axial = UnitConverter.KnToN(settings.AxialLoadKnPerM);
            double stripArea = wall.StripAreaMm2;
            double relativeAxial = stripArea > 0 ? axial / (stripArea * materials.Fcd) : 0.0;

            // Elancement de la bande : i = t / sqrt(12) pour une section rectangulaire.
            double radiusOfGyration = wall.ThicknessMm / Math.Sqrt(12.0);
            result.SlendernessRatio = radiusOfGyration > 0
                ? buckling.BucklingLengthMm / radiusOfGyration : 0.0;

            // --- Sollicitations hors plan ---
            double firstOrderMoment = UnitConverter.KnmToNmm(settings.OutOfPlaneMomentKnmPerM);
            double minimumEccentricity = SecondOrder.MinimumEccentricityMm(wall.ThicknessMm);
            double minimumMoment = axial * minimumEccentricity;
            if (minimumMoment > firstOrderMoment)
            {
                result.Notes.Add(string.Format(
                    "EC2 6.1(4) : l'excentricite minimale e_0 = max(t/30 ; 20) = {0:0} mm " +
                    "impose M = {1:0.0} kN.m/m, superieur au moment declare.",
                    minimumEccentricity, minimumMoment / 1e6));
                firstOrderMoment = minimumMoment;
            }

            // --- Ferraillage vertical minimal, article 9.6.2 ---
            string minVerticalJustification;
            double minVertical = Detailing.MinVerticalSteel(stripArea, out minVerticalJustification);
            result.Notes.Add(minVerticalJustification);

            // Le second ordre depend du ferraillage, qui depend du second ordre : on part
            // du minimum reglementaire et on augmente tant que la section ne resiste pas.
            double verticalRequired = minVertical;
            SecondOrderResult secondOrder = null;
            double designMoment = firstOrderMoment;
            bool capacityReached = false;
            double maxVertical = Detailing.MaxVerticalSteel(stripArea, false);

            var diagram = new InteractionDiagram(materials);
            for (int iteration = 0; iteration < 24; iteration++)
            {
                double mechanicalRatio = verticalRequired * materials.Fyd
                                         / (stripArea * materials.Fcd);
                double limit = SecondOrder.SlendernessLimit(relativeAxial, mechanicalRatio,
                    settings.CreepCoefficient);

                if (result.SlendernessRatio > limit)
                {
                    secondOrder = SecondOrder.NominalCurvature(axial, buckling.BucklingLengthMm,
                        r.EffectiveDepthMm, result.SlendernessRatio, relativeAxial,
                        mechanicalRatio, materials.SteelYieldStrain, materials.Fck,
                        settings.CreepCoefficient);
                    secondOrder.SlendernessLimit = limit;
                    designMoment = firstOrderMoment + secondOrder.SecondOrderMomentNmm;
                }
                else
                {
                    secondOrder = new SecondOrderResult
                    {
                        Slenderness = result.SlendernessRatio,
                        SlendernessLimit = limit,
                        Required = false,
                        Justification = string.Format(
                            "EC2 5.8.3.1 : lambda = {0:0.0} <= lambda_lim = {1:0.0}, " +
                            "le second ordre peut etre neglige.",
                            result.SlendernessRatio, limit)
                    };
                    designMoment = firstOrderMoment;
                }

                BendingSection section = StripSection(wall, r, verticalRequired);
                double resistance = diagram.MomentResistance(section, axial);
                if (resistance >= designMoment)
                {
                    capacityReached = true;
                    break;
                }

                if (verticalRequired >= maxVertical) break;
                verticalRequired = Math.Min(verticalRequired * 1.15, maxVertical);
            }

            result.SecondOrder = secondOrder;
            result.DesignOutOfPlaneMomentKnmPerM = designMoment / 1e6;
            result.VerticalSteelRequiredMm2PerM = verticalRequired;
            if (secondOrder != null) result.Notes.Add(secondOrder.Justification);

            if (!capacityReached)
            {
                result.Warnings.Add(string.Format(
                    "SECTION INSUFFISANTE hors plan : meme au maximum reglementaire de " +
                    "{0:0} mm2/m (0,04 Ac), la bande ne reprend pas M_Ed = {1:0.0} kN.m/m. " +
                    "Actions possibles : epaissir le voile, reduire la hauteur libre, " +
                    "ajouter un retour de voile, ou augmenter la classe de beton.",
                    maxVertical, designMoment / 1e6));
                return result;
            }

            // --- Choix des nappes verticales ---
            string spacingJustification;
            double maxVerticalSpacing = Detailing.MaxVerticalSpacingMm(wall.ThicknessMm,
                out spacingJustification);
            var verticalOptimizer = new MeshOptimizer(settings.AutoVerticalDiameter
                ? (double[])null : new[] { settings.ForcedVerticalDiameterMm });

            // La section requise est totale : chaque nappe en reprend la moitie.
            r.VerticalPerFace = verticalOptimizer.Select(verticalRequired / 2.0, maxVerticalSpacing);
            if (r.VerticalPerFace == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : aucune nappe verticale courante ne fournit " +
                    "l'acier requis dans l'espacement autorise.");
                return result;
            }

            // --- Aciers horizontaux, article 9.6.3 ---
            string minHorizontalJustification;
            double minHorizontal = Detailing.MinHorizontalSteel(stripArea,
                r.VerticalTotalMm2PerM, out minHorizontalJustification);
            result.Notes.Add(minHorizontalJustification);

            var horizontalOptimizer = new MeshOptimizer(settings.AutoHorizontalDiameter
                ? (double[])null : new[] { settings.ForcedHorizontalDiameterMm });
            r.HorizontalPerFace = horizontalOptimizer.Select(minHorizontal / 2.0,
                Detailing.MaxHorizontalSpacingMm);
            if (r.HorizontalPerFace == null)
            {
                result.Warnings.Add("Aucune nappe horizontale constructible n'a ete trouvee.");
                return result;
            }

            // Hauteur utile reelle une fois le diametre connu.
            r.EffectiveDepthMm = wall.ThicknessMm - r.CoverMm - r.VerticalPerFace.DiameterMm / 2.0;

            // --- Epingles de liaison, article 9.6.4 ---
            if (Detailing.RequiresTransverseLinks(stripArea, r.VerticalTotalMm2PerM))
            {
                r.LinksPerSquareMetre = Detailing.LinksPerSquareMetre;
                r.LinkDiameterMm = Math.Max(6.0, r.VerticalPerFace.DiameterMm / 4.0);
                result.Notes.Add(string.Format(
                    "EC2 9.6.4(1) : As,v = {0:0} mm2/m depasse 0,02 Ac = {1:0} mm2/m, des " +
                    "epingles de liaison sont exigees ({2:0.0} au m2, HA{3:0}). Elles ne sont " +
                    "pas decoratives : elles empechent les barres verticales comprimees de " +
                    "flamber en faisant eclater l'enrobage.",
                    r.VerticalTotalMm2PerM, 0.02 * stripArea, r.LinksPerSquareMetre,
                    r.LinkDiameterMm));
            }

            AddOutOfPlaneChecks(wall, r, materials, diagram, axial, designMoment,
                                minVertical, maxVertical, maxVerticalSpacing,
                                spacingJustification, minHorizontal, result);
            AddInPlaneChecks(wall, r, settings, materials, annex, axial, result);

            // --- Ancrages et recouvrements ---
            AnchorageResult anchorage = Anchorage.Compute(r.VerticalPerFace.DiameterMm,
                materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            r.LapLengthMm = RoundUpTo(anchorage.LapLengthMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            result.Reinforcement = r;
            result.Plan = WallPlanBuilder.Build(wall, r, MarkPrefix(wall));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Geometrie
        // ------------------------------------------------------------------

        private static void CheckGeometry(WallData wall, WallDesignResult result)
        {
            if (!wall.IsWallByCode)
            {
                result.Warnings.Add(string.Format(
                    "L'article 9.6.1 definit un voile par longueur >= 4 x epaisseur. Ici " +
                    "{0:0} mm pour {1:0} mm d'epaisseur, soit un rapport de {2:0.0} : cet " +
                    "element est un POTEAU au sens de l'Eurocode, et ce sont les dispositions " +
                    "de l'article 9.5 qui s'appliquent, pas celles de 9.6. Utilisez le module " +
                    "Column.",
                    wall.LengthMm, wall.ThicknessMm,
                    wall.ThicknessMm > 0 ? wall.LengthMm / wall.ThicknessMm : 0.0));
            }

            if (wall.HeightToThickness > 40.0)
            {
                result.Warnings.Add(string.Format(
                    "Elancement geometrique h/t = {0:0.0} : au-dela de 40, un voile devient " +
                    "difficile a betonner et tres sensible aux defauts de verticalite.",
                    wall.HeightToThickness));
            }
        }

        // ------------------------------------------------------------------
        // Section de calcul
        // ------------------------------------------------------------------

        /// <summary>
        /// Bande verticale de 1 metre vue en flexion hors plan : une section rectangulaire
        /// de 1 000 mm de large et t de haut, avec une nappe d'acier a chaque parement.
        /// </summary>
        private static BendingSection StripSection(WallData wall, WallReinforcement r,
                                                   double totalSteelMm2PerM)
        {
            BendingSection section = BendingSection.Rectangular(wall.ThicknessMm,
                                                                WallData.StripWidthMm);
            double offset = r.CoverMm + AssumedDiameterMm / 2.0;
            section.BarDepthsMm.Add(offset);
            section.BarAreasMm2.Add(totalSteelMm2PerM / 2.0);
            section.BarDepthsMm.Add(wall.ThicknessMm - offset);
            section.BarAreasMm2.Add(totalSteelMm2PerM / 2.0);
            return section;
        }

        // ------------------------------------------------------------------
        // Verifications hors plan
        // ------------------------------------------------------------------

        private static void AddOutOfPlaneChecks(WallData wall, WallReinforcement r,
                                                ConcreteProperties materials,
                                                InteractionDiagram diagram,
                                                double axialN, double designMomentNmm,
                                                double minVertical, double maxVertical,
                                                double maxVerticalSpacing,
                                                string spacingJustification,
                                                double minHorizontal,
                                                WallDesignResult result)
        {
            BendingSection section = StripSection(wall, r, r.VerticalTotalMm2PerM);
            double resistance = diagram.MomentResistance(section, axialN);

            var capacity = new CheckResult
            {
                Code = Ec2,
                Clause = "6.1 et 5.8.8",
                Equation = "M_Ed (1er + 2e ordre) <= M_Rd (N_Ed)",
                Description = "Flexion composee hors plan",
                GoverningCombination = "ULS-COMB-001",
                Comment = result.SecondOrder != null && result.SecondOrder.Required
                    ? "Second ordre inclus par la courbure nominale."
                    : "Second ordre negligeable."
            };
            capacity.WithInput("N_Ed", Quantity.Force(UnitConverter.NToKn(axialN)))
                    .WithInput("lambda", Quantity.Ratio(result.SlendernessRatio));
            capacity.Verify(Quantity.Moment(designMomentNmm / 1e6),
                            Quantity.Moment(resistance / 1e6));
            result.Checks.Add(capacity);

            var minimum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.6.2 (1)",
                Equation = "As,v >= 0,002 Ac",
                Description = "Section verticale minimale",
                GoverningCombination = "ULS-COMB-001"
            };
            minimum.Verify(Quantity.Area(minVertical), Quantity.Area(r.VerticalTotalMm2PerM));
            result.Checks.Add(minimum);

            var maximum = new CheckResult
            {
                Code = Ec2,
                Clause = "9.6.2 (1)",
                Equation = "As,v <= 0,04 Ac (0,08 Ac aux recouvrements)",
                Description = "Section verticale maximale",
                GoverningCombination = "ULS-COMB-001"
            };
            maximum.Verify(Quantity.Area(r.VerticalTotalMm2PerM), Quantity.Area(maxVertical));
            result.Checks.Add(maximum);

            var spacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.6.2 (3)",
                Equation = "s <= min(3t ; 400 mm)",
                Description = "Espacement des aciers verticaux",
                GoverningCombination = "ULS-COMB-001",
                Comment = spacingJustification
            };
            spacing.Verify(Quantity.Length(r.VerticalPerFace.SpacingMm),
                           Quantity.Length(maxVerticalSpacing));
            result.Checks.Add(spacing);

            var horizontal = new CheckResult
            {
                Code = Ec2,
                Clause = "9.6.3 (1)",
                Equation = "As,h >= max(0,25 As,v ; 0,001 Ac)",
                Description = "Section horizontale minimale",
                GoverningCombination = "ULS-COMB-001",
                Comment = "Proportionnelle a ce qui est reellement pose en vertical."
            };
            horizontal.Verify(Quantity.Area(minHorizontal),
                              Quantity.Area(r.HorizontalTotalMm2PerM));
            result.Checks.Add(horizontal);

            var horizontalSpacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.6.3 (2)",
                Equation = "s <= 400 mm",
                Description = "Espacement des aciers horizontaux",
                GoverningCombination = "ULS-COMB-001"
            };
            horizontalSpacing.Verify(Quantity.Length(r.HorizontalPerFace.SpacingMm),
                                     Quantity.Length(Detailing.MaxHorizontalSpacingMm));
            result.Checks.Add(horizontalSpacing);
        }

        // ------------------------------------------------------------------
        // Verifications dans le plan
        // ------------------------------------------------------------------

        private static void AddInPlaneChecks(WallData wall, WallReinforcement r,
                                             WallDesignSettings settings,
                                             ConcreteProperties materials, INationalAnnex annex,
                                             double axialPerMetreN, WallDesignResult result)
        {
            if (settings.InPlaneShearKn <= 0 && settings.InPlaneMomentKnm <= 0) return;

            // Le voile vu comme une console verticale : sa "hauteur utile" est sa longueur.
            double effectiveLength = 0.8 * wall.LengthMm;
            double totalAxial = axialPerMetreN * wall.LengthMm / WallData.StripWidthMm;
            double axialStress = wall.GrossAreaMm2 > 0 ? totalAxial / wall.GrossAreaMm2 : 0.0;

            if (settings.InPlaneShearKn > 0)
            {
                double shear = UnitConverter.KnToN(settings.InPlaneShearKn);

                // L'acier tendu mobilisable est celui des aciers verticaux du voile.
                double verticalSteel = r.VerticalTotalMm2PerM * wall.LengthMm / 1000.0;
                ShearResult shearResult = ShearDesign.Design(shear, wall.ThicknessMm,
                    effectiveLength, verticalSteel, axialStress, materials, annex.GammaC);

                var check = new CheckResult
                {
                    Code = Ec2,
                    Clause = "6.2.2 et 6.2.3",
                    Equation = "V_Ed <= V_Rd,c, sinon V_Ed <= V_Rd,s avec les aciers horizontaux",
                    Description = "Effort tranchant dans le plan",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = "Le voile est vu comme une console verticale de hauteur utile " +
                              "0,8 l_w. " + shearResult.Justification
                };
                check.WithInput("sigma_cp", Quantity.Stress(axialStress));

                if (!shearResult.RequiresShearReinforcement)
                {
                    check.Verify(Quantity.Force(UnitConverter.NToKn(shear)),
                                 Quantity.Force(UnitConverter.NToKn(shearResult.VrdcN)));
                    result.Checks.Add(check);
                }
                else
                {
                    // Les aciers horizontaux poses jouent le role des cadres.
                    double provided = r.HorizontalTotalMm2PerM;
                    check.Description = "Effort tranchant dans le plan - aciers horizontaux";
                    check.Equation = "A_sw/s requis <= A_sh posee";
                    check.Verify(Quantity.Area(shearResult.AswPerMetreMm2),
                                 Quantity.Area(provided));
                    check.Comment += " Les aciers horizontaux du voile tiennent lieu " +
                                     "d'armatures d'effort tranchant.";
                    result.Checks.Add(check);

                    var crushing = new CheckResult
                    {
                        Code = Ec2,
                        Clause = "6.2.3 (3)",
                        Equation = "V_Ed <= V_Rd,max",
                        Description = "Ecrasement des bielles dans le plan",
                        GoverningCombination = "ULS-COMB-001",
                        Comment = "Si les bielles cedent, aucune armature ne rattrape : " +
                                  "il faut epaissir le voile."
                    };
                    crushing.Verify(Quantity.Force(UnitConverter.NToKn(shear)),
                                    Quantity.Force(UnitConverter.NToKn(shearResult.VrdmaxN)));
                    result.Checks.Add(crushing);

                    if (crushing.Status == CheckStatus.Fail)
                    {
                        result.Warnings.Add(
                            "BIELLES ECRASEES dans le plan du voile. Aucune armature ne " +
                            "rattrape cela : epaissir le voile ou l'allonger.");
                    }
                }
            }

            if (settings.InPlaneMomentKnm > 0)
            {
                // Bras de levier approche entre les deux zones de rive.
                double leverArm = 0.8 * wall.LengthMm;
                double moment = UnitConverter.KnmToNmm(settings.InPlaneMomentKnm);

                // La compression due a l'effort normal soulage la traction de rive.
                double tension = moment / leverArm - totalAxial / 2.0;
                result.EdgeSteelRequiredMm2 = Math.Max(tension / materials.Fyd, 0.0);

                double distributed = r.VerticalTotalMm2PerM * (0.15 * wall.LengthMm) / 1000.0;
                double edgeProvided = distributed
                    + (settings.EdgeBars
                        ? settings.EdgeBarCount * UnitConverter.BarArea(settings.EdgeBarDiameterMm)
                        : 0.0);

                var check = new CheckResult
                {
                    Code = Ec2,
                    Clause = "6.1",
                    Equation = "As,rive >= (M_Ed / z - N_Ed / 2) / f_yd",
                    Description = "Traction de rive, flexion dans le plan",
                    GoverningCombination = "ULS-COMB-001",
                    Comment = string.Format(
                        "Modele simplifie a deux zones de rive, bras de levier z = 0,8 l_w = " +
                        "{0:0} mm. La compression N_Ed soulage la traction. L'acier disponible " +
                        "compte les aciers verticaux repartis sur 15 % de la longueur, plus " +
                        "les barres de rive.", leverArm)
                };
                check.WithInput("M_Ed", Quantity.Moment(settings.InPlaneMomentKnm))
                     .WithInput("N_Ed", Quantity.Force(UnitConverter.NToKn(totalAxial)));
                check.Verify(Quantity.Area(result.EdgeSteelRequiredMm2),
                             Quantity.Area(edgeProvided));
                result.Checks.Add(check);

                if (settings.EdgeBars)
                {
                    r.EdgeBarCount = settings.EdgeBarCount;
                    r.EdgeBarDiameterMm = settings.EdgeBarDiameterMm;
                }
                else if (check.Status == CheckStatus.Fail)
                {
                    result.Warnings.Add(
                        "La traction de rive n'est pas reprise par les seuls aciers repartis. " +
                        "Activez les barres de rive, ou augmentez le ferraillage vertical.");
                }

                result.Warnings.Add(
                    "La flexion dans le plan est traitee par un modele simplifie a deux zones " +
                    "de rive. Les elements de rive confines de l'EN 1998-1 ne sont pas " +
                    "dimensionnes : pour un voile de contreventement en zone sismique, ce " +
                    "resultat est insuffisant.");
            }
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static CoverResult ResolveCover(WallDesignSettings settings, double barDiameterMm)
        {
            CoverResult required = ConcreteCover.Compute(barDiameterMm, settings.Exposure,
                settings.ConcreteStrengthMPa, settings.DesignLife, true, false);
            if (settings.AutoCover) return required;

            required.NominalCoverMm = settings.CoverMm;
            required.Justification = string.Format(
                "Enrobage impose par l'utilisateur : {0:0} mm.", settings.CoverMm);
            return required;
        }

        private static string MarkPrefix(WallData wall)
        {
            return string.IsNullOrWhiteSpace(wall.Mark) ? "V" : wall.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
