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

namespace DanCI.Structural.Engine.IsolatedFooting
{
    /// <summary>
    /// DanCI Isolated Footing : verifications geotechniques (EN 1997-1) et structurelles
    /// (EN 1992-1-1) d'une semelle isolee, puis choix des nappes et des attentes.
    ///
    /// Deux jeux de contraintes cohabitent, et les confondre est une erreur classique :
    /// la verification **geotechnique** porte sur la charge totale, poids propre de la
    /// semelle inclus, tandis que le calcul **structurel** n'utilise que la contrainte
    /// nette due au poteau, le poids propre etant directement equilibre par le sol.
    /// </summary>
    public sealed class FootingDesignModule
        : IDesignModule<FootingData, FootingDesignSettings, FootingDesignResult>
    {
        private const string Ec2 = "EN 1992-1-1:2004";
        private const string Ec7 = "EN 1997-1:2004";

        public string Name { get { return "DanCI Isolated Footing"; } }

        public FootingDesignResult Design(FootingData footing, FootingDesignSettings settings,
                                          IReadOnlyList<LoadCombination> combinations)
        {
            INationalAnnex annex = NationalAnnexFactory.Create(settings.NationalAnnex);
            var materials = new ConcreteProperties(
                new ConcreteMaterial(settings.ConcreteStrengthMPa),
                new SteelMaterial(settings.SteelStrengthMPa),
                annex);

            var result = new FootingDesignResult
            {
                Footing = footing,
                IsValid = false,
                CodeLabel = Ec2 + " et " + Ec7 + " - " + annex.Name
            };
            result.Notes.Add("Normes appliquees : " + result.CodeLabel);

            LoadCombination combination = ResolveCombination(combinations, settings);
            string combinationId = combination.Id;

            // --- Enrobage ---
            double provisionalDiameter = settings.AutoMeshDiameter ? 12.0 : settings.ForcedMeshDiameterMm;
            CoverResult cover = ResolveCover(settings, provisionalDiameter);
            result.Notes.Add(cover.Justification);

            var r = new FootingReinforcement { CoverMm = cover.NominalCoverMm };
            r.EffectiveDepthXMm = footing.ThicknessMm - r.CoverMm - provisionalDiameter / 2.0;
            r.EffectiveDepthYMm = r.EffectiveDepthXMm - provisionalDiameter;
            if (r.EffectiveDepthYMm <= 0)
            {
                result.Warnings.Add("La semelle est trop mince pour l'enrobage demande.");
                return result;
            }

            // --- Charges ---
            double columnLoad = UnitConverter.KnToN(settings.AxialLoadKn);
            double selfWeight = settings.IncludeSelfWeight
                ? footing.SelfWeightN(settings.ConcreteUnitWeightKnM3) : 0.0;
            double totalLoad = columnLoad + selfWeight;

            // Les efforts horizontaux en tete creent un moment supplementaire sur la hauteur
            // de la semelle.
            double momentAboutY = UnitConverter.KnmToNmm(settings.MomentAboutYKnm)
                                  + UnitConverter.KnToN(settings.ShearXKn) * footing.ThicknessMm;
            double momentAboutX = UnitConverter.KnmToNmm(settings.MomentAboutXKnm)
                                  + UnitConverter.KnToN(settings.ShearYKn) * footing.ThicknessMm;

            result.Notes.Add(string.Format(
                "Charges a la base : N = {0:0} kN (dont {1:0} kN de poids propre), " +
                "M/Y = {2:0.0} kN.m, M/X = {3:0.0} kN.m",
                totalLoad / 1000.0, selfWeight / 1000.0,
                momentAboutY / 1e6, momentAboutX / 1e6));

            // --- Contraintes sous la semelle ---
            SoilPressureResult pressure = SoilPressure.Compute(footing.WidthXMm, footing.WidthYMm,
                totalLoad, momentAboutY, momentAboutX, columnLoad);
            result.Pressure = pressure;
            result.Notes.Add(pressure.Justification);

            AddGeotechnicalChecks(footing, settings, pressure, totalLoad, momentAboutX,
                                  momentAboutY, combinationId, result);

            // --- Flexion des deux consoles ---
            double netPressure = pressure.NetPressureKpa / 1000.0;   // kPa -> N/mm2
            double designPressure = Math.Max(netPressure,
                pressure.MaxPressureKpa / 1000.0 * (columnLoad / Math.Max(totalLoad, 1.0)));

            double momentX = CantileverMoment(footing.OverhangXMm, footing.WidthYMm, designPressure);
            double momentY = CantileverMoment(footing.OverhangYMm, footing.WidthXMm, designPressure);

            BendingResult bendingX = BendingDesign.Rectangular(momentX, footing.WidthYMm,
                r.EffectiveDepthXMm, r.CoverMm, materials);
            BendingResult bendingY = BendingDesign.Rectangular(momentY, footing.WidthXMm,
                r.EffectiveDepthYMm, r.CoverMm, materials);

            string minXJustification;
            double asMinX = BendingDesign.MinimumTensionSteel(footing.WidthYMm, r.EffectiveDepthXMm,
                materials, out minXJustification);
            string minYJustification;
            double asMinY = BendingDesign.MinimumTensionSteel(footing.WidthXMm, r.EffectiveDepthYMm,
                materials, out minYJustification);

            double requiredX = Math.Max(bendingX.TensionSteelMm2, asMinX);
            double requiredY = Math.Max(bendingY.TensionSteelMm2, asMinY);
            result.RequiredSteelXMm2PerM = requiredX / (footing.WidthYMm / 1000.0);
            result.RequiredSteelYMm2PerM = requiredY / (footing.WidthXMm / 1000.0);

            result.Notes.Add(string.Format(
                "Console suivant X : debord {0:0} mm, M = {1:0.0} kN.m -> As = {2:0} mm2 " +
                "soit {3:0} mm2/m. " + minXJustification,
                footing.OverhangXMm, momentX / 1e6, requiredX, result.RequiredSteelXMm2PerM));
            result.Notes.Add(string.Format(
                "Console suivant Y : debord {0:0} mm, M = {1:0.0} kN.m -> As = {2:0} mm2 " +
                "soit {3:0} mm2/m.",
                footing.OverhangYMm, momentY / 1e6, requiredY, result.RequiredSteelYMm2PerM));

            // --- Choix des nappes ---
            // EC2 9.3.1.1(3) : espacement maximal des armatures principales de dalle.
            double maxSpacing = Math.Min(3.0 * footing.ThicknessMm, 400.0);
            var optimizer = new MeshOptimizer(settings.AutoMeshDiameter
                ? (double[])null : new[] { settings.ForcedMeshDiameterMm });

            r.BottomX = optimizer.Select(result.RequiredSteelXMm2PerM, maxSpacing);
            r.BottomY = optimizer.Select(result.RequiredSteelYMm2PerM, maxSpacing);
            if (r.BottomX == null || r.BottomY == null)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE : aucune nappe courante ne fournit l'acier requis. " +
                    "Actions possibles : epaissir la semelle, l'elargir, augmenter la classe " +
                    "de beton, ou autoriser un diametre superieur.");
                return result;
            }

