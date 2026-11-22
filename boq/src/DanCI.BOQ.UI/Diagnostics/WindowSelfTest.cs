using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.ViewModels;
using DanCI.BOQ.UI.Views;

namespace DanCI.BOQ.UI.Diagnostics
{
    /// <summary>
    /// Autotest de la fenetre : la cree hors ecran avec un devis d'essai, affiche chaque onglet
    /// (et chaque volet des Parametres du projet), en fait une capture PNG et collecte les
    /// erreurs de liaison WPF. Rien n'est envoye a Revit : l'hote est factice.
    /// </summary>
    public static class WindowSelfTest
    {
        public sealed class Result
        {
            public bool Passed { get; set; }
            public List<string> Screens { get; } = new List<string>();
            public List<string> BindingErrors { get; } = new List<string>();
            public string ThemeError { get; set; }
            public string Detail =>
                (ThemeError == null ? "thème futuriste chargé ; " : "THÈME NON CHARGÉ (" + ThemeError + ") ; ") + Screens.Count + " capture(s) d'écran ; " + BindingErrors.Count + " erreur(s) de liaison" +
                (BindingErrors.Count == 0 ? string.Empty : " : " + string.Join(" | ", BindingErrors.Take(5)));
        }

        public static Result Run(AnalysisOutcome sample, ProjectFile project, ProjectInfo revitInfo,
            DanCI.BOQ.Core.Pricing.EstimateSettings installation, IEnumerable<DanCI.BOQ.Core.Pricing.PriceEdit> prices, string folder, byte[] boardPng = null)
        {
            var result = new Result();
            var listener = new BindingErrorListener();
            PresentationTraceSources.Refresh();
            PresentationTraceSources.DataBindingSource.Listeners.Add(listener);
            SourceLevels previous = PresentationTraceSources.DataBindingSource.Switch.Level;
            PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Error;
            BoqWindow window = null;
            try
            {
                var vm = new BoqWindowViewModel(new NullHost());
                vm.SetLevels(new[] { "Niveau 0", "Niveau 1" });
                vm.SetContext(new[] { "Existant", "Nouvelle construction" }, new[] { "Lien structure.rvt" }, project, Path.Combine(folder, "autotest.danciboq.json"));
                vm.SetSettingsContext(project, revitInfo, installation, prices, "Bibliothèque de l'autotest");
                vm.Apply(sample);
                vm.SetDashboardImage(boardPng);

                window = new BoqWindow(vm)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                Pump();

                TabControl main = Find<TabControl>(window).First();
                for (int i = 0; i < main.Items.Count; i++)
                {
                    vm.SelectedTab = i;
                    Pump();
                    string header = Header(main.Items[i]);
                    TabControl inner = Find<TabControl>((DependencyObject)((TabItem)main.Items[i]).Content).FirstOrDefault();
                    if (inner == null)
                    {
                        result.Screens.Add(Capture(window, folder, (i + 1) + "_" + header));
                        continue;
                    }
                    for (int j = 0; j < inner.Items.Count; j++)
                    {
                        inner.SelectedIndex = j;
                        Pump();
                        result.Screens.Add(Capture(window, folder, (i + 1) + "_" + header + "_" + (j + 1) + "_" + Header(inner.Items[j])));
                    }
                }
                // Style classique (clair) : memes onglets principaux, puis retour au style de l'utilisateur.
                string userStyle = DanCI.BOQ.UI.Themes.ThemeColors.Settings.Style;
                string userBackground = DanCI.BOQ.UI.Themes.ThemeColors.Settings.Background;
                try
                {
                    DanCI.BOQ.UI.Themes.ThemeColors.Preview(userBackground, DanCI.BOQ.Core.Model.InterfaceSettings.Classic);
                    for (int i = 0; i < main.Items.Count; i++)
                    {
                        vm.SelectedTab = i;
                        Pump();
                        result.Screens.Add(Capture(window, folder, "classique_" + (i + 1) + "_" + Header(main.Items[i])));
                    }
                }
                finally
                {
                    DanCI.BOQ.UI.Themes.ThemeColors.Preview(userBackground, userStyle);
                }
                result.BindingErrors.AddRange(listener.Errors.Distinct());
                result.ThemeError = BoqWindow.ThemeError;
                result.Passed = result.Screens.Count >= main.Items.Count && result.BindingErrors.Count == 0 && result.ThemeError == null;
            }
            finally
            {
                window?.Close();
                PresentationTraceSources.DataBindingSource.Listeners.Remove(listener);
                PresentationTraceSources.DataBindingSource.Switch.Level = previous;
            }
            return result;
        }

        /// <summary>Fenetre d'apercu avant export : onglet PDF et chaque feuille Excel, en captures PNG.</summary>
        public static Result RunPreview(IReadOnlyList<byte[]> pages, string pdfProblem, IReadOnlyList<DanCI.BOQ.Core.Preview.SheetPreview> sheets, string folder)
        {
            var result = new Result { ThemeError = BoqWindow.ThemeError };
            DanCI.BOQ.UI.Preview.PreviewWindow window = null;
            try
            {
                window = new DanCI.BOQ.UI.Preview.PreviewWindow("AUTOTEST", pages, pdfProblem, sheets, export: null)
                {
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false
                };
                window.Show();
                Pump();
                TabControl main = Find<TabControl>(window).First();
                main.SelectedIndex = 0;
                Pump();
                result.Screens.Add(Capture(window, folder, "apercu_1_PDF"));
                main.SelectedIndex = 1;
                Pump();
                TabControl inner = Find<TabControl>((DependencyObject)((TabItem)main.Items[1]).Content).FirstOrDefault();
                for (int j = 0; inner != null && j < inner.Items.Count; j++)
                {
                    inner.SelectedIndex = j;
                    Pump();
                    result.Screens.Add(Capture(window, folder, "apercu_2_Excel_" + (j + 1) + "_" + Header(inner.Items[j])));
                }
                result.Passed = (pages == null || window.PageCount == pages.Count) && result.Screens.Count == 1 + (sheets?.Count ?? 0);
            }
            finally
            {
                window?.Close();
            }
            return result;
        }

