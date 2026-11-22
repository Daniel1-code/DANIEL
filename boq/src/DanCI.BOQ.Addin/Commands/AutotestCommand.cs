using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Addin.Analysis;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Export.Diagnostics;
using DanCI.BOQ.RevitAdapter;
using DanCI.BOQ.RevitAdapter.Extraction;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>
    /// Autotest de l'installation, en un clic : Revit et runtime, onglet DanCI (autres
    /// extensions), configuration, moteur, Excel, PDF, CSV, dependances chargees, inventaire du
    /// projet ouvert (LECTURE SEULE). Produit UN fichier zip a envoyer au support : rapport,
    /// journal du jour, fichiers d'essai.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AutotestCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                UIApplication app = commandData.Application;
                string stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                string root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DanCI", "BOQ");
                string work = Path.Combine(root, "autotest_" + stamp);
                Directory.CreateDirectory(work);

                var header = new List<string>();
                header.AddRange(RuntimeReport.Build(RevitHostReader.Read(app.Application)));
                header.Add("Assembly          : " + Assembly.GetExecutingAssembly().Location);
                header.Add("Configuration     : " + AnalysisService.ConfigDirectory);
                header.Add(string.Empty);
                header.AddRange(RibbonReport(app));
                header.Add(string.Empty);
                header.AddRange(DocumentReport(app.ActiveUIDocument?.Document));

                List<SelfTestCheck> checks = SelfTest.Run(AnalysisService.ConfigDirectory, work, out DanCI.BOQ.Export.ExportContext sample);
                var windowCheck = new List<SelfTestCheck>();
                SelfTest.Check(windowCheck, "Fenêtre Métré & BOQ (tous les onglets, captures d'écran)", () => WindowCheck(sample, work));
                SelfTest.Check(windowCheck, "Aperçu avant export (PDF en images, feuilles Excel, captures)", () => PreviewCheck(sample, work));
                SelfTest.Check(windowCheck, "Panneau Diagramme du métré (lot, statut, prix ; captures)", () => ChartCheck(sample, work));
                int firstInfo = checks.FindIndex(c => !c.IsCheck);
                checks.InsertRange(firstInfo < 0 ? checks.Count : firstInfo, windowCheck);
                string report = SelfTest.Format(header, checks);
                File.WriteAllText(Path.Combine(work, "rapport_autotest.txt"), report, new System.Text.UTF8Encoding(true));
                if (File.Exists(BoqLog.CurrentFile))
                {
                    File.Copy(BoqLog.CurrentFile, Path.Combine(work, Path.GetFileName(BoqLog.CurrentFile)), true);
                }
                string zip = Path.Combine(root, "DanCI-BOQ-support_" + stamp + ".zip");
                ZipFile.CreateFromDirectory(work, zip);
                BoqLog.Info("Autotest : " + zip);

                int total = checks.Count(c => c.IsCheck), ok = checks.Count(c => c.IsCheck && c.Passed);
                var dialog = new TaskDialog("DanCI — autotest")
                {
                    MainInstruction = ok + " / " + total + " contrôles réussis",
                    MainContent = string.Join(Environment.NewLine, checks.Where(c => c.IsCheck)
                                      .Select(c => (c.Passed ? "✔ " : "✘ ") + c.Name + (c.Passed ? string.Empty : " — " + c.Detail))) +
                                  Environment.NewLine + Environment.NewLine +
                                  "Fichier à envoyer au support : " + Environment.NewLine + zip,
                    ExpandedContent = report,
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.Close,
                    FooterText = "Lecture seule : le modèle n'a pas été modifié."
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ouvrir le dossier du fichier à envoyer");
                if (dialog.Show() == TaskDialogResult.CommandLink1)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + zip + "\"") { UseShellExecute = true });
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Autotest en échec.", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>
        /// Ouvre la vraie fenetre hors ecran avec le devis d'essai et un hote factice, parcourt
        /// chaque onglet et chaque volet des Parametres du projet, et en fait des captures PNG.
        /// </summary>
        private static (bool, string) WindowCheck(DanCI.BOQ.Export.ExportContext sample, string work)
        {
            if (sample == null)
            {
                return (false, "non testé : pas de devis d'essai (moteur en échec)");
            }
            var outcome = new DanCI.BOQ.UI.ViewModels.AnalysisOutcome
            {
                DocumentTitle = "autotest",
                ScopeDescription = "autotest (données fictives)",
                Boq = sample.Boq,
                Audit = sample.Audit,
                Quantities = sample.Quantities,
                Configuration = sample.Configuration,
                Duration = TimeSpan.FromSeconds(1)
            };
            var project = new DanCI.BOQ.Core.Model.ProjectFile
            {
                Details = new DanCI.BOQ.Core.Model.ProjectDetails { Name = "AUTOTEST", Purpose = "Contrôle de la fenêtre", ReferenceArea = 500m },
                Estimate = DanCI.BOQ.Core.Model.ProjectEstimate.From(sample.Configuration?.Settings),
                Cover = new DanCI.BOQ.Core.Model.CoverSettings()
            };
            var prices = DanCI.BOQ.Core.Pricing.PriceEditor.Rows(sample.Configuration?.Mapping, sample.Configuration?.Prices);
            byte[] board = DanCI.BOQ.Export.BoqExporter.MissingPdfComponents() == null ? DanCI.BOQ.Export.Pdf.DashboardBoard.Png(sample) : null;
            if (board != null) File.WriteAllBytes(Path.Combine(work, "planche_tableau_de_bord.png"), board);
            DanCI.BOQ.UI.Diagnostics.WindowSelfTest.Result r = DanCI.BOQ.UI.Diagnostics.WindowSelfTest.Run(
                outcome, project, new DanCI.BOQ.Core.Model.ProjectInfo { Name = "AUTOTEST (Revit)" }, sample.Configuration?.Settings, prices, work, board);
            return (r.Passed, r.Detail);
        }

        private static (bool, string) ChartCheck(DanCI.BOQ.Export.ExportContext sample, string work)
        {
            if (sample?.Boq == null)
            {
                return (false, "non testé : pas de devis d'essai (moteur en échec)");
            }
            DanCI.BOQ.UI.Diagnostics.WindowSelfTest.Result r = DanCI.BOQ.UI.Diagnostics.WindowSelfTest.RunChart(sample.Boq, work);
            string pane = Analysis.ChartPane.Registered ? "panneau ancrable enregistré" : "PANNEAU ANCRABLE NON ENREGISTRÉ (voir le journal)";
            return (r.Passed && Analysis.ChartPane.Registered, r.Detail + " ; " + pane);
        }

        private static (bool, string) PreviewCheck(DanCI.BOQ.Export.ExportContext sample, string work)
        {
            if (sample == null)
            {
                return (false, "non testé : pas de devis d'essai (moteur en échec)");
            }
            string problem = DanCI.BOQ.Export.BoqExporter.MissingPdfComponents();
            IReadOnlyList<byte[]> pages = problem == null ? DanCI.BOQ.Export.Pdf.BoqPdfWriter.GeneratePageImages(sample) : null;
            var sheets = DanCI.BOQ.Export.Preview.WorkbookPreview.Build(sample);
            DanCI.BOQ.UI.Diagnostics.WindowSelfTest.Result r = DanCI.BOQ.UI.Diagnostics.WindowSelfTest.RunPreview(pages, problem, sheets, work);
            return (r.Passed && problem == null, (pages?.Count ?? 0) + " page(s) PDF, " + sheets.Count + " feuille(s) Excel ; " +
                                                 (problem == null ? string.Empty : "PDF : " + problem + " ; ") + r.Screens.Count + " capture(s)");
        }

        /// <summary>Panneaux de l'onglet DanCI : montre si les autres extensions DanCI sont bien chargees.</summary>
        private static IEnumerable<string> RibbonReport(UIApplication app)
        {
            var lines = new List<string> { "Onglet « " + BoqApplication.TabName + " » :" };
            try
            {
                foreach (RibbonPanel p in app.GetRibbonPanels(BoqApplication.TabName))
                {
                    lines.Add("  panneau « " + p.Name + " » : " + string.Join(", ", p.GetItems().Select(i => (i.ItemText ?? i.Name).Replace(Environment.NewLine, " "))));
                }
            }
            catch (Exception ex)
            {
                lines.Add("  illisible : " + ex.Message);
            }
            return lines;
        }

        /// <summary>Inventaire du projet ouvert, en lecture seule.</summary>
        private static IEnumerable<string> DocumentReport(Document doc)
        {
            var lines = new List<string>();
            if (doc == null)
            {
                lines.Add("Projet ouvert     : aucun (inventaire non fait).");
                return lines;
            }
            try
            {
                lines.Add("Projet ouvert     : " + doc.Title + (doc.IsFamilyDocument ? " (famille)" : string.Empty) +
                          (doc.IsWorkshared ? " — partage de travail" : string.Empty));
                lines.Add("Phases            : " + string.Join(", ", ModelExtractor.PhaseNames(doc)));
                lines.Add("Niveaux           : " + string.Join(", ", AnalysisService.LevelNames(doc)));
                var links = new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>().ToList();
                lines.Add("Liens RVT         : " + (links.Count == 0 ? "aucun" : string.Join(", ", links.Select(l => l.Name + " (" + l.GetLinkedFileStatus() + ")"))));
                var counts = new (string Label, BuiltInCategory Cat)[]
                {
                    ("Fondations", BuiltInCategory.OST_StructuralFoundation), ("Poteaux porteurs", BuiltInCategory.OST_StructuralColumns),
                    ("Ossature", BuiltInCategory.OST_StructuralFraming), ("Sols", BuiltInCategory.OST_Floors), ("Murs", BuiltInCategory.OST_Walls),
                    ("Toits", BuiltInCategory.OST_Roofs), ("Escaliers", BuiltInCategory.OST_Stairs), ("Armatures", BuiltInCategory.OST_Rebar),
                    ("Portes", BuiltInCategory.OST_Doors), ("Fenêtres", BuiltInCategory.OST_Windows), ("Pièces", BuiltInCategory.OST_Rooms),
                    ("Canalisations", BuiltInCategory.OST_PipeCurves), ("Parts", BuiltInCategory.OST_Parts)
                };
                lines.Add("Éléments          : " + string.Join(", ", counts.Select(c =>
                    c.Label + " " + new FilteredElementCollector(doc).OfCategory(c.Cat).WhereElementIsNotElementType().GetElementCount())));
                lines.Add("Avertissements Revit : " + doc.GetWarnings().Count);
            }
            catch (Exception ex)
            {
                lines.Add("Inventaire interrompu : " + ex.GetType().Name + " : " + ex.Message);
            }
            return lines;
        }
    }
}