            if (settings.TopMesh)
            {
                // Nappe superieure constructive : le minimum de l'article 9.3.1.1.
                r.TopX = optimizer.Select(asMinX / (footing.WidthYMm / 1000.0), maxSpacing);
                r.TopY = optimizer.Select(asMinY / (footing.WidthXMm / 1000.0), maxSpacing);
                result.Notes.Add("Nappe superieure posee au minimum reglementaire.");
            }

            // Hauteurs utiles reelles, une fois les diametres connus.
            r.EffectiveDepthXMm = footing.ThicknessMm - r.CoverMm - r.BottomX.DiameterMm / 2.0;
            r.EffectiveDepthYMm = footing.ThicknessMm - r.CoverMm - r.BottomX.DiameterMm
                                  - r.BottomY.DiameterMm / 2.0;

            AddBendingChecks(footing, r, requiredX, requiredY, asMinX, asMinY, bendingX, bendingY,
                             combinationId, result);

            // --- Poinconnement et effort tranchant ---
            double ratioX = r.BottomX.AreaPerMetreMm2 / (1000.0 * r.EffectiveDepthXMm);
            double ratioY = r.BottomY.AreaPerMetreMm2 / (1000.0 * r.EffectiveDepthYMm);
            double meanRatio = Math.Sqrt(Math.Max(ratioX * ratioY, 0.0));