        /// <summary>Panneau « Diagramme du métré » (par lot, statut, prix ; montant / elements), en captures PNG.</summary>
        public static Result RunChart(DanCI.BOQ.Core.Model.BoqResult boq, string folder,
            IReadOnlyList<DanCI.BOQ.Core.Model.ElementQuantity> inventory = null, IReadOnlyList<string> levels = null)
        {
            var result = new Result { ThemeError = BoqWindow.ThemeError };
            Window window = null;
            try
            {
                var vm = new DanCI.BOQ.UI.Chart.ViewChartViewModel();
                vm.SetBoq(boq, "USD", inventory, levels);
                window = new Window
                {
                    Title = "DanCI — Diagramme (autotest)",
                    Width = 380,
                    Height = 760,
                    Background = new SolidColorBrush(Color.FromRgb(0x07, 0x0D, 0x1A)),
                    Content = new DanCI.BOQ.UI.Chart.ViewChartPanel(vm),
                    WindowStartupLocation = WindowStartupLocation.Manual,
                    Left = -20000,
                    Top = -20000,
                    ShowActivated = false,
                    ShowInTaskbar = false
                };
                window.Show();
                string[] modes = { "lot", "statut", "prix", "niveau", "materiaux" };
                foreach (string mode in modes)
                {
                    vm.SetModeCommand.Execute(mode);
                    foreach (string metric in mode == "materiaux" ? new[] { "volume", "surface" } : mode == "niveau" ? new[] { "montant", "volume" } : new[] { "montant", "elements" })
                    {
                        vm.SetMetricCommand.Execute(metric);
                        Pump();
                        result.Screens.Add(Capture(window, folder, "diagramme_" + mode + "_" + metric));
                    }
                }
                result.Passed = result.Screens.Count == 10;
            }
            finally
            {
                window?.Close();
            }
            return result;
        }

        private static string Header(object tab)
        {
            string text = (tab as TabItem)?.Header?.ToString() ?? "onglet";
            foreach (char c in Path.GetInvalidFileNameChars().Concat(new[] { ' ', '(', ')', ':', ',' }))
            {
                text = text.Replace(c, '_');
            }
            return text.Length > 40 ? text.Substring(0, 40) : text;
        }

        /// <summary>Laisse WPF faire la mise en page et le rendu.</summary>
        private static void Pump()
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }

        private static string Capture(Window window, string folder, string name)
        {
            var root = (FrameworkElement)window.Content;
            int w = Math.Max(1, (int)Math.Ceiling(root.ActualWidth));
            int h = Math.Max(1, (int)Math.Ceiling(root.ActualHeight));
            var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            var visual = new DrawingVisual();
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.DrawRectangle(window.Background ?? Brushes.White, null, new Rect(0, 0, w, h));
                dc.DrawRectangle(new VisualBrush(root), null, new Rect(0, 0, w, h));
            }
            bmp.Render(visual);
            string path = Path.Combine(folder, "fenetre_" + name + ".png");
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bmp));
            using (FileStream f = File.Create(path))
            {
                encoder.Save(f);
            }
            return path;
        }

        private static IEnumerable<T> Find<T>(DependencyObject root) where T : DependencyObject
        {
            if (root == null) yield break;
            int n = VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(root, i);
                if (child is T t) yield return t;
                foreach (T d in Find<T>(child)) yield return d;
            }
        }

        private sealed class BindingErrorListener : TraceListener
        {
            public List<string> Errors { get; } = new List<string>();
            private string pending = string.Empty;
            public override void Write(string message) => pending += message;
            public override void WriteLine(string message)
            {
                string line = (pending + message).Trim();
                pending = string.Empty;
                // Les liaisons du survol, de la recherche, etc. hors des donnees de l'extension sont ignorees.
                if (line.Length > 0)
                {
                    Errors.Add(line.Length > 300 ? line.Substring(0, 300) + "…" : line);
                }
            }
        }

        /// <summary>Hote factice : aucune demande n'atteint Revit pendant l'autotest.</summary>
        private sealed class NullHost : IBoqHost
        {
            public void RequestAnalysis(AnalysisScope scope, IReadOnlyList<ManualLine> manualLines) { }
            public void RequestRecompute(IReadOnlyList<ManualLine> manualLines) { }
            public void RequestSelection(IReadOnlyCollection<ElementRef> elements) { }
            public void RequestExport() { }
            public void RequestShowChart() { }
            public void RequestSchedules() { }
            public void RequestPreview() { }
            public void RequestRevisionComparison(string snapshotPath) { }
            public void RequestFreezeRevision() { }
            public void RequestColorize(DanCI.BOQ.Core.Dashboard.ColoringMode mode) { }
            public void RequestSaveProject(ProjectFile project) { }
            public void RequestWriteParameters() { }
            public void RequestPriceLibrary(string path) { }
            public void RequestSaveSettings(ProjectFile project, IReadOnlyList<DanCI.BOQ.Core.Pricing.PriceEdit> prices, PartyInfo company) { }
            public void RequestPriceTemplate() { }
            public void RequestValidationTemplate() { }
            public void RequestManualComparison(string filledTemplatePath) { }
            public void RequestRunComparison(string rawCsvA, string rawCsvB) { }
        }
    }
}
