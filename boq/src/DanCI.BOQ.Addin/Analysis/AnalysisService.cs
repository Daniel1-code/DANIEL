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
        public static AnalysisOutcome Run(UIDocument uidoc, AnalysisScope scope, IReadOnlyList<ManualLine> manualLines, IProgressSink progress,
            string priceLibraryPath = null, ProjectEstimate estimate = null, ProjectExtraction projectExtraction = null)
        {
            Document doc = uidoc.Document;
            LoadResult load = LoadConfiguration(priceLibraryPath, estimate);
            ExtractionSettings extraction = load.Configuration?.Extraction ?? new ExtractionSettings();
            // Reglages du metre propres au projet (terrain naturel, foisonnement, ouvrages...).
            projectExtraction?.ApplyTo(extraction);

            var request = new ExtractionRequest
            {
                Scope = scope ?? new AnalysisScope(),
                ActiveView = uidoc.ActiveView,
                Selection = uidoc.Selection.GetElementIds()
            };
            if (request.Scope.Mode == ScopeMode.VueActive && !IsModelView(request.ActiveView))
            {
                return new AnalysisOutcome
                {
                    DocumentTitle = doc.Title,
                    ScopeDescription = request.Scope.Describe(),
                    Failure = "Portée « vue active » : la vue « " + request.ActiveView?.Name +
                              " » n'est pas une vue du modèle (nomenclature, feuille, légende…). Activez un plan, une coupe ou une vue 3D."
                };
            }
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
                    boq = new BoqEngine().Build(report.Quantities, load.Configuration, manualLines);
                }
                catch (BoqConfigurationException ex)
                {
                    failure = "Configuration invalide : BOQ non calculé.";
                    load.Issues.AddRange(ex.Issues);
                }
            }

            // Constats : modele (extraction) + configuration si le moteur ne l'a pas deja rapportee.
            List<BoqIssue> facts = report.Notes.Concat(boq == null ? load.Issues : load.Issues.Where(i => i.Code == "FICHIER_ABSENT" || i.Code == "JSON_INVALIDE")).ToList();
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
                Layers = report.Layers,
                Inventory = report.Materials,
                LevelElevations = LevelElevations(doc),
                ConfigurationIssues = load.Issues,
                Configuration = load.Configuration,
                ModelFacts = facts,
                ManualLines = manualLines ?? new ManualLine[0],
                Duration = report.Duration,
                Failure = failure
            };
        }

        /// <summary>Configuration de l'installation + bibliotheque de prix et parametres de devis du projet.</summary>
        public static LoadResult LoadConfiguration(string priceLibraryPath, ProjectEstimate estimate)
        {
            LoadResult load = ConfigurationLoader.LoadDirectory(ConfigDirectory);
            UsePriceLibrary(load, priceLibraryPath);
            ProjectOverrides.ApplyEstimate(load, estimate);
            return load;
        }

        /// <summary>
        /// Remplace la bibliotheque de l'installation par celle du projet. Bibliotheque choisie mais
        /// introuvable ou illisible : ERREUR (jamais de repli silencieux sur d'autres prix).
        /// </summary>
        public static void UsePriceLibrary(LoadResult load, string path)
        {
            if (string.IsNullOrWhiteSpace(path) || load?.Configuration == null)
            {
                return;
            }
            if (!File.Exists(path))
            {
                load.Issues.Add(new BoqIssue(IssueSeverity.Error, "BIBLIOTHEQUE_INTROUVABLE",
                    "Bibliothèque de prix du projet introuvable : " + path + ". Choisissez-en une autre ou revenez à celle de l'installation."));
                return;
            }
            var issues = new List<BoqIssue>();
            DanCI.BOQ.Core.Pricing.PriceLibrary lib = DanCI.BOQ.Core.Pricing.PriceLibraryStore.Load(path, issues);
            load.Issues.AddRange(issues);
            if (lib == null)
            {
                return;
            }
            load.Configuration.Prices = lib;
            // Revalidation des prix contre le catalogue, avec la bibliotheque du projet.
            load.Issues.RemoveAll(i => i.File == BoqConfiguration.PricesFileName);
            load.Issues.AddRange(ConfigurationValidator.Validate(load.Configuration).Where(i => i.File == BoqConfiguration.PricesFileName));
            BoqLog.Info("Bibliothèque de prix du projet : " + path + " (" + lib.Prices.Count + " prix).");
        }

        /// <summary>
        /// Recalcule BOQ et audit avec d'autres forfaits, sur les quantites deja extraites :
        /// pas d'acces au modele, resultat identique a une reanalyse si le modele n'a pas change.
        /// </summary>
        public static AnalysisOutcome Recompute(AnalysisOutcome previous, IReadOnlyList<ManualLine> manualLines, string priceLibraryPath = null,
            ProjectEstimate estimate = null)
        {
            if (previous?.Configuration == null || previous.Boq == null)
            {
                return new AnalysisOutcome { Failure = "Lancez d'abord une analyse." };
            }
            // Configuration relue (bibliotheque de prix eventuellement changee), quantites conservees.
            LoadResult load = LoadConfiguration(priceLibraryPath, estimate);
            if (load.HasErrors)
            {
                return new AnalysisOutcome
                {
                    Failure = "Configuration invalide : " + string.Join(" | ", load.Issues.Where(i => i.Severity == IssueSeverity.Error).Take(3).Select(i => i.Message))
                };
            }
            BoqResult boq;
            try
            {
                boq = new BoqEngine().Build(previous.Quantities, load.Configuration, manualLines);
            }
            catch (BoqConfigurationException ex)
            {
                return new AnalysisOutcome { Failure = "Configuration invalide : " + ex.Message };
            }
            AuditReport audit = BoqAuditor.Run(previous.Quantities, previous.ModelFacts, boq, load.Configuration.Extraction);
            return new AnalysisOutcome
            {
                DocumentTitle = previous.DocumentTitle,
                ScopeDescription = previous.ScopeDescription,
                Boq = boq,
                Audit = audit,
                Quantities = previous.Quantities,
                Layers = previous.Layers,
                Inventory = previous.Inventory,
                LevelElevations = previous.LevelElevations,
                ConfigurationIssues = load.Issues,
                Configuration = load.Configuration,
                ModelFacts = previous.ModelFacts,
                ManualLines = manualLines ?? new ManualLine[0],
                Duration = previous.Duration
            };
        }

        public static string ProjectFilePath(Document doc) =>
            ProjectFileStore.PathFor(doc.PathName,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DanCI", "BOQ"), doc.Title);

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

        /// <summary>
        /// Export complet : Excel, PDF, CSV bruts et CSV d'audit. Un format en echec n'empeche pas
        /// les autres ; les echecs sont journalises avec les versions des dependances chargees
        /// (conflit de DLL avec un autre add-in, par exemple).
        /// </summary>
        /// <summary>Contexte d'export (identique pour l'apercu et pour les fichiers).</summary>
        /// <summary>Modifications du Gantt lues dans le fichier du projet (null si aucune).</summary>
        private static DanCI.BOQ.Core.Planning.PlanOverrides LoadPlanOverrides(Document doc)
        {
            try
            {
                string path = ProjectFilePath(doc);
                return string.IsNullOrEmpty(path) || !File.Exists(path) ? null : ProjectFileStore.Load(path, new List<DanCI.BOQ.Core.Model.BoqIssue>())?.Planning;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                return null;
            }
        }

        public static DanCI.BOQ.Export.ExportContext BuildExportContext(Document doc, AnalysisOutcome outcome,
            DanCI.BOQ.Core.Model.ProjectInfo identity = null, CoverSettings cover = null,
            DanCI.BOQ.Core.Planning.ProjectSchedule schedule = null) => new DanCI.BOQ.Export.ExportContext
            {
                Schedule = schedule,
                PlanOverrides = LoadPlanOverrides(doc),
                Project = identity ?? ReadProjectInfo(doc),
                Cover = cover ?? new CoverSettings(),
                Boq = outcome.Boq,
                Audit = outcome.Audit,
                Configuration = outcome.Configuration,
                Quantities = outcome.Quantities,
                Layers = outcome.Layers,
                Inventory = outcome.Inventory,
                LevelElevations = outcome.LevelElevations,
                LevelOrder = LevelNames(doc).ToList(),
                DocumentTitle = outcome.DocumentTitle,
                ScopeDescription = outcome.ScopeDescription,
                GeneratedAt = DateTime.Now,
                LogoPath = new[] { "logo.png", "logo.jpg" }.Select(n => Path.Combine(ConfigDirectory, n)).FirstOrDefault(File.Exists)
            };

        public static DanCI.BOQ.Export.ExportResult Export(Document doc, AnalysisOutcome outcome, DanCI.BOQ.Core.Model.ProjectInfo identity = null, CoverSettings cover = null,
            DanCI.BOQ.Core.Planning.ProjectSchedule schedule = null)
        {
            DanCI.BOQ.Export.ExportContext ctx = BuildExportContext(doc, outcome, identity, cover, schedule);
            var revisionIssues = new List<BoqIssue>();
            ctx.RevisionHistory = DanCI.BOQ.Core.Revisions.RevisionStore.List(DanCI.BOQ.Core.Revisions.RevisionStore.FolderFor(ProjectFilePath(doc)), revisionIssues);
            string folder = OutputFolder(doc);
            string stem = SafeName(doc.Title) + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            DanCI.BOQ.Export.ExportResult result = DanCI.BOQ.Export.BoqExporter.ExportAll(ctx, folder, stem);
            if (outcome.Audit != null)
            {
                string audit = Path.Combine(folder, stem + "_audit.csv");
                AuditCsvWriter.WriteFile(audit, outcome.Audit, CsvFormat.ExcelFrancais);
                result.Files.Add(audit);
            }
            BoqLog.Info("Export : " + string.Join(" ; ", result.Files));
            foreach (string w in result.Warnings)
            {
                BoqLog.Warn(w);
            }
            if (result.Errors.Count > 0)
            {
                BoqLog.Error("Export partiel : " + string.Join(" | ", result.Errors) + Environment.NewLine +
                             string.Join(Environment.NewLine, RuntimeReport.Dependencies()));
            }
            return result;
        }

        /// <summary>Informations sur le projet de Revit ; indice de revision : parametre DanCI_BOQ_Revision.</summary>
        public static DanCI.BOQ.Core.Model.ProjectInfo ReadProjectInfo(Document doc)
        {
            Autodesk.Revit.DB.ProjectInfo p = doc.ProjectInformation;
            if (p == null)
            {
                return new DanCI.BOQ.Core.Model.ProjectInfo { Name = doc.Title };
            }
            return new DanCI.BOQ.Core.Model.ProjectInfo
            {
                Name = p.Name,
                Number = p.Number,
                Client = p.ClientName,
                Location = p.Address,
                Author = p.Author,
                IssueDate = p.IssueDate,
                Status = p.Status,
                Revision = p.LookupParameter("DanCI_BOQ_Revision")?.AsString()
            };
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

        private static bool IsModelView(View view)
        {
            if (view == null || view.IsTemplate)
            {
                return false;
            }
            switch (view.ViewType)
            {
                case ViewType.FloorPlan:
                case ViewType.CeilingPlan:
                case ViewType.EngineeringPlan:
                case ViewType.AreaPlan:
                case ViewType.Elevation:
                case ViewType.Section:
                case ViewType.Detail:
                case ViewType.ThreeD:
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>Cote de chaque niveau en metres (repere des niveaux de Revit).</summary>
        public static Dictionary<string, double> LevelElevations(Document doc)
        {
            var map = new Dictionary<string, double>(StringComparer.Ordinal);
            foreach (Level l in new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>())
            {
                if (!map.ContainsKey(l.Name)) map[l.Name] = Math.Round(UnitUtils.ConvertFromInternalUnits(l.Elevation, UnitTypeId.Meters), 3);
            }
            return map;
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
