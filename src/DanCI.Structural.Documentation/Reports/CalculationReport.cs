using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Reinforcement.Plan;

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
            foreach (ColumnReportItem item in items)
            {
                sb.Append(BuildElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
            }

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

        private static string Total(SteelQuantities total)
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
            foreach (BeamReportItem item in items)
            {
                sb.Append(BuildBeamElement(item));
                if (item.Quantities != null) total.Merge(item.Quantities);
            }

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
