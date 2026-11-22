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
        }

        public ViewChartViewModel ViewModel => (ViewChartViewModel)DataContext;
    }
}
