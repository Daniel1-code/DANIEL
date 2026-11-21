using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export.Csv;
using DanCI.BOQ.RevitAdapter.Extraction;
using DanCI.BOQ.UI.ViewModels;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Analyse complete : configuration -&gt; extraction (lecture seule) -&gt; moteur -&gt; audit.
    /// Partagee par la fenetre et par la commande de diagnostic.
    /// </summary>
    internal static class AnalysisService
    {
        public static string ConfigDirectory =>
            Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "config");

        /// <summary>Doit etre appele dans le contexte API Revit. Peut lever OperationCanceledException.</summary>
        public static AnalysisOutcome Run(UIDocument uidoc, AnalysisScope scope, IProgressSink progress)
        {
            Document doc = uidoc.Document;
            LoadResult load = ConfigurationLoader.LoadDirectory(ConfigDirectory);
            ExtractionSettings extraction = load.Configuration?.Extraction ?? new ExtractionSettings();

            var request = new ExtractionRequest
            {
                Scope = scope ?? new AnalysisScope(),
                ActiveView = uidoc.ActiveView,
                Selection = uidoc.Selection.GetElementIds()
            };
            if (request.Scope.Mode == ScopeMode.Selection && request.Selection.Count == 0)
            {
                return new AnalysisOutcome
                {
                    DocumentTitle = doc.Title,
                    ScopeDescription = request.Scope.Describe(),
                    Failure = "Portée « sélection » : aucun élément n'est sélectionné dans Revit."
                };
            }

            BoqLog.Info("Analyse : " + doc.Title + " — portée " + request.Scope.Describe());
            ExtractionReport report = ModelExtractor.Extract(doc, extraction, load.Configuration?.SteelProfiles, request, progress);
            progress.Report("Calcul du BOQ et audit", 0, 0);

            BoqResult boq = null;
            string failure = null;
            if (load.HasErrors)
            {
                failure = "Configuration invalide : BOQ non calculé. Voir l'onglet Audit (" + load.Issues.Count(i => i.Severity == IssueSeverity.Error) + " erreur(s)).";
            }
            else
            {
                try
                {
                    boq = new BoqEngine().Build(report.Quantities, load.Configuration);
                }
                catch (BoqConfigurationException ex)
                {
                    failure = "Configuration invalide : BOQ non calculé.";
                    load.Issues.AddRange(ex.Issues);
                }
            }

            // Constats : modele (extraction) + configuration si le moteur ne l'a pas deja rapportee.
            IEnumerable<BoqIssue> facts = report.Notes.Concat(boq == null ? load.Issues : load.Issues.Where(i => i.Code == "FICHIER_ABSENT" || i.Code == "JSON_INVALIDE"));
            AuditReport audit = BoqAuditor.Run(report.Quantities, facts, boq, extraction);

            BoqLog.Info("Analyse terminée : " + report.Quantities.Count + " quantités, " + (boq?.AllLines.Count() ?? 0) +
                        " lignes, " + audit.ErrorCount + " erreur(s) d'audit, " + report.Duration.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s.");

            return new AnalysisOutcome
            {
                DocumentTitle = doc.Title,
                ScopeDescription = request.Scope.Describe(),
                Boq = boq,
                Audit = audit,
                Quantities = report.Quantities,
                ConfigurationIssues = load.Issues,
                Duration = report.Duration,
                Failure = failure
            };
        }

        /// <summary>Ecrit metre brut, lignes du BOQ et audit. Renvoie les chemins ecrits.</summary>
        public static List<string> WriteCsv(Document doc, AnalysisOutcome outcome)
        {
            string folder = OutputFolder(doc);
            string stem = Path.Combine(folder, SafeName(doc.Title) + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            CsvFormat format = CsvFormat.ExcelFrancais;
            var paths = new List<string>();

            string raw = stem + "_metre_brut.csv";
            QuantityCsvWriter.WriteFile(raw, outcome.Quantities, format);
            paths.Add(raw);
            if (outcome.Boq != null)
            {
                string lines = stem + "_BOQ_lignes.csv";
                BoqLinesCsvWriter.WriteFile(lines, outcome.Boq, format);
                paths.Add(lines);
            }
            if (outcome.Audit != null)
            {
                string audit = stem + "_audit.csv";
                AuditCsvWriter.WriteFile(audit, outcome.Audit, format);
                paths.Add(audit);
            }
            BoqLog.Info("CSV écrits : " + string.Join(" ; ", paths));
            return paths;
        }

        /// <summary>A cote du .rvt s'il est local et inscriptible ; sinon Documents\DanCI\BOQ.</summary>
        public static string OutputFolder(Document doc)
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

        public static IEnumerable<string> LevelNames(Document doc) =>
            new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().OrderBy(l => l.Elevation).Select(l => l.Name);

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