            PunchingResult punching = PunchingShear.Check(columnLoad, footing.ColumnWidthXMm,
                footing.ColumnWidthYMm, r.MeanEffectiveDepthMm, netPressure, meanRatio,
                materials, annex.GammaC, footing.MinOverhangMm);
            result.Punching = punching;
            result.Notes.Add(punching.Justification);
            AddPunchingChecks(punching, r.MeanEffectiveDepthMm, combinationId, result);

            AddOneWayShearCheck(footing, r, materials, annex, designPressure, ratioX,
                                combinationId, result);

            // --- Ancrages et attentes ---
            AnchorageResult anchorage = Anchorage.Compute(r.BottomX.DiameterMm, materials, annex);
            r.AnchorageLengthMm = RoundUpTo(anchorage.DesignAnchorageMm, 50.0);
            result.Notes.Add(anchorage.Justification);

            if (settings.StarterBarCount > 0)
            {
                r.StarterBarCount = settings.StarterBarCount;
                r.StarterBarDiameterMm = settings.StarterBarDiameterMm;
                AnchorageResult starterAnchorage = Anchorage.Compute(r.StarterBarDiameterMm,
                    materials, annex);
                r.LapLengthMm = RoundUpTo(starterAnchorage.LapLengthMm, 50.0);
                r.StarterProjectionMm = r.LapLengthMm;
                r.StarterReturnMm = RoundUpTo(
                    Math.Max(starterAnchorage.DesignAnchorageMm * 0.3, 10.0 * r.StarterBarDiameterMm),
                    50.0);
                result.Notes.Add(string.Format(
                    "Attentes : {0} HA{1:0}, retour horizontal de {2:0} mm en pied, " +
                    "depassement de {3:0} mm pour le recouvrement avec le poteau.",
                    r.StarterBarCount, r.StarterBarDiameterMm, r.StarterReturnMm,
                    r.StarterProjectionMm));

                AddStarterAnchorageCheck(footing, r, starterAnchorage.DesignAnchorageMm,
                                         combinationId, result);
            }

