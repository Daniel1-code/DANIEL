using System.Windows;
using System.Windows.Markup;

namespace DanCI.BOQ.UI.Studio
{
    /// <summary>
    /// Fenetre du Studio 4D (non modale), a placer a cote de la vue 3D de Revit. Le Gantt defile
    /// tout seul : horizontalement avec la date, verticalement vers la premiere tache en cours.
    /// </summary>
    public partial class Studio4DWindow : Window
    {
        private const double LeftColumns = 440, RowHeight = 26, HeaderHeight = 44;
        private DanCI.BOQ.UI.Themes.FuturisticTheme theme;

        public Studio4DWindow(Studio4DViewModel viewModel, bool placeAtBottom = true)
        {
            InitializeComponent();
            try
            {
                if (!DanCI.BOQ.UI.Themes.ThemeColors.IsClassic)
                {
                    theme = new DanCI.BOQ.UI.Themes.FuturisticTheme();
                    Resources.MergedDictionaries.Add(theme);
                }
            }
            catch (System.Exception)
            {
                // theme indisponible : controles standard
            }
            // Cockpit sombre, comme le tableau de bord.
            DanCI.BOQ.UI.Themes.ThemeColors.Attach(Resources, darkOnly: true);
            Language = XmlLanguage.GetLanguage("fr-FR");
            DataContext = viewModel;
            // Bas de l'ecran, pleine largeur : la vue 3D de Revit reste visible au-dessus.
            Loaded += (s, e) =>
            {
                if (placeAtBottom)
                {
                    Rect area = SystemParameters.WorkArea;
                    Width = System.Math.Min(area.Width, 1400);
                    Left = area.Left + (area.Width - Width) / 2;
                    Top = area.Bottom - Height;
                }
                viewModel.Open();
            };
            Closed += (s, e) => viewModel.Close();
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(Studio4DViewModel.Day)) FollowCursor(viewModel);
                else if (e.PropertyName == nameof(Studio4DViewModel.FocusRow)) FollowRow(viewModel.FocusRow);
            };
        }

        private double fullHeight = 340;

        /// <summary>Gantt masque : la fenetre se reduit au bandeau (en bas de l'ecran) ; affiche : hauteur precedente.</summary>
        private void ToggleGantt_Click(object sender, RoutedEventArgs e)
        {
            var vm = (Studio4DViewModel)DataContext;
            double bottom = Top + ActualHeight;
            if (vm.ShowGantt)
            {
                fullHeight = ActualHeight;
                vm.ShowGantt = false;
                SizeToContent = SizeToContent.Height;
            }
            else
            {
                vm.ShowGantt = true;
                SizeToContent = SizeToContent.Manual;
                Height = fullHeight;
            }
            UpdateLayout();
            Top = bottom - ActualHeight; // reste colle au bas de l'ecran
        }

        private void FollowCursor(Studio4DViewModel vm)
        {
            if (GanttScroll == null) return;
            double x = LeftColumns + vm.Boq.CursorLeft;
            if (x < GanttScroll.HorizontalOffset + LeftColumns + 40 || x > GanttScroll.HorizontalOffset + GanttScroll.ViewportWidth - 120)
            {
                GanttScroll.ScrollToHorizontalOffset(System.Math.Max(0, x - LeftColumns - (GanttScroll.ViewportWidth - LeftColumns) / 3));
            }
        }

        private void FollowRow(int row)
        {
            if (GanttScroll == null || row < 0) return;
            double y = HeaderHeight + row * RowHeight;
            if (y < GanttScroll.VerticalOffset + HeaderHeight || y > GanttScroll.VerticalOffset + GanttScroll.ViewportHeight - 2 * RowHeight)
            {
                GanttScroll.ScrollToVerticalOffset(System.Math.Max(0, y - HeaderHeight - RowHeight * 2));
            }
        }
    }
}
