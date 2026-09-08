using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Reinforcement.Plan;
using DanCI.Structural.Reinforcement.Schedule;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Entete d'une note de calcul DanCI Structural Studio.</summary>
    public sealed class ReportHeader
    {
        public string ProjectName { get; set; }
        public string StructuralCode { get; set; }
        public string NationalAnnex { get; set; }
        public string ApplicationVersion { get; set; }
        public string EngineVersion { get; set; }

        public ReportHeader()
        {
            ProjectName = "Projet";
            StructuralCode = "EN 1992-1-1:2004+A1:2014";
            NationalAnnex = "Valeurs recommandees";
            ApplicationVersion = "3.0.0";
            EngineVersion = "1.0.0";
        }
    }

    /// <summary>
    /// Produit la note de calcul : hypotheses, verifications avec leur clause et leur
    /// equation, ferraillage retenu et quantitatif. Elle ne se contente jamais d'affirmer
    /// « conforme Eurocode » : chaque ligne porte sa demande, sa resistance et son taux.
    /// </summary>
    public static class CalculationReport
    {
        public static string Build(ReportHeader header, IEnumerable<ColumnReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (ColumnReportItem item in items)
            {
                sb.Append(BuildElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Column.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        private static string Preamble(ReportHeader header)
        {
            var sb = new StringBuilder();
            sb.AppendLine("DanCI Structural Studio");
            sb.AppendLine("Structural Design & Reinforcement Automation for Autodesk Revit");
            sb.AppendLine(new string('=', 78));
            sb.AppendLine("Projet             : " + header.ProjectName);
            sb.AppendLine("Norme              : " + header.StructuralCode);
            sb.AppendLine("Annexe Nationale   : " + header.NationalAnnex);
            sb.AppendLine("Version logiciel   : " + header.ApplicationVersion);
            sb.AppendLine("Version moteur     : " + header.EngineVersion);
            sb.AppendLine("Genere le          : " +
                          DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture));
            sb.AppendLine(new string('=', 78));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>
        /// Carnet de ferraillage : un repere par FORME faconnee, partage par tous les
        /// elements qui l'emploient, et une longueur de COUPE et non un developpe d'angle
        /// a angle.
        /// </summary>
        private static string Schedule(IEnumerable<ScheduledElement> elements)
        {
            BarSchedule schedule = BarScheduleBuilder.Build(elements);
            if (schedule.Rows.Count == 0) return string.Empty;

            var sb = new StringBuilder();
            sb.AppendLine(new string('=', 78));
            sb.AppendLine("CARNET DE FERRAILLAGE");
            sb.AppendLine();
            sb.AppendLine("  Un repere designe une FORME faconnee, pas une barre : toutes les");
            sb.AppendLine("  barres identiques du lot le partagent, quel que soit l'element qui");
            sb.AppendLine("  les porte. La longueur est celle de COUPE, plis deduits selon");
            sb.AppendLine("  l'EN 1992-1-1 art. 8.3, et non le developpe d'angle a angle.");
            sb.AppendLine();
            sb.AppendLine("  Rep.  Diam.  Forme            Cotes                 Coupe   Nb    Masse");
            sb.AppendLine("  " + new string('-', 74));

            foreach (BarScheduleRow row in schedule.Rows)
            {
                sb.AppendLine(string.Format(
                    "  {0,-5} HA{1,-4:0} {2,-16} {3,-20} {4,6:0}  {5,4}  {6,7:0.0} kg",
                    row.Mark, row.DiameterMm, Truncate(row.ShapeLabel, 16),
                    Truncate(row.DimensionsLabel, 20), row.CutLengthMm, row.Count,
                    row.TotalMassKg));

                if (row.Uses.Count > 1)
                {
                    var names = new List<string>();
                    foreach (BarScheduleUse use in row.Uses)
                    {
                        names.Add(string.Format("{0} ({1})", use.ElementName, use.Count));
                    }
                    sb.AppendLine("        partage par : " + string.Join(", ", names));
                }

                if (row.BendDeductionMm > 0.05)
                {
                    sb.AppendLine(string.Format(
                        "        developpe {0:0} mm, {1} pli(s) sur mandrin {2:0} mm = " +
                        "-{3:0.0} mm{4}",
                        row.PolylineLengthMm, row.BendAnglesDegrees.Count,
                        row.MandrelDiameterMm, row.BendDeductionMm,
                        row.HookAllowanceMm > 0
                            ? string.Format(", crochets +{0:0} mm", row.HookAllowanceMm)
                            : string.Empty));
                }

                if (row.HasBendBeyondModel)
                {
                    sb.AppendLine("        ! un pli depasse ce que le modele sait deduire : "
                                  + "verifiez la longueur de coupe.");
                }
            }

            sb.AppendLine("  " + new string('-', 74));
            sb.AppendLine(string.Format(
                "  {0} forme(s) distincte(s), {1} barres, {2:0.0} m, {3:0.0} kg",
                schedule.DistinctShapes, schedule.TotalBarCount, schedule.TotalLengthM,
                schedule.TotalMassKg));
            sb.AppendLine();
            return sb.ToString();
        }

        private static string Truncate(string text, int length)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            return text.Length <= length ? text : text.Substring(0, length);
        }

        private static string Total(SteelQuantities total)        private static string Total(SteelQuantities total)
        {
            var sb = new StringBuilder();
            if (total.TotalMassKg > 0)
            {
                sb.AppendLine(new string('=', 78));
                sb.AppendLine("TOTAL");
                sb.AppendLine(string.Format(
                    "  Acier : {0:0.0} kg pour {1:0.000} m3 de beton, soit {2:0} kg/m3",
                    total.TotalMassKg, total.ConcreteVolumeM3, total.RatioKgPerM3));
                sb.AppendLine("  Repartition : " + total.DiameterBreakdown());
            }

            sb.AppendLine();
            sb.AppendLine("Genere avec DanCI Structural Studio. Le resultat est un avant-projet de");
            sb.AppendLine("ferraillage : il doit etre verifie et valide par l'ingenieur responsable.");
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un seul poteau.</summary>
        public static string BuildElement(ColumnReportItem item)
        {
            ColumnDesignResult result = item.Result;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Column.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine(string.Format("  Section {0} mm - Hauteur {1:0} mm - Ac = {2:0} mm2",
                result.Column.SectionLabel, result.Column.HeightMm, result.Column.GrossAreaMm2));
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine("  Longitudinales : " + result.Reinforcement.LongitudinalLabel);
            sb.AppendLine("  Transversales  : " + result.Reinforcement.TransverseLabel);
            sb.AppendLine(string.Format("  Recouvrement   : {0:0} mm",
                result.Reinforcement.LapLengthMm));
            sb.AppendLine(string.Format("  Taux d'armature: {0:0.00} %", result.SteelRatioPercent));
            sb.AppendLine();

            if (item.Quantities != null && item.Quantities.TotalMassKg > 0)
            {
                SteelQuantities q = item.Quantities;
                sb.AppendLine("QUANTITATIF");
                sb.AppendLine(string.Format(
                    "  Longitudinales : {0} barres de {1:0} mm = {2:0.0} m, {3:0.0} kg",
                    q.LongitudinalBarCount, q.LongitudinalCutLengthMm, q.LongitudinalLengthM,
                    q.LongitudinalMassKg));
                sb.AppendLine(string.Format(
                    "  Cadres         : {0} unites de {1:0} mm developpes = {2:0.0} m, {3:0.0} kg",
                    q.StirrupCount, q.StirrupCutLengthMm, q.StirrupLengthM, q.StirrupMassKg));
                if (q.CrossTieCount > 0)
                {
                    sb.AppendLine(string.Format("  Epingles       : {0} unites = {1:0.0} m, {2:0.0} kg",
                        q.CrossTieCount, q.CrossTieLengthM, q.CrossTieMassKg));
                }
                sb.AppendLine(string.Format(
                    "  Total          : {0:0.0} kg pour {1:0.000} m3, soit {2:0} kg/m3",
                    q.TotalMassKg, q.ConcreteVolumeM3, q.RatioKgPerM3));
                sb.AppendLine();
            }

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de poutres.</summary>
        public static string BuildBeams(ReportHeader header, IEnumerable<BeamReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (BeamReportItem item in items)
            {
                sb.Append(BuildBeamElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Beam.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule poutre.</summary>
        public static string BuildBeamElement(BeamReportItem item)
        {
            BeamDesignResult result = item.Result;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Beam.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine(string.Format("  Section {0} mm - Portee {1:0} mm",
                result.Beam.SectionLabel, result.Beam.SpanMm));
            sb.AppendLine(string.Format("  Hauteur utile d = {0:0} mm - Enrobage {1:0} mm",
                result.Reinforcement.EffectiveDepthMm, result.Reinforcement.CoverMm));
            if (result.EffectiveFlangeWidthMm > result.Beam.WebWidthMm)
            {
                sb.AppendLine(string.Format("  Largeur participante de table b_eff = {0:0} mm",
                    result.EffectiveFlangeWidthMm));
            }
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format("  Travee         : {0} (As requis {1:0} mm2)",
                result.Reinforcement.BottomSpan.Label, result.SpanSteelRequiredMm2));
            if (result.Reinforcement.TopLeft.Count > 0)
            {
                sb.AppendLine(string.Format("  Appui gauche   : {0} (As requis {1:0} mm2)",
                    result.Reinforcement.TopLeft.Label, result.LeftSteelRequiredMm2));
            }
            if (result.Reinforcement.TopRight.Count > 0)
            {
                sb.AppendLine(string.Format("  Appui droit    : {0} (As requis {1:0} mm2)",
                    result.Reinforcement.TopRight.Label, result.RightSteelRequiredMm2));
            }
            sb.AppendLine("  Montage        : " + result.Reinforcement.TopContinuous.Label);
            foreach (BeamStirrupZone zone in result.Reinforcement.StirrupZones)
            {
                sb.AppendLine(string.Format(
                    "  Cadres {0,-13}: HA{1:0} a {2} brins, e = {3:0} mm de {4:0} a {5:0} mm " +
                    "(V_Ed = {6:0} kN)",
                    zone.Label, result.Reinforcement.StirrupDiameterMm,
                    result.Reinforcement.StirrupLegs, zone.SpacingMm, zone.StartMm, zone.EndMm,
                    zone.DesignShearN / 1000.0));
            }
            sb.AppendLine(string.Format("  Ancrage l_bd   : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                result.Reinforcement.AnchorageLengthMm, result.Reinforcement.LapLengthMm));
            sb.AppendLine(string.Format("  Decalage a_l   : {0:0} mm - Chapeaux : {1:0} mm",
                result.Reinforcement.ShiftLengthMm, result.Reinforcement.TopBarLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de semelles isolees.</summary>
        public static string BuildFootings(ReportHeader header, IEnumerable<FootingReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (FootingReportItem item in items)
            {
                sb.Append(BuildFootingElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Footing.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule semelle isolee.</summary>
        public static string BuildFootingElement(FootingReportItem item)
        {
            FootingDesignResult result = item.Result;
            FootingReinforcement r = result.Reinforcement;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Footing.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine(string.Format("  Semelle {0} mm - Poteau porte {1:0} x {2:0} mm",
                result.Footing.SectionLabel, result.Footing.ColumnWidthXMm,
                result.Footing.ColumnWidthYMm));
            sb.AppendLine(string.Format("  Debords {0:0} mm (X) et {1:0} mm (Y)",
                result.Footing.OverhangXMm, result.Footing.OverhangYMm));
            sb.AppendLine(string.Format("  Enrobage {0:0} mm - d_x = {1:0} mm - d_y = {2:0} mm",
                r.CoverMm, r.EffectiveDepthXMm, r.EffectiveDepthYMm));
            sb.AppendLine();

            if (result.Pressure != null)
            {
                sb.AppendLine("CONTRAINTES SOUS LA SEMELLE");
                sb.AppendLine(string.Format(
                    "  Distribution lineaire : {0:0} / {1:0} kPa - excentricites {2:0} / {3:0} mm",
                    result.Pressure.MaxPressureKpa, result.Pressure.MinPressureKpa,
                    result.Pressure.EccentricityXMm, result.Pressure.EccentricityYMm));
                sb.AppendLine(string.Format(
                    "  Aire effective B' x L' = {0:0} x {1:0} mm  ->  sigma' = {2:0} kPa",
                    result.Pressure.EffectiveWidthMm, result.Pressure.EffectiveLengthMm,
                    result.Pressure.EffectivePressureKpa));
                sb.AppendLine(string.Format(
                    "  Contrainte nette retenue pour le calcul structurel : {0:0} kPa",
                    result.Pressure.NetPressureKpa));
                sb.AppendLine();
            }

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Nappe inferieure // X : {0} ({1:0} mm2/m fournis pour {2:0} mm2/m requis)",
                r.BottomX.Label, r.BottomX.AreaPerMetreMm2, result.RequiredSteelXMm2PerM));
            sb.AppendLine(string.Format(
                "  Nappe inferieure // Y : {0} ({1:0} mm2/m fournis pour {2:0} mm2/m requis)",
                r.BottomY.Label, r.BottomY.AreaPerMetreMm2, result.RequiredSteelYMm2PerM));
            if (r.HasTopMesh)
            {
                sb.AppendLine(string.Format("  Nappe superieure      : {0} // X, {1} // Y",
                    r.TopX.Label, r.TopY.Label));
            }
            if (r.StarterBarCount > 0)
            {
                sb.AppendLine(string.Format(
                    "  Attentes              : {0}, retour {1:0} mm, depassement {2:0} mm",
                    r.StarterLabel, r.StarterReturnMm, r.StarterProjectionMm));
            }
            sb.AppendLine(string.Format("  Ancrage l_bd          : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de dalles.</summary>
        public static string BuildSlabs(ReportHeader header, IEnumerable<SlabReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (SlabReportItem item in items)
            {
                sb.Append(BuildSlabElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Slab.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule dalle.</summary>
        public static string BuildSlabElement(SlabReportItem item)
        {
            SlabDesignResult result = item.Result;
            SlabReinforcement r = result.Reinforcement;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Slab.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine(string.Format("  {0} - panneau {1:0} x {2:0} mm - {3}",
                result.Slab.SectionLabel, result.Slab.SpanMm, result.Slab.WidthMm,
                result.Slab.SpanKind));
            sb.AppendLine(string.Format("  Enrobage {0:0} mm - d = {1:0} mm - bande de calcul 1 000 mm",
                r.CoverMm, r.EffectiveDepthMm));
            sb.AppendLine();

            sb.AppendLine("ACTIONS ET SOLLICITATIONS");
            if (result.UltimateLoadKnM2 > 0)
            {
                sb.AppendLine(string.Format(
                    "  Charge ELU {0:0.00} kN/m2 - charge quasi-permanente {1:0.00} kN/m2",
                    result.UltimateLoadKnM2, result.QuasiPermanentLoadKnM2));
            }
            sb.AppendLine(string.Format(
                "  M travee {0:0.0} kN.m/m - M appui {1:0.0} kN.m/m - V {2:0.0} kN/m",
                result.SpanMomentKnmPerM, result.SupportMomentKnmPerM, result.ShearKnPerM));
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Nappe inferieure porteuse : {0} ({1:0} mm2/m fournis pour {2:0} mm2/m requis)",
                r.BottomMain.Label, r.BottomMain.AreaPerMetreMm2,
                result.SpanSteelRequiredMm2PerM));
            sb.AppendLine(string.Format(
                "  Repartition inferieure    : {0} ({1:0} mm2/m, minimum 20 % soit {2:0} mm2/m)",
                r.BottomTransverse.Label, r.BottomTransverse.AreaPerMetreMm2,
                0.2 * r.BottomMain.AreaPerMetreMm2));
            if (r.HasTopReinforcement)
            {
                sb.AppendLine(string.Format(
                    "  Chapeaux                  : {0} sur {1:0} mm depuis le nu d'appui " +
                    "({2:0} mm2/m requis)",
                    r.TopMain.Label, r.TopBarLengthMm, result.SupportSteelRequiredMm2PerM));
                sb.AppendLine("  Repartition superieure    : " + r.TopTransverse.Label);
            }
            sb.AppendLine(string.Format("  Ancrage l_bd              : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de voiles.</summary>
        public static string BuildWalls(ReportHeader header, IEnumerable<WallReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (WallReportItem item in items)
            {
                sb.Append(BuildWallElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Wall.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un seul voile.</summary>
        public static string BuildWallElement(WallReportItem item)
        {
            WallDesignResult result = item.Result;
            WallReinforcement r = result.Reinforcement;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Wall.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine("  " + result.Wall.SectionLabel);
            sb.AppendLine(string.Format(
                "  Enrobage {0:0} mm - d = {1:0} mm - bande de calcul verticale 1 000 mm",
                r.CoverMm, r.EffectiveDepthMm));
            sb.AppendLine();

            sb.AppendLine("FLAMBEMENT HORS PLAN");
            if (result.Buckling != null)
            {
                sb.AppendLine("  " + result.Buckling.Justification);
            }
            sb.AppendLine(string.Format("  lambda = {0:0.0}", result.SlendernessRatio));
            if (result.SecondOrder != null)
            {
                sb.AppendLine("  " + result.SecondOrder.Justification);
            }
            sb.AppendLine(string.Format("  M_Ed hors plan retenu : {0:0.0} kN.m/m",
                result.DesignOutOfPlaneMomentKnmPerM));
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Aciers verticaux   : {0} - total {1:0} mm2/m (requis {2:0} mm2/m)",
                r.VerticalLabel, r.VerticalTotalMm2PerM, result.VerticalSteelRequiredMm2PerM));
            sb.AppendLine(string.Format(
                "  Aciers horizontaux : {0} - total {1:0} mm2/m",
                r.HorizontalLabel, r.HorizontalTotalMm2PerM));
            if (r.HasEdgeBars)
            {
                sb.AppendLine(string.Format(
                    "  Barres de rive     : {0} (requis {1:0} mm2 par extremite)",
                    r.EdgeLabel, result.EdgeSteelRequiredMm2));
            }
            if (r.HasLinks)
            {
                sb.AppendLine(string.Format(
                    "  Epingles           : HA{0:0}, {1:0.0} au m2 (art. 9.6.4)",
                    r.LinkDiameterMm, r.LinksPerSquareMetre));
            }
            sb.AppendLine(string.Format("  Ancrage l_bd       : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de semelles filantes.</summary>
        public static string BuildStripFootings(ReportHeader header,
                                                IEnumerable<StripFootingReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (StripFootingReportItem item in items)
            {
                sb.Append(BuildStripFootingElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Footing.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule semelle filante.</summary>
        public static string BuildStripFootingElement(StripFootingReportItem item)
        {
            StripFootingDesignResult result = item.Result;
            StripFootingReinforcement r = result.Reinforcement;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Footing.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine("  " + result.Footing.SectionLabel);
            sb.AppendLine(string.Format(
                "  Longueur {0:0} mm - debord {1:0} mm de chaque cote - semelle {2}",
                result.Footing.LengthMm, result.Footing.OverhangMm,
                result.Footing.IsRigid ? "rigide" : "SOUPLE"));
            sb.AppendLine(string.Format(
                "  Enrobage {0:0} mm - d = {1:0} mm - tranche de calcul 1 000 mm",
                r.CoverMm, r.EffectiveDepthMm));
            sb.AppendLine();

            if (result.Pressure != null)
            {
                sb.AppendLine("CONTRAINTES SOUS LA SEMELLE");
                sb.AppendLine(string.Format(
                    "  Distribution lineaire : {0:0} / {1:0} kPa - excentricite {2:0} mm",
                    result.Pressure.MaxPressureKpa, result.Pressure.MinPressureKpa,
                    result.Pressure.EccentricityXMm));
                sb.AppendLine(string.Format(
                    "  Largeur effective B' = {0:0} mm  ->  sigma' = {1:0} kPa",
                    result.Pressure.EffectiveWidthMm, result.Pressure.EffectivePressureKpa));
                sb.AppendLine(string.Format(
                    "  Contrainte nette structurelle : {0:0} kPa",
                    result.Pressure.NetPressureKpa));
                sb.AppendLine();
            }

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Transversales inferieures : {0} ({1:0} mm2/m fournis pour {2:0} mm2/m requis){3}",
                r.TransverseLabel, r.Transverse.AreaPerMetreMm2,
                result.TransverseSteelRequiredMm2PerM,
                r.TransverseNeedsHook ? " AVEC CROCHETS D'EXTREMITE" : ""));
            sb.AppendLine(string.Format(
                "  Repartition longitudinale : {0} ({1:0} mm2/m, minimum 20 % soit {2:0} mm2/m)",
                r.LongitudinalLabel, r.Longitudinal.AreaPerMetreMm2,
                0.2 * r.Transverse.AreaPerMetreMm2));
            if (r.HasTopMesh)
            {
                sb.AppendLine("  Transversales superieures : " + r.TopTransverse.Label);
            }
            if (r.HasStarters)
            {
                sb.AppendLine(string.Format(
                    "  Attentes de voile         : {0}, retour {1:0} mm, depassement {2:0} mm",
                    r.StarterLabel, r.StarterReturnMm, r.StarterProjectionMm));
            }
            sb.AppendLine(string.Format("  Ancrage l_bd              : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de longrines.</summary>
        public static string BuildGradeBeams(ReportHeader header,
                                             IEnumerable<GradeBeamReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (GradeBeamReportItem item in items)
            {
                sb.Append(BuildGradeBeamElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Beam.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule longrine.</summary>
        public static string BuildGradeBeamElement(GradeBeamReportItem item)
        {
            GradeBeamDesignResult result = item.Result;
            GradeBeamReinforcement r = result.Reinforcement;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + result.Beam.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine("  " + result.Beam.SectionLabel);
            sb.AppendLine(string.Format(
                "  Enrobage {0:0} mm - d = {1:0} mm - portee sur hauteur {2:0.0}",
                r.CoverMm, r.EffectiveDepthMm, result.Beam.SpanToDepth));
            sb.AppendLine();

            sb.AppendLine("ACTIONS ET SOLLICITATIONS");
            sb.AppendLine(string.Format("  Charge de calcul : {0:0.00} kN/m",
                result.DesignLoadKnPerM));
            sb.AppendLine(string.Format("  M travee {0:0.0} kN.m - V appui {1:0.0} kN",
                result.SpanMomentKnm, result.ShearKn));
            sb.AppendLine(string.Format("  Effort de liaison : {0}",
                result.TieForceKn > 0
                    ? string.Format("+- {0:0.0} kN, alterne", result.TieForceKn)
                    : "aucun"));
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Nappe inferieure : {0} ({1:0} mm2 fournis pour {2:0} mm2 requis)",
                r.BottomBars.Label, r.BottomBars.AreaMm2, result.BottomSteelRequiredMm2));
            sb.AppendLine(string.Format(
                "  Nappe superieure : {0} ({1:0} mm2 fournis pour {2:0} mm2 requis)",
                r.TopBars.Label, r.TopBars.AreaMm2, result.TopSteelRequiredMm2));
            sb.AppendLine(string.Format(
                "  Cadres           : {0} ({1} unites)",
                r.TransverseLabel, r.StirrupCount(result.Beam.SpanMm)));
            sb.AppendLine(string.Format(
                "  Ancrage l_bd     : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        /// <summary>Note de calcul d'un ensemble de volees d'escalier.</summary>
        public static string BuildStairs(ReportHeader header,
                                         IEnumerable<StairReportItem> items)
        {
            var sb = new StringBuilder();
            sb.Append(Preamble(header));

            var total = new SteelQuantities();
            var scheduled = new List<ScheduledElement>();
            foreach (StairReportItem item in items)
            {
                sb.Append(BuildStairElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
                scheduled.Add(new ScheduledElement(item.Result.Stair.Name,
                                                   item.Result.Plan));
            }

            sb.Append(Schedule(scheduled));
            sb.Append(Total(total));
            return sb.ToString();
        }

        /// <summary>Note de calcul d'une seule volee.</summary>
        public static string BuildStairElement(StairReportItem item)
        {
            StairDesignResult result = item.Result;
            StairReinforcement r = result.Reinforcement;
            StairData stair = result.Stair;
            var sb = new StringBuilder();

            sb.AppendLine("ELEMENT : " + stair.Name);
            sb.AppendLine(new string('-', 78));
            sb.AppendLine("GEOMETRIE");
            sb.AppendLine("  " + stair.SectionLabel);
            sb.AppendLine(string.Format(
                "  Denivele {0:0} mm - projection {1:0} mm - palier {2:0} mm - portee {3:0} mm",
                stair.TotalRiseMm, stair.TotalGoingMm, stair.LandingSpanMm, stair.SpanMm));
            sb.AppendLine(string.Format(
                "  Enrobage {0:0} mm - d = {1:0} mm (mesure sur l'epaisseur de paillasse)",
                r.CoverMm, r.EffectiveDepthMm));
            sb.AppendLine();

            sb.AppendLine("DESCENTE DE CHARGE");
            if (result.FlightLoad != null)
            {
                sb.AppendLine(string.Format(
                    "  Volee  : paillasse {0:0.000} + marches {1:0.000} + revetement {2:0.000} " +
                    "+ sous-face {3:0.000} = {4:0.000} kN/m2",
                    result.FlightLoad.WaistKnM2, result.FlightLoad.StepsKnM2,
                    result.FlightLoad.TreadFinishKnM2, result.FlightLoad.SoffitFinishKnM2,
                    result.FlightLoad.PermanentKnM2));
            }
            if (result.LandingLoad != null)
            {
                sb.AppendLine(string.Format("  Palier : {0:0.000} kN/m2",
                    result.LandingLoad.PermanentKnM2));
            }
            sb.AppendLine(string.Format(
                "  ELU : volee {0:0.000} kN/m2, palier {1:0.000} kN/m2",
                result.FlightUltimateLoadKnM2, result.LandingUltimateLoadKnM2));
            sb.AppendLine(string.Format("  M travee {0:0.0} kN.m/m - V appui {1:0.0} kN/m",
                result.SpanMomentKnmPerM, result.ShearKnPerM));
            sb.AppendLine();

            sb.AppendLine("HYPOTHESES ET CHOIX");
            foreach (string note in result.Notes) sb.AppendLine("  - " + note);
            sb.AppendLine();

            if (result.Checks.Count > 0)
            {
                sb.AppendLine("VERIFICATIONS");
                foreach (CheckResult check in result.Checks) sb.Append(FormatCheck(check));
                sb.AppendLine();
            }

            sb.AppendLine("ARMATURES RETENUES");
            sb.AppendLine(string.Format(
                "  Nappe inferieure : {0} ({1:0} mm2/m fournis pour {2:0} mm2/m requis)",
                r.BottomMain.Label, r.BottomMain.AreaPerMetreMm2,
                result.SpanSteelRequiredMm2PerM));
            sb.AppendLine(string.Format("  Repartition      : {0} ({1:0} mm2/m)",
                r.BottomTransverse.Label, r.BottomTransverse.AreaPerMetreMm2));
            sb.AppendLine(string.Format("  Chapeaux         : {0}{1}",
                r.TopLabel,
                r.HasTopReinforcement
                    ? string.Format(", longueur {0:0} mm depuis le nu", r.TopBarLengthMm)
                    : string.Empty));
            sb.AppendLine(string.Format(
                "  Ancrage l_bd     : {0:0} mm - Recouvrement l_0 : {1:0} mm",
                r.AnchorageLengthMm, r.LapLengthMm));
            sb.AppendLine(string.Format("  Noeud volee-palier : {0}",
                r.HasKneeJoint
                    ? string.Format("angle rentrant tendu, nappes CROISEES et ancrees sur " +
                                    "{0:0} mm au-dela du pli", r.KneeAnchorageMm)
                    : "sans objet"));
            sb.AppendLine();

            sb.Append(FormatQuantities(item.Quantities));

            if (result.Warnings.Count > 0)
            {
                sb.AppendLine("POINTS A REPRENDRE");
                foreach (string warning in result.Warnings) sb.AppendLine("  ! " + warning);
                sb.AppendLine();
            }

            sb.AppendLine(string.Format("CONCLUSION : {0} (taux de travail maximal {1:0.00})",
                result.Status, result.MaxUtilization));
            sb.AppendLine();
            return sb.ToString();
        }

        private static string FormatQuantities(SteelQuantities q)
        {
            if (q == null || q.TotalMassKg <= 0) return string.Empty;
            var sb = new StringBuilder();
            sb.AppendLine("QUANTITATIF");
            sb.AppendLine(string.Format(
                "  Longitudinales : {0} barres = {1:0.0} m, {2:0.0} kg",
                q.LongitudinalBarCount, q.LongitudinalLengthM, q.LongitudinalMassKg));
            sb.AppendLine(string.Format(
                "  Cadres         : {0} unites de {1:0} mm developpes = {2:0.0} m, {3:0.0} kg",
                q.StirrupCount, q.StirrupCutLengthMm, q.StirrupLengthM, q.StirrupMassKg));
            sb.AppendLine(string.Format(
                "  Total          : {0:0.0} kg pour {1:0.000} m3, soit {2:0} kg/m3",
                q.TotalMassKg, q.ConcreteVolumeM3, q.RatioKgPerM3));
            sb.AppendLine();
            return sb.ToString();
        }

        private static string FormatCheck(CheckResult check)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("  [{0}] {1}", check.StatusLabel, check.Description));
            sb.AppendLine(string.Format("      {0} art. {1} : {2}", check.Code, check.Clause,
                check.Equation));
            if (check.Inputs.Count > 0)
            {
                var parts = new List<string>();
                foreach (var input in check.Inputs) parts.Add(input.Key + " = " + input.Value);
                sb.AppendLine("      Donnees : " + string.Join(" ; ", parts));
            }
            sb.AppendLine(string.Format("      Sollicitation {0} / Resistance {1} -> taux {2:0.00}",
                check.Demand, check.Resistance, check.Utilization));
            if (!string.IsNullOrEmpty(check.GoverningCombination))
            {
                sb.AppendLine("      Combinaison : " + check.GoverningCombination);
            }
            if (!string.IsNullOrEmpty(check.Comment))
            {
                sb.AppendLine("      " + check.Comment);
            }
            return sb.ToString();
        }
    }
}