            result.Reinforcement = r;
            result.Plan = FootingPlanBuilder.Build(footing, r, MarkPrefix(footing));
            result.IsValid = true;
            return result;
        }

        // ------------------------------------------------------------------
        // Flexion des consoles
        // ------------------------------------------------------------------

        /// <summary>
        /// Moment a l'encastrement d'une console de semelle, sous une contrainte uniforme :
        /// M = largeur x a^2 sigma / 2.
        /// </summary>
        private static double CantileverMoment(double overhangMm, double widthMm, double pressureMPa)
        {
            if (overhangMm <= 0) return 0.0;
            return widthMm * overhangMm * overhangMm * pressureMPa / 2.0;
        }

        // ------------------------------------------------------------------
        // Verifications
        // ------------------------------------------------------------------

        private static void AddGeotechnicalChecks(FootingData footing, FootingDesignSettings settings,
                                                  SoilPressureResult pressure, double totalLoadN,
                                                  double momentAboutXNmm, double momentAboutYNmm,
                                                  string combinationId, FootingDesignResult result)
        {
            var bearing = new CheckResult
            {
                Code = Ec7,
                Clause = "6.5.2 et annexe D",
                Equation = "sigma' = V / (B' L') <= sigma_adm",
                Description = "Capacite portante du sol",
                GoverningCombination = combinationId,
                Comment = "Contrainte uniforme sur l'aire effective (methode de Meyerhof)."
            };
            bearing.WithInput("B'", Quantity.Length(pressure.EffectiveWidthMm))
                   .WithInput("L'", Quantity.Length(pressure.EffectiveLengthMm));
            bearing.Verify(new Quantity(pressure.EffectivePressureKpa, "kPa", 0),
                           new Quantity(settings.AllowableBearingPressureKpa, "kPa", 0));
            result.Checks.Add(bearing);

            var uplift = new CheckResult
            {
                Code = Ec7,
                Clause = "6.5.4",
                Equation = "e <= B/6 dans chaque direction",
                Description = "Absence de soulevement sous la semelle",
                Demand = Quantity.Length(Math.Max(pressure.EccentricityXMm, pressure.EccentricityYMm)),
                Resistance = Quantity.Length(Math.Min(footing.WidthXMm, footing.WidthYMm) / 6.0),
                GoverningCombination = combinationId
            };
            uplift.Utilization = uplift.Resistance.Value > 0
                ? uplift.Demand.Value / uplift.Resistance.Value : 0.0;
            uplift.Status = pressure.WithinCore ? CheckStatus.Pass : CheckStatus.Fail;
            uplift.Comment = pressure.WithinCore
                ? "La resultante reste dans le noyau central : toute la surface est comprimee."
                : "La resultante sort du noyau central : une partie de la semelle decolle.";
            result.Checks.Add(uplift);

            double horizontal = Math.Sqrt(
                Math.Pow(UnitConverter.KnToN(settings.ShearXKn), 2.0) +
                Math.Pow(UnitConverter.KnToN(settings.ShearYKn), 2.0));
            if (horizontal > 0)
            {
                double effectiveArea = pressure.EffectiveWidthMm * pressure.EffectiveLengthMm;
                StabilityResult sliding = StabilityChecks.Sliding(horizontal, totalLoadN,
                    settings.InterfaceFrictionAngleDeg, settings.InterfaceAdhesionKpa, effectiveArea);

                var check = new CheckResult
                {
                    Code = Ec7,
                    Clause = "6.5.3",
                    Equation = "H_Ed <= V' tan(delta_d) + A' c_a,d",
                    Description = "Glissement a la base",
                    GoverningCombination = combinationId,
                    Comment = sliding.Justification
                };
                check.Verify(Quantity.Force(UnitConverter.NToKn(sliding.Destabilising)),
                             Quantity.Force(UnitConverter.NToKn(sliding.Stabilising)));
                result.Checks.Add(check);
            }

            double overturning = Math.Max(Math.Abs(momentAboutXNmm), Math.Abs(momentAboutYNmm));
            if (overturning > 0)
            {
                double width = Math.Abs(momentAboutYNmm) >= Math.Abs(momentAboutXNmm)
                    ? footing.WidthXMm : footing.WidthYMm;
                StabilityResult over = StabilityChecks.Overturning(overturning, totalLoadN, width);

                var check = new CheckResult
                {
                    Code = Ec7,
                    Clause = "2.4.7.2 (EQU)",
                    Equation = "M_dst <= 0,9 N B/2",
                    Description = "Renversement",
                    GoverningCombination = combinationId,
                    Comment = over.Justification
                };
                check.Verify(Quantity.Moment(over.Destabilising / 1e6),
                             Quantity.Moment(over.Stabilising / 1e6));
                result.Checks.Add(check);
            }
        }

        private static void AddBendingChecks(FootingData footing, FootingReinforcement r,
                                             double requiredX, double requiredY,
                                             double asMinX, double asMinY,
                                             BendingResult bendingX, BendingResult bendingY,
                                             string combinationId, FootingDesignResult result)
        {
            double providedX = r.BottomX.AreaPerMetreMm2 * footing.WidthYMm / 1000.0;
            double providedY = r.BottomY.AreaPerMetreMm2 * footing.WidthXMm / 1000.0;

            var checkX = new CheckResult
            {
                Code = Ec2,
                Clause = "6.1",
                Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                Description = "Flexion de la console suivant X",
                GoverningCombination = combinationId,
                Comment = string.Format("x/d = {0:0.000}, z = {1:0} mm{2}",
                    bendingX.NeutralAxisRatio, bendingX.LeverArmMm,
                    requiredX > bendingX.TensionSteelMm2 ? ", As,min gouverne" : "")
            };
            checkX.Verify(Quantity.Area(requiredX), Quantity.Area(providedX));
            result.Checks.Add(checkX);

            var checkY = new CheckResult
            {
                Code = Ec2,
                Clause = "6.1",
                Equation = "As fourni >= As requis = M_Ed / (z f_yd)",
                Description = "Flexion de la console suivant Y",
                GoverningCombination = combinationId,
                Comment = string.Format("x/d = {0:0.000}, z = {1:0} mm{2}",
                    bendingY.NeutralAxisRatio, bendingY.LeverArmMm,
                    requiredY > bendingY.TensionSteelMm2 ? ", As,min gouverne" : "")
            };
            checkY.Verify(Quantity.Area(requiredY), Quantity.Area(providedY));
            result.Checks.Add(checkY);

            var spacing = new CheckResult
            {
                Code = Ec2,
                Clause = "9.3.1.1 (3)",
                Equation = "s <= min(3h ; 400 mm)",
                Description = "Espacement des armatures principales",
                GoverningCombination = combinationId
            };
            spacing.Verify(Quantity.Length(Math.Max(r.BottomX.SpacingMm, r.BottomY.SpacingMm)),
                           Quantity.Length(Math.Min(3.0 * footing.ThicknessMm, 400.0)));
            result.Checks.Add(spacing);
        }

        private static void AddPunchingChecks(PunchingResult punching, double meanDepthMm,
                                              string combinationId, FootingDesignResult result)
        {
            var face = new CheckResult
            {
                Code = Ec2,
                Clause = "6.4.5 (3)",
                Equation = "v_Ed <= v_Rd,max = 0,5 nu f_cd",
                Description = "Poinconnement au nu du poteau",
                GoverningCombination = combinationId
            };
            face.Verify(new Quantity(punching.ColumnFaceStressMPa, "MPa", 3),
                        new Quantity(punching.MaxStressMPa, "MPa", 3));
            result.Checks.Add(face);

            if (punching.Critical == null) return;

            var critical = new CheckResult
            {
                Code = Ec2,
                Clause = "6.4.4 (2)",
                Equation = "v_Ed = (V_Ed - dV_Ed) / (u d) <= v_Rd,c x 2d/a",
                Description = "Poinconnement au perimetre critique",
                GoverningCombination = combinationId,
                Comment = string.Format(
                    "Perimetre le plus defavorable a a = {0:0} mm du nu, soit {1:0.00} d.",
                    punching.Critical.DistanceMm,
                    punching.Critical.DistanceMm / Math.Max(meanDepthMm, 1.0))
            };
            critical.WithInput("u", Quantity.Length(punching.Critical.PerimeterMm))
                    .WithInput("V_Ed reduit", Quantity.Force(punching.Critical.NetShearN / 1000.0));
            critical.Verify(new Quantity(punching.Critical.AppliedStressMPa, "MPa", 3),
                            new Quantity(punching.Critical.ResistanceMPa, "MPa", 3));
            result.Checks.Add(critical);
        }

        private static void AddOneWayShearCheck(FootingData footing, FootingReinforcement r,
                                                ConcreteProperties materials, INationalAnnex annex,
                                                double designPressureMPa, double ratioX,
                                                string combinationId, FootingDesignResult result)
        {
            // Section a la distance d du nu du poteau, article 6.2.1(8).
            double distanceFromEdge = footing.OverhangXMm - r.EffectiveDepthXMm;
            if (distanceFromEdge <= 0)
            {
                result.Notes.Add("Semelle compacte : la section a d du nu tombe hors de la " +
                                 "semelle, l'effort tranchant unidirectionnel n'est pas dimensionnant.");
                return;
            }

            double shear = designPressureMPa * footing.WidthYMm * distanceFromEdge;
            string justification;
            double resistance = ShearDesign.ShearResistanceWithoutReinforcement(footing.WidthYMm,
                r.EffectiveDepthXMm, ratioX * footing.WidthYMm * r.EffectiveDepthXMm, 0.0, materials,
                annex.GammaC, out justification);

            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "6.2.2",
                Equation = "V_Ed <= V_Rd,c a la distance d du nu",
                Description = "Effort tranchant unidirectionnel",
                GoverningCombination = combinationId,
                Comment = "Une semelle ne porte pas d'armatures d'effort tranchant : " +
                          "si V_Rd,c est depasse, il faut epaissir."
            };
            check.Verify(Quantity.Force(UnitConverter.NToKn(shear)),
                         Quantity.Force(UnitConverter.NToKn(resistance)));
            result.Checks.Add(check);

            if (check.Status == CheckStatus.Fail)
            {
                result.Warnings.Add(
                    "SECTION INSUFFISANTE a l'effort tranchant. Actions possibles : epaissir " +
                    "la semelle, la reduire en plan, ou augmenter la classe de beton.");
            }
        }

        private static void AddStarterAnchorageCheck(FootingData footing, FootingReinforcement r,
                                                     double starterAnchorageMm,
                                                     string combinationId, FootingDesignResult result)
        {
            // La hauteur disponible pour l'ancrage vertical des attentes.
            double available = footing.ThicknessMm - r.CoverMm - r.BottomX.DiameterMm
                               - r.BottomY.DiameterMm;
            var check = new CheckResult
            {
                Code = Ec2,
                Clause = "8.4.4",
                Equation = "hauteur disponible >= l_bd de l'attente",
                Description = "Ancrage des attentes dans la semelle",
                GoverningCombination = combinationId,
                Comment = "Un retour horizontal en pied complete l'ancrage lorsque la hauteur " +
                          "ne suffit pas."
            };
            check.Verify(Quantity.Length(starterAnchorageMm), Quantity.Length(available));
            if (check.Status == CheckStatus.Fail)
            {
                check.Status = CheckStatus.Warning;
                check.Comment += " Ici la hauteur seule ne suffit pas : le retour horizontal de "
                                 + string.Format("{0:0} mm est indispensable.", r.StarterReturnMm);
            }
            result.Checks.Add(check);
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static LoadCombination ResolveCombination(IReadOnlyList<LoadCombination> combinations,
                                                          FootingDesignSettings settings)
        {
            if (combinations != null)
            {
                foreach (LoadCombination candidate in combinations)
                {
                    if (candidate.IsUltimate && candidate.Stations.Count > 0) return candidate;
                }
            }

            var forces = new InternalForces(
                UnitConverter.KnToN(settings.AxialLoadKn),
                UnitConverter.KnToN(settings.ShearYKn),
                UnitConverter.KnToN(settings.ShearXKn),
                0.0,
                UnitConverter.KnmToNmm(settings.MomentAboutYKnm),
                UnitConverter.KnmToNmm(settings.MomentAboutXKnm));
            return LoadCombination.Single("ULS-COMB-001", DesignSituation.UltimateFundamental, forces);
        }

        private static CoverResult ResolveCover(FootingDesignSettings settings, double barDiameterMm)
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

        private static string MarkPrefix(FootingData footing)
        {
            return string.IsNullOrWhiteSpace(footing.Mark) ? "FT" : footing.Mark;
        }

        private static double RoundUpTo(double value, double step)
        {
            return Math.Ceiling(value / step) * step;
        }
    }
}
