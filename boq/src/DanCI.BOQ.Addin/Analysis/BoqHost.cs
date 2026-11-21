using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.RevitAdapter.Extraction;
using DanCI.BOQ.UI.Progress;
using DanCI.BOQ.UI.ViewModels;
using DanCI.BOQ.UI.Views;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Pont entre la fenetre non modale et Revit. Les demandes de la fenetre sont mises en file
    /// et executees par Revit dans le contexte API (ExternalEvent) : la fenetre n'appelle jamais
    /// l'API Revit directement. Une seule fenetre par session.
    /// </summary>
    internal sealed class BoqHost : IBoqHost, IExternalEventHandler
    {
        private static BoqHost instance;

        private readonly ExternalEvent externalEvent;
        private readonly Queue<Action<UIApplication>> requests = new Queue<Action<UIApplication>>();
        private BoqWindow window;
        private AnalysisOutcome last;
        private Document lastDocument;

        private BoqHost()
        {
            externalEvent = ExternalEvent.Create(this);
        }

        /// <summary>Ouvre (ou ramene au premier plan) la fenetre. A appeler depuis une commande (contexte API).</summary>
        public static void ShowWindow(UIApplication app)
        {
            if (instance == null)
            {
                instance = new BoqHost();
            }
            instance.Show(app);
        }

        private void Show(UIApplication app)
        {
            Document doc = app.ActiveUIDocument.Document;
            if (window != null)
            {
                window.ViewModel.SetLevels(AnalysisService.LevelNames(doc));
                if (window.WindowState == System.Windows.WindowState.Minimized)
                {
                    window.WindowState = System.Windows.WindowState.Normal;
                }
                window.Activate();
                return;
            }
            var vm = new BoqWindowViewModel(this);
            vm.SetLevels(AnalysisService.LevelNames(doc));
            LoadProjectContext(vm, doc);
            window = new BoqWindow(vm);
            new WindowInteropHelper(window) { Owner = app.MainWindowHandle };
            window.Closed += (s, e) => window = null;
            window.Show();
        }

        public void RequestAnalysis(AnalysisScope scope, IReadOnlyList<ManualLine> manualLines) => Enqueue(app => Analyze(app, scope, manualLines));

        public void RequestRecompute(IReadOnlyList<ManualLine> manualLines) => Enqueue(app =>
        {
            if (last == null)
            {
                window?.ViewModel.Apply(new AnalysisOutcome { Failure = "Forfaits enregistrés. Lancez une analyse pour les voir dans le BOQ." });
                return;
            }
            AnalysisOutcome recomputed = AnalysisService.Recompute(last, manualLines, window?.ViewModel.PriceLibraryPath);
            if (recomputed.Boq != null)
            {
                last = recomputed;
            }
            window?.ViewModel.Apply(recomputed);
        });

        public void RequestSelection(IReadOnlyCollection<ElementRef> elements) => Enqueue(app => Select(app, elements));

        public void RequestExport() => Enqueue(Export);

        public void RequestSaveProject(ProjectFile project) => Enqueue(app => SaveProject(app, project));

        public void RequestWriteParameters() => Enqueue(WriteParameters);

        public void RequestValidationTemplate() => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            string path = ValidationFile("_validation_gabarit.xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteTemplate(path, last.Quantities, last.Boq, last.DocumentTitle);
            BoqLog.Info("Gabarit de validation : " + path);
            SetStatus("Gabarit écrit : " + path + " — à faire remplir à la main, puis « Comparer au gabarit rempli ».");
            OpenFolder(path);
        });

        public void RequestManualComparison(string filledTemplatePath) => Enqueue(app =>
        {
            if (!HasAnalysis(app)) return;
            List<DanCI.BOQ.Core.Validation.ManualEntry> manual = DanCI.BOQ.Export.Validation.ValidationWorkbook.ReadManual(filledTemplatePath);
            if (manual.Count == 0)
            {
                SetStatus("Le gabarit ne contient aucune quantité manuelle (colonne « Quantité manuelle »).");
                return;
            }
            var report = DanCI.BOQ.Core.Validation.TakeoffComparer.Compare(last.Quantities, last.Boq, manual);
            string path = ValidationFile("_validation_rapport.xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteComparison(path, report, last.DocumentTitle,
                DanCI.BOQ.Core.Validation.QuantityFingerprint.Compute(last.Quantities));
            string verdict = report.Kinds.Any(k => k.IsTarget && k.Compared > 0)
                ? (report.TargetsMet ? "OBJECTIF ATTEINT (béton et acier < 2 %)" : "OBJECTIF NON ATTEINT : écarts à expliquer")
                : "objectif non évaluable (aucun béton/acier mesuré des deux côtés)";
            BoqLog.Info("Validation : " + manual.Count + " mesures manuelles — " + verdict + " — " + path);
            SetStatus(manual.Count + " mesure(s) manuelle(s) comparée(s) : " + verdict + ". Rapport : " + path);
            OpenFolder(path);
        });

        public void RequestRunComparison(string rawCsvA, string rawCsvB) => Enqueue(app =>
        {
            var a = DanCI.BOQ.Export.Csv.QuantityCsvReader.ReadFile(rawCsvA);
            var b = DanCI.BOQ.Export.Csv.QuantityCsvReader.ReadFile(rawCsvB);
            var report = DanCI.BOQ.Core.Validation.RunComparer.Compare(a, b);
            string path = Path.Combine(Path.GetDirectoryName(rawCsvA),
                "DanCI_reproductibilite_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            DanCI.BOQ.Export.Validation.ValidationWorkbook.WriteRunComparison(path, report, rawCsvA, rawCsvB);
            BoqLog.Info("Reproductibilité : " + (report.Identical ? "identiques" : report.Differences.Count + " différence(s)") + " — " + path);
            SetStatus((report.Identical
                ? "Métrés IDENTIQUES (empreinte " + DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(report.FingerprintA) + ")."
                : report.Differences.Count + " quantité(s) différente(s) entre les deux métrés.") + " Rapport : " + path);
            OpenFolder(path);
        });

        public void RequestPriceLibrary(string path) => Enqueue(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null || window == null)
            {
                return;
            }
            string chosen = path;
            if (path != null && path.EndsWith(".xlsx", StringComparison.OrdinalIgnoreCase))
            {
                // Import Excel -> bibliotheque JSON du meme nom, validee contre le catalogue.
                var issues = new List<BoqIssue>();
                DanCI.BOQ.Core.Pricing.PriceLibrary lib = DanCI.BOQ.Export.Excel.PriceLibraryWorkbook.Read(path, issues);
                if (issues.Any(i => i.Severity == IssueSeverity.Error))
                {
                    SetStatus("Import refusé : " + string.Join(" | ", issues.Where(i => i.Severity == IssueSeverity.Error).Take(3).Select(i => i.Message)));
                    return;
                }
                chosen = Path.ChangeExtension(path, ".json");
                DanCI.BOQ.Core.Pricing.PriceLibraryStore.Save(chosen, lib);
                BoqLog.Info("Bibliothèque importée : " + path + " -> " + chosen + " (" + lib.Prices.Count + " prix).");
            }
            window.ViewModel.PriceLibraryPath = chosen;
            SaveProject(app, window.ViewModel.BuildProjectFile());
            SetStatus(chosen == null
                ? "Prix : bibliothèque de l'installation."
                : "Prix : " + Path.GetFileName(chosen) + (last != null ? " — recalcul…" : " — lancez l'analyse."));
            if (last != null)
            {
                AnalysisOutcome recomputed = AnalysisService.Recompute(last, window.ViewModel.CurrentManualLines(), chosen);
                if (recomputed.Boq != null)
                {
                    last = recomputed;
                }
                window.ViewModel.Apply(recomputed);
            }
        });

        public void RequestPriceTemplate() => Enqueue(app =>
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return;
            }
            LoadResult load = ConfigurationLoader.LoadDirectory(AnalysisService.ConfigDirectory);
            AnalysisService.UsePriceLibrary(load, window?.ViewModel.PriceLibraryPath);
            string folder = AnalysisService.OutputFolder(doc);
            string name = Path.GetFileNameWithoutExtension(doc.Title ?? "projet");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string path = Path.Combine(folder, name + "_DanCI_prix_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + ".xlsx");
            DanCI.BOQ.Export.Excel.PriceLibraryWorkbook.WriteTemplate(path, load.Configuration?.Mapping, load.Configuration?.Prices);
            SetStatus("Modèle des prix écrit : " + path + " — remplissez les PU, puis « Choisir… » ce classeur pour l'importer.");
            OpenFolder(path);
        });

        private bool HasAnalysis(UIApplication app)
        {
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                SetStatus("Lancez d'abord une analyse.");
                return false;
            }
            return true;
        }

        private string ValidationFile(string suffix)
        {
            string folder = AnalysisService.OutputFolder(lastDocument);
            string name = Path.GetFileNameWithoutExtension(lastDocument.Title ?? "projet");
            foreach (char c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return Path.Combine(folder, name + "_DanCI_" + DateTime.Now.ToString("yyyyMMdd-HHmmss", System.Globalization.CultureInfo.InvariantCulture) + suffix);
        }

        private static void OpenFolder(string file) =>
            Process.Start(new ProcessStartInfo(Path.GetDirectoryName(file)) { UseShellExecute = true });

        /// <summary>Phases, liens et fichier projet du document actif.</summary>
        private static void LoadProjectContext(BoqWindowViewModel vm, Document doc)
        {
            string path = AnalysisService.ProjectFilePath(doc);
            var issues = new List<DanCI.BOQ.Core.Model.BoqIssue>();
            ProjectFile project = ProjectFileStore.Load(path, issues);
            vm.SetContext(ModelExtractor.PhaseNames(doc), ModelExtractor.LoadedLinkNames(doc), project, path);
            if (issues.Count > 0)
            {
                vm.Status = "Fichier projet illisible (" + issues[0].Message + ") : il ne sera PAS écrasé sans votre accord, corrigez-le ou supprimez-le.";
                BoqLog.Warn("Fichier projet illisible : " + path + " — " + issues[0].Message);
            }
        }

        private void SaveProject(UIApplication app, ProjectFile project)
        {
            Document doc = app.ActiveUIDocument?.Document;
            if (doc == null)
            {
                return;
            }
            string path = AnalysisService.ProjectFilePath(doc);
            var issues = new List<DanCI.BOQ.Core.Model.BoqIssue>();
            ProjectFileStore.Load(path, issues);
            if (issues.Count > 0)
            {
                SetStatus("Fichier projet existant illisible : non écrasé. Corrigez ou supprimez " + path);
                return;
            }
            ProjectFileStore.Save(path, project);
            BoqLog.Info("Fichier projet enregistré : " + path);
            if (window != null)
            {
                window.ViewModel.ProjectFilePath = path;
            }
            SetStatus("Fichier projet enregistré : " + path);
        }

        private void WriteParameters(UIApplication app)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (last?.Boq == null || lastDocument == null || !lastDocument.IsValidObject || uidoc == null || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Écriture impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            var plan = ModelParameterWriter.Plan(last.Boq, out int linked);
            var confirm = new TaskDialog("DanCI — écrire les codes BOQ dans le modèle")
            {
                MainInstruction = "Écrire DanCI_BOQ_Code et DanCI_BOQ_Lot sur " + plan.Count + " élément(s) ?",
                MainContent =
                    "Cette action MODIFIE le modèle :" + Environment.NewLine +
                    "  • crée si besoin les paramètres partagés DanCI (Code, Lot, Exclure, Revision) ;" + Environment.NewLine +
                    "  • écrit le code des articles et le lot de chaque élément métré du modèle hôte." + Environment.NewLine +
                    (linked > 0 ? "  • " + linked + " contribution(s) d'éléments de liens RVT ne seront pas écrites (modèle lié non modifiable)." + Environment.NewLine : string.Empty) +
                    Environment.NewLine + "Une seule annulation (Ctrl+Z) défait l'ensemble.",
                CommonButtons = TaskDialogCommonButtons.Yes | TaskDialogCommonButtons.No,
                DefaultButton = TaskDialogResult.No
            };
            if (confirm.Show() != TaskDialogResult.Yes)
            {
                SetStatus("Écriture annulée : modèle non modifié.");
                return;
            }
            ModelParameterWriter.WriteResult r = ModelParameterWriter.Write(app.Application, uidoc.Document, last.Boq);
            if (r.Error != null)
            {
                SetStatus(r.Error);
                TaskDialog.Show("DanCI — écriture des codes", r.Error);
                return;
            }
            SetStatus("Codes écrits sur " + r.Written + " élément(s) ; " + r.Unchanged + " déjà à jour" +
                      (r.Failed > 0 ? " ; " + r.Failed + " échec(s) (paramètre absent ou en lecture seule)" : string.Empty) +
                      (r.SkippedOwnedByOthers > 0 ? " ; " + r.SkippedOwnedByOthers + " emprunté(s) par un autre utilisateur" : string.Empty) +
                      (r.SkippedLinked > 0 ? " ; " + r.SkippedLinked + " contribution(s) de liens ignorée(s)" : string.Empty) + ". Annulable (Ctrl+Z).");
        }

        private void Enqueue(Action<UIApplication> request)
        {
            lock (requests)
            {
                requests.Enqueue(request);
            }
            ExternalEventRequest status = externalEvent.Raise();
            if (status == ExternalEventRequest.Denied || status == ExternalEventRequest.TimedOut)
            {
                lock (requests)
                {
                    requests.Clear();
                }
                BoqLog.Warn("Requête refusée par Revit : " + status);
                if (window != null)
                {
                    window.ViewModel.IsBusy = false;
                    window.ViewModel.Status = "Revit n'accepte pas la demande pour l'instant (commande ou dialogue en cours ?) : terminez-la, puis réessayez.";
                }
            }
        }

        public void Execute(UIApplication app)
        {
            while (true)
            {
                Action<UIApplication> next;
                lock (requests)
                {
                    if (requests.Count == 0)
                    {
                        return;
                    }
                    next = requests.Dequeue();
                }
                try
                {
                    next(app);
                }
                catch (Exception ex)
                {
                    BoqLog.Error("Requête de la fenêtre BOQ en échec.", ex);
                    if (window != null)
                    {
                        window.ViewModel.IsBusy = false;
                        window.ViewModel.Status = "Erreur : " + ex.Message + " (détails dans le journal).";
                    }
                }
            }
        }

        public string GetName() => "DanCI — Métré & BOQ";

        private void Analyze(UIApplication app, AnalysisScope scope, IReadOnlyList<ManualLine> manualLines)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                window?.ViewModel.Apply(new AnalysisOutcome { Failure = "Ouvrez un projet Revit (pas une famille)." });
                return;
            }

            AnalysisOutcome outcome;
            using (ProgressDialog progress = ProgressDialog.Start("DanCI — analyse du métré"))
            {
                try
                {
                    outcome = AnalysisService.Run(uidoc, scope, manualLines, progress, window?.ViewModel.PriceLibraryPath);
                }
                catch (OperationCanceledException)
                {
                    BoqLog.Info("Analyse annulée par l'utilisateur.");
                    // Aucun resultat partiel : un metre interrompu n'est jamais presente comme un metre.
                    outcome = new AnalysisOutcome { Failure = "Analyse annulée : aucun résultat partiel n'est affiché." };
                }
            }

            if (outcome.Boq != null || outcome.Audit != null)
            {
                last = outcome;
                lastDocument = uidoc.Document;
            }
            window?.ViewModel.SetLevels(AnalysisService.LevelNames(uidoc.Document));
            window?.ViewModel.Apply(outcome);
        }

        private void Select(UIApplication app, IReadOnlyCollection<ElementRef> refs)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || lastDocument == null || !lastDocument.IsValidObject || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Sélection impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            Document doc = uidoc.Document;
            var references = new List<Reference>();
            var zoom = new HashSet<ElementId>();
            int missing = 0;
            foreach (ElementRef r in refs)
            {
                if (!r.IsLinked)
                {
                    Element e = doc.GetElement(new ElementId(r.ElementId));
                    if (e == null) { missing++; continue; }
                    references.Add(new Reference(e));
                    zoom.Add(e.Id);
                    continue;
                }
                // Element d'un lien : reference de lien, zoom sur l'instance du lien.
                var link = doc.GetElement(new ElementId(r.LinkInstanceId.Value)) as RevitLinkInstance;
                Element linked = link?.GetLinkDocument()?.GetElement(new ElementId(r.ElementId));
                if (linked == null) { missing++; continue; }
                references.Add(new Reference(linked).CreateLinkReference(link));
                zoom.Add(link.Id);
            }
            if (references.Count == 0)
            {
                SetStatus("Aucun des éléments n'existe encore dans le modèle (supprimés depuis l'analyse ?).");
                return;
            }
            uidoc.Selection.SetReferences(references);
            try
            {
                uidoc.ShowElements(zoom);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                BoqLog.Warn("Zoom impossible : " + ex.Message);
            }
            SetStatus(references.Count + " élément(s) sélectionné(s) dans Revit" + (missing > 0 ? " (" + missing + " introuvable(s))." : "."));
        }

        private void Export(UIApplication app)
        {
            if (last == null || lastDocument == null || !lastDocument.IsValidObject)
            {
                SetStatus("Rien à exporter : lancez d'abord une analyse.");
                return;
            }
            if (last.Boq == null)
            {
                SetStatus("Rien à exporter : le BOQ n'a pas été calculé (configuration invalide, voir l'onglet Audit).");
                return;
            }
            DanCI.BOQ.Export.ExportResult result = AnalysisService.Export(lastDocument, last);
            if (result.Files.Count == 0)
            {
                SetStatus("Export en échec : " + string.Join(" | ", result.Errors) + " (détails dans le journal).");
                return;
            }
            string folder = Path.GetDirectoryName(result.Files[0]);
            SetStatus(result.Errors.Count == 0
                ? result.Files.Count + " fichier(s) écrit(s) (Excel, PDF, CSV) dans " + folder
                : "Export PARTIEL — " + string.Join(" | ", result.Errors) + " — autres fichiers écrits dans " + folder);
            Process.Start(new ProcessStartInfo(folder) { UseShellExecute = true });
        }

        private void SetStatus(string text)
        {
            if (window != null)
            {
                window.ViewModel.Status = text;
            }
        }
    }
}
