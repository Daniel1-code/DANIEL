using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Interop;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Extraction;
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
            if (window != null)
            {
                window.ViewModel.SetLevels(AnalysisService.LevelNames(app.ActiveUIDocument.Document));
                if (window.WindowState == System.Windows.WindowState.Minimized)
                {
                    window.WindowState = System.Windows.WindowState.Normal;
                }
                window.Activate();
                return;
            }
            var vm = new BoqWindowViewModel(this);
            vm.SetLevels(AnalysisService.LevelNames(app.ActiveUIDocument.Document));
            window = new BoqWindow(vm);
            new WindowInteropHelper(window) { Owner = app.MainWindowHandle };
            window.Closed += (s, e) => window = null;
            window.Show();
        }

        public void RequestAnalysis(AnalysisScope scope) => Enqueue(app => Analyze(app, scope));

        public void RequestSelection(IReadOnlyCollection<long> elementIds) => Enqueue(app => Select(app, elementIds));

        public void RequestExport() => Enqueue(Export);

        private void Enqueue(Action<UIApplication> request)
        {
            lock (requests)
            {
                requests.Enqueue(request);
            }
            externalEvent.Raise();
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

        private void Analyze(UIApplication app, AnalysisScope scope)
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
                    outcome = AnalysisService.Run(uidoc, scope, progress);
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

        private void Select(UIApplication app, IReadOnlyCollection<long> ids)
        {
            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null || lastDocument == null || !lastDocument.IsValidObject || !uidoc.Document.Equals(lastDocument))
            {
                SetStatus("Sélection impossible : le projet actif n'est pas celui de la dernière analyse. Relancez l'analyse.");
                return;
            }
            List<ElementId> existing = ids
                .Select(i => new ElementId(i))
                .Where(id => uidoc.Document.GetElement(id) != null)
                .ToList();
            if (existing.Count == 0)
            {
                SetStatus("Aucun des éléments n'existe encore dans le modèle (supprimés depuis l'analyse ?).");
                return;
            }
            uidoc.Selection.SetElementIds(existing);
            try
            {
                uidoc.ShowElements(existing);
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException ex)
            {
                BoqLog.Warn("Zoom impossible : " + ex.Message);
            }
            SetStatus(existing.Count + " élément(s) sélectionné(s) dans Revit" +
                      (existing.Count < ids.Count ? " (" + (ids.Count - existing.Count) + " introuvable(s))." : "."));
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
