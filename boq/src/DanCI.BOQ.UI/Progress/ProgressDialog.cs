using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Threading;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.UI.Progress
{
    /// <summary>
    /// Fenetre de progression sur SON PROPRE fil STA : Revit est occupe par l'extraction sur le
    /// fil principal, cette fenetre reste donc reactive et son bouton « Annuler » fonctionne.
    /// L'annulation est cooperative : l'extraction la verifie a intervalles reguliers.
    /// </summary>
    public sealed class ProgressDialog : IProgressSink, IDisposable
    {
        private readonly CancellationTokenSource cts = new CancellationTokenSource();
        private readonly ManualResetEventSlim ready = new ManualResetEventSlim(false);
        private Dispatcher dispatcher;
        private Window window;
        private TextBlock stageText;
        private ProgressBar bar;
        private DateTime lastUpdate = DateTime.MinValue;

        private ProgressDialog()
        {
        }

        public CancellationToken Cancellation => cts.Token;

        public static ProgressDialog Start(string title)
        {
            var dialog = new ProgressDialog();
            var thread = new Thread(() => dialog.Run(title)) { IsBackground = true, Name = "DanCI BOQ - progression" };
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            dialog.ready.Wait(TimeSpan.FromSeconds(5));
            return dialog;
        }

        private void Run(string title)
        {
            dispatcher = Dispatcher.CurrentDispatcher;
            stageText = new TextBlock { Text = "Préparation…", Margin = new Thickness(0, 0, 0, 8), TextTrimming = TextTrimming.CharacterEllipsis };
            bar = new ProgressBar { Height = 18, Minimum = 0, Maximum = 1, IsIndeterminate = true };
            var cancel = new Button { Content = "Annuler", Width = 100, Margin = new Thickness(0, 12, 0, 0), HorizontalAlignment = HorizontalAlignment.Right };
            cancel.Click += (s, e) =>
            {
                cts.Cancel();
                cancel.IsEnabled = false;
                stageText.Text = "Annulation demandée : arrêt au prochain élément…";
            };
            var panel = new StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(stageText);
            panel.Children.Add(bar);
            panel.Children.Add(cancel);
            window = new Window
            {
                Title = title,
                Content = panel,
                Width = 440,
                SizeToContent = SizeToContent.Height,
                ResizeMode = ResizeMode.NoResize,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Topmost = true,
                ShowInTaskbar = false,
                Language = XmlLanguage.GetLanguage("fr-FR")
            };
            window.Closing += (s, e) =>
            {
                if (!closing)
                {
                    e.Cancel = true; // fermeture = annulation, jamais un abandon silencieux
                    cts.Cancel();
                }
            };
            window.Show();
            ready.Set();
            Dispatcher.Run();
        }

        private volatile bool closing;

        public void Report(string stage, int done, int total)
        {
            // Au plus 10 mises a jour par seconde : l'extraction ne doit pas attendre l'affichage.
            DateTime now = DateTime.UtcNow;
            if ((now - lastUpdate).TotalMilliseconds < 100 && done != total)
            {
                return;
            }
            lastUpdate = now;
            dispatcher?.BeginInvoke(new Action(() =>
            {
                if (cts.IsCancellationRequested)
                {
                    return;
                }
                stageText.Text = stage + " : " + done + " / " + total;
                bar.IsIndeterminate = total <= 0;
                bar.Maximum = Math.Max(1, total);
                bar.Value = done;
            }));
        }

        public void Dispose()
        {
            closing = true;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(new Action(() =>
                {
                    window?.Close();
                    dispatcher.InvokeShutdown();
                }));
            }
            cts.Dispose();
            ready.Dispose();
        }
    }
}
