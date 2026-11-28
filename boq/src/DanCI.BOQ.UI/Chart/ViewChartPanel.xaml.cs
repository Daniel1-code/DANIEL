using System.Windows.Controls;

namespace DanCI.BOQ.UI.Chart
{
    /// <summary>Contenu du panneau ancrable « DanCI — Diagramme du métré » (aucun appel a l'API Revit).</summary>
    public partial class ViewChartPanel : UserControl
    {
        public ViewChartPanel(ViewChartViewModel viewModel)
        {
            InitializeComponent();
            DanCI.BOQ.UI.Themes.ThemeColors.Attach(Resources, darkOnly: true);
            DataContext = viewModel;
            // Simulation 4D : la liste defile vers la tache en cours.
            viewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName != nameof(ViewChartViewModel.FocusIndex) || viewModel.FocusIndex < 0) return;
                Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    if (RowsList.ItemContainerGenerator.ContainerFromIndex(viewModel.FocusIndex) is System.Windows.FrameworkElement item)
                    {
                        var p = item.TransformToAncestor(RowsList).Transform(new System.Windows.Point(0, 0));
                        RowsScroll.ScrollToVerticalOffset(System.Math.Max(0, p.Y - 40));
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);
            };
        }

        public ViewChartViewModel ViewModel => (ViewChartViewModel)DataContext;
    }
}
