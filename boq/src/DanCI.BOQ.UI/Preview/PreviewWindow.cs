using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using DanCI.BOQ.Core.Preview;

namespace DanCI.BOQ.UI.Preview
{
    /// <summary>
    /// Apercu AVANT export : les pages du PDF (images du meme document que le fichier) et les
    /// feuilles du classeur (valeurs calculees). Rien n'est ecrit sur le disque tant que
    /// l'utilisateur ne clique pas sur « Exporter ». Fenetre construite en code (pas de XAML).
    /// </summary>
    public sealed class PreviewWindow : Window
    {
        private static readonly Brush Night = Freeze(new SolidColorBrush(Color.FromRgb(0x07, 0x0D, 0x1A)));
        private static readonly Brush Panel = Freeze(new SolidColorBrush(Color.FromRgb(0x0F, 0x1A, 0x2E)));
        private static readonly Brush Line = Freeze(new SolidColorBrush(Color.FromRgb(0x1E, 0x3A, 0x5F)));
        private static readonly Brush Text = Freeze(new SolidColorBrush(Color.FromRgb(0xE6, 0xF1, 0xFF)));
        private static readonly Brush Muted = Freeze(new SolidColorBrush(Color.FromRgb(0x8A, 0xA4, 0xC8)));
        private static readonly Brush Cyan = Freeze(new SolidColorBrush(Color.FromRgb(0x00, 0xE5, 0xFF)));
        private static readonly Brush Yellow = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xC2, 0x33)));

        private readonly Action export;
        private readonly ScaleTransform zoom = new ScaleTransform(1, 1);

        /// <param name="pages">Pages du PDF en PNG ; null si le PDF ne peut pas etre rendu.</param>
        /// <param name="pdfProblem">Raison de l'absence d'apercu PDF (composants manquants...).</param>
        /// <param name="export">Action « Exporter » (null : bouton absent).</param>
        public PreviewWindow(string projectTitle, IReadOnlyList<byte[]> pages, string pdfProblem, IReadOnlyList<SheetPreview> sheets, Action export)
        {
            this.export = export;
            Title = "DanCI — Aperçu avant export";
            Width = 1180;
            Height = 860;
            MinWidth = 700;
            MinHeight = 500;
            Background = Night;
            Foreground = Text;
            FontSize = 12;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Language = System.Windows.Markup.XmlLanguage.GetLanguage("fr-FR");
            try
            {
                Resources.MergedDictionaries.Add(new DanCI.BOQ.UI.Themes.FuturisticTheme());
            }
            catch (Exception ex)
            {
                DanCI.BOQ.Core.Diagnostics.BoqLog.Warn("Thème de l'aperçu non chargé : " + ex.Message);
            }

            var root = new DockPanel { Margin = new Thickness(10) };
            root.Children.Add(TopBar(projectTitle, pages, sheets));
            var tabs = new TabControl();
            tabs.Items.Add(new TabItem { Header = "PDF — " + (pages == null ? "indisponible" : pages.Count + " page(s)"), Content = PdfView(pages, pdfProblem) });
            tabs.Items.Add(new TabItem { Header = "Excel — " + (sheets?.Count ?? 0) + " feuille(s)", Content = ExcelView(sheets) });
            root.Children.Add(tabs);
            Content = root;
        }

        public int PageCount { get; private set; }

        private UIElement TopBar(string projectTitle, IReadOnlyList<byte[]> pages, IReadOnlyList<SheetPreview> sheets)
        {
            var bar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
            DockPanel.SetDock(bar, Dock.Top);
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(buttons, Dock.Right);
            buttons.Children.Add(new TextBlock { Text = "Zoom", Foreground = Muted, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 6, 0) });
            var slider = new Slider { Minimum = 0.4, Maximum = 2.0, Value = 1.0, Width = 140, VerticalAlignment = VerticalAlignment.Center, TickFrequency = 0.1, IsSnapToTickEnabled = true };
            slider.ValueChanged += (s, e) =>
            {
                zoom.ScaleX = e.NewValue;
                zoom.ScaleY = e.NewValue;
            };
            buttons.Children.Add(slider);
            if (export != null)
            {
                var exportButton = new Button { Content = "Exporter Excel / PDF", Tag = "primary", Margin = new Thickness(16, 0, 4, 0) };
                exportButton.Click += (s, e) =>
                {
                    export();
                    Close();
                };
                buttons.Children.Add(exportButton);
            }
            var close = new Button { Content = "Fermer", IsCancel = true };
            close.Click += (s, e) => Close();
            buttons.Children.Add(close);
            bar.Children.Add(buttons);

            var title = new StackPanel();
            title.Children.Add(new TextBlock
            {
                Text = "APERÇU AVANT EXPORT",
                Foreground = Cyan,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Effect = new DropShadowEffect { Color = Color.FromRgb(0x00, 0xE5, 0xFF), BlurRadius = 14, ShadowDepth = 0, Opacity = 0.6 }
            });
            title.Children.Add(new TextBlock
            {
                Text = projectTitle + " — rien n'est écrit sur le disque avant « Exporter ». Les fichiers exportés seront identiques à cet aperçu.",
                Foreground = Muted,
                TextWrapping = TextWrapping.Wrap
            });
            bar.Children.Add(title);
            return bar;
        }

        // ------------------------------------------------------------------ PDF
        private UIElement PdfView(IReadOnlyList<byte[]> pages, string problem)
        {
            if (pages == null || pages.Count == 0)
            {
                return Message("Aperçu PDF indisponible" + (string.IsNullOrWhiteSpace(problem) ? "." : " : " + problem) +
                               Environment.NewLine + "L'aperçu Excel reste disponible dans l'onglet suivant.");
            }
            var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, LayoutTransform = zoom, Margin = new Thickness(0, 10, 0, 10) };
            for (int i = 0; i < pages.Count; i++)
            {
                BitmapImage bmp = Load(pages[i]);
                var image = new Image { Source = bmp, Stretch = Stretch.None, SnapsToDevicePixels = true };
                RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
                var frame = new Border
                {
                    Child = image,
                    BorderBrush = Line,
                    BorderThickness = new Thickness(1),
                    Margin = new Thickness(0, 0, 0, 4),
                    Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 16, ShadowDepth = 3, Opacity = 0.6 }
                };
                stack.Children.Add(frame);
                stack.Children.Add(new TextBlock
                {
                    Text = "Page " + (i + 1) + " / " + pages.Count,
                    Foreground = Muted,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 18)
                });
            }
            PageCount = pages.Count;
            return new ScrollViewer
            {
                Content = stack,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Night
            };
        }

        private static BitmapImage Load(byte[] png)
        {
            var bmp = new BitmapImage();
            using (var ms = new MemoryStream(png))
            {
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.StreamSource = ms;
                bmp.EndInit();
            }
            bmp.Freeze();
            return bmp;
        }

        // ------------------------------------------------------------------ Excel
        private UIElement ExcelView(IReadOnlyList<SheetPreview> sheets)
        {
            if (sheets == null || sheets.Count == 0)
            {
                return Message("Aperçu Excel indisponible.");
            }
            var tabs = new TabControl { TabStripPlacement = Dock.Bottom };
            foreach (SheetPreview sheet in sheets)
            {
                var item = new TabItem { Header = sheet.Name };
                tabs.Items.Add(item);
            }
            tabs.SelectionChanged += (s, e) =>
            {
                if (tabs.SelectedItem is TabItem t && t.Content == null)
                {
                    t.Content = SheetView(sheets[tabs.SelectedIndex]);
                }
            };
            tabs.SelectedIndex = 0;
            ((TabItem)tabs.Items[0]).Content = SheetView(sheets[0]);
            return tabs;
        }

        /// <summary>Une feuille dessinee comme dans Excel : largeurs, hauteurs, fusions, couleurs, gras.</summary>
        public static FrameworkElement SheetView(SheetPreview sheet)
        {
            var grid = new Grid { Background = Brushes.White, SnapsToDevicePixels = true };
            foreach (double w in sheet.ColumnWidths)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(8, w * 7.0 + 5)) });
            }
            if (grid.ColumnDefinitions.Count == 0)
            {
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            }
            var gridLine = Freeze(new SolidColorBrush(Color.FromRgb(0xE1, 0xE5, 0xEA)));
            for (int r = 0; r < sheet.Rows.Count; r++)
            {
                RowPreview row = sheet.Rows[r];
                grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(12, row.Height * 96.0 / 72.0)) });
                if (sheet.ShowGridLines)
                {
                    var line = new Border { BorderBrush = gridLine, BorderThickness = new Thickness(0, 0, 0, 1) };
                    Grid.SetRow(line, r);
                    Grid.SetColumnSpan(line, grid.ColumnDefinitions.Count);
                    grid.Children.Add(line);
                }
                foreach (CellPreview c in row.Cells)
                {
                    if (c.Column > grid.ColumnDefinitions.Count) continue;
                    var tb = new TextBlock
                    {
                        Text = c.Text ?? string.Empty,
                        FontWeight = c.Bold ? FontWeights.Bold : FontWeights.Normal,
                        FontStyle = c.Italic ? FontStyles.Italic : FontStyles.Normal,
                        FontSize = Math.Max(7, c.FontSize * 96.0 / 72.0 * 0.92),
                        Foreground = Brush(c.Foreground) ?? Brushes.Black,
                        TextAlignment = c.AlignRight ? TextAlignment.Right : TextAlignment.Left,
                        TextWrapping = c.Wrap ? TextWrapping.Wrap : TextWrapping.NoWrap,
                        TextTrimming = c.Wrap ? TextTrimming.None : TextTrimming.CharacterEllipsis,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(3, 0, 3, 0)
                    };
                    var cell = new Border { Background = Brush(c.Background), Child = tb, ClipToBounds = true };
                    Grid.SetRow(cell, r);
                    Grid.SetColumn(cell, c.Column - 1);
                    Grid.SetColumnSpan(cell, Math.Max(1, Math.Min(c.ColumnSpan, grid.ColumnDefinitions.Count - c.Column + 1)));
                    grid.Children.Add(cell);
                }
            }
            var page = new StackPanel { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(10) };
            page.Children.Add(new Border
            {
                Child = grid,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 12, ShadowDepth = 2, Opacity = 0.5 }
            });
            var notes = new List<string>();
            if (sheet.HiddenRows > 0) notes.Add(sheet.HiddenRows + " ligne(s) suivante(s) non affichée(s) dans l'aperçu : elles seront dans le fichier.");
            if (sheet.ChartCount > 0) notes.Add(sheet.ChartCount + " graphique(s) Excel (lots, répartition, articles, statuts) : dessinés dans le fichier, aux emplacements « GRAPHIQUES », pas dans cet aperçu.");
            if (sheet.PictureCount > 0) notes.Add(sheet.PictureCount + " image(s) (logo, rendu, page importée) : visibles dans le fichier, pas dans cet aperçu.");
            if (notes.Count > 0)
            {
                page.Children.Add(new TextBlock { Text = string.Join("  ", notes), Foreground = Yellow, Margin = new Thickness(0, 8, 0, 0), TextWrapping = TextWrapping.Wrap });
            }
            return new ScrollViewer
            {
                Content = page,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Background = Night
            };
        }

        private static UIElement Message(string text) => new Border
        {
            Background = Panel,
            Padding = new Thickness(20),
            Child = new TextBlock { Text = text, Foreground = Yellow, TextWrapping = TextWrapping.Wrap, FontSize = 14 }
        };

        private static Brush Brush(string hex)
        {
            if (string.IsNullOrEmpty(hex)) return null;
            try
            {
                return Freeze(new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)));
            }
            catch (FormatException)
            {
                return null;
            }
        }

        private static Brush Freeze(Brush b)
        {
            b.Freeze();
            return b;
        }
    }
}
