using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export.Csv;
using DanCI.BOQ.RevitAdapter.Extraction;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>
    /// Diagnostic du metre (etape 3) : extrait beton, maconnerie et menuiseries du modele entier,
    /// en LECTURE SEULE, et ecrit trois CSV a cote du projet :
    ///   ..._metre_brut.csv : une ligne par quantite d'element, avant toute regle ;
    ///   ..._BOQ_lignes.csv : les lignes du BOQ calculees avec la configuration installee ;
    ///   ..._anomalies.csv  : extraction, configuration et moteur.
    /// C'est l'outil de controle contre un metre manuel, element par element.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class DiagnosticCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Document doc = commandData.Application.ActiveUIDocument?.Document;
            if (doc == null || doc.IsFamilyDocument)
            {
                message = "Ouvrez un projet Revit (pas une famille).";
                return Result.Cancelled;
            }

            try
            {
                return Run(doc);
            }
            catch (Exception ex)
            {
                BoqLog.Error("Diagnostic du métré en échec.", ex);
                message = ex.Message + Environment.NewLine + "Journal : " + BoqLog.CurrentFile;
                return Result.Failed;
            }
        }

        private static Result Run(Document doc)
        {
            string configDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config");
            LoadResult load = ConfigurationLoader.LoadDirectory(configDir);
            ExtractionSettings extraction = load.Configuration?.Extraction ?? new ExtractionSettings();

            BoqLog.Info("Diagnostic du métré : " + doc.Title + " — configuration " + configDir);
            ExtractionReport report = ModelExtractor.Extract(doc, extraction);
            BoqLog.Info("Extraction : " + report.Quantities.Count + " quantités en " + report.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");

            var issues = new List<BoqIssue>(load.Issues);
            issues.AddRange(report.Notes);

            BoqResult boq = null;
            if (!load.HasErrors)
            {
                try
                {
                    boq = new BoqEngine().Build(report.Quantities, load.Configuration);
                    issues.AddRange(boq.Issues);
                }
                catch (BoqConfigurationException ex)
                {
                    issues.AddRange(ex.Issues);
                }
            }

            // Le chargeur et le moteur valident tous deux la configuration : une anomalie n'est listee qu'une fois.
            issues = issues
                .GroupBy(i => i.Severity + "|" + i.Code + "|" + i.File + "|" + i.ArticleCode + "|" + i.Message + "|" + string.Join(",", i.ElementIds))
                .Select(g => g.First())
                .ToList();

            string folder = OutputFolder(doc);
            string stem = Path.Combine(folder, SafeName(doc.Title) + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            string rawPath = stem + "_metre_brut.csv";
            string linesPath = stem + "_BOQ_lignes.csv";
            string issuesPath = stem + "_anomalies.csv";

            CsvFormat format = CsvFormat.ExcelFrancais;
            QuantityCsvWriter.WriteFile(rawPath, report.Quantities, format);
            if (boq != null)
            {
                BoqLinesCsvWriter.WriteFile(linesPath, boq, format);
            }
            IssuesCsvWriter.WriteFile(issuesPath, issues, format);
            BoqLog.Info("Diagnostic écrit : " + rawPath);

            ShowSummary(doc, report, boq, issues, load, folder, rawPath, boq != null ? linesPath : null, issuesPath);
            return Result.Succeeded;
        }

        private static void ShowSummary(Document doc, ExtractionReport report, BoqResult boq, List<BoqIssue> issues,
            LoadResult load, string folder, string rawPath, string linesPath, string issuesPath)
        {
            CultureInfo fr = CultureInfo.GetCultureInfo("fr-FR");
            var text = new StringBuilder();
            text.AppendLine("Quantités extraites (modèle hôte entier, toutes phases) :");
            foreach (var g in report.Quantities.GroupBy(q => new { q.Kind, q.Unit }).OrderBy(g => g.Key.Kind, StringComparer.Ordinal))
            {
                text.AppendLine("  • " + g.Key.Kind + " : " + g.Count() + " quantité(s), total " +
                                g.Sum(q => q.Value).ToString("N3", fr) + " " + g.Key.Unit.Symbol() +
                                (g.Any(q => q.Reliability != Reliability.Measured) ? "  (dont estimées : " + g.Count(q => q.Reliability != Reliability.Measured) + ")" : string.Empty));
            }
            if (report.Quantities.Count == 0)
            {
                text.AppendLine("  (aucune)");
            }
            foreach (KeyValuePair<string, int> s in report.Skipped)
            {
                text.AppendLine("  Ignorés — " + s.Key + " : " + s.Value);
            }
            int excluded = report.Quantities.Count(q => q.ExcludedByUser);
            if (excluded > 0)
            {
                text.AppendLine("  Marqués DanCI_BOQ_Exclure : " + excluded);
            }

            text.AppendLine();
            if (boq != null)
            {
                text.AppendLine("BOQ : " + boq.AllLines.Count() + " ligne(s), dont " + boq.AllLines.Count(l => l.IsUnclassified) + " non classée(s).");
            }
            else
            {
                text.AppendLine("BOQ non calculé : la configuration contient des erreurs (voir le fichier des anomalies).");
            }
            text.AppendLine("Anomalies : " + issues.Count(i => i.Severity == IssueSeverity.Error) + " erreur(s), " +
                            issues.Count(i => i.Severity == IssueSeverity.Warning) + " avertissement(s).");
            text.AppendLine("Durée de l'extraction : " + report.Duration.TotalSeconds.ToString("0.0", fr) + " s.");

            var dialog = new TaskDialog("Diagnostic du métré — " + doc.Title)
            {
                MainInstruction = "Métré brut écrit (" + report.Quantities.Count + " quantités)",
                MainContent = text.ToString(),
                ExpandedContent =
                    "Fichiers :" + Environment.NewLine + "  " + rawPath + Environment.NewLine +
                    (linesPath != null ? "  " + linesPath + Environment.NewLine : string.Empty) +
                    "  " + issuesPath + Environment.NewLine + Environment.NewLine +
                    "Configuration : " + (load.HasErrors ? "ERREURS — " : string.Empty) + "dossier config de l'extension." + Environment.NewLine +
                    "Lecture seule : le modèle n'a pas été modifié.",
                FooterText = "Les quantités sont à contrôler contre un métré manuel avant tout usage.",
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ouvrir le dossier des fichiers");
            if (dialog.Show() == TaskDialogResult.CommandLink1)
            {
                Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
            }
        }

        /// <summary>A cote du .rvt s'il est local et inscriptible ; sinon Documents\DanCI\BOQ.</summary>
        private static string OutputFolder(Document doc)
        {
            try
            {
                if (!string.IsNullOrEmpty(doc.PathName) && File.Exists(doc.PathName))
                {
                    string dir = Path.GetDirectoryName(doc.PathName);
                    string probe = Path.Combine(dir, ".danci-ecriture-" + Guid.NewGuid().ToString("N"));
                    File.WriteAllText(probe, string.Empty);
                    File.Delete(probe);
                    return dir;
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                BoqLog.Warn("Dossier du projet non inscriptible : " + ex.Message);
            }
            string fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DanCI", "BOQ");
            Directory.CreateDirectory(fallback);
            return fallback;
        }

        private static string SafeName(string title)
        {
            string name = string.IsNullOrWhiteSpace(title) ? "projet" : title;
            foreach (char c in Path.GetInvalidFileNameChars())
            {
                name = name.Replace(c, '_');
            }
            return name.EndsWith(".rvt", StringComparison.OrdinalIgnoreCase) ? name.Substring(0, name.Length - 4) : name;
        }
    }
}
