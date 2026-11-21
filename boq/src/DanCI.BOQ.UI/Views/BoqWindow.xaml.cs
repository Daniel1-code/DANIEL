using System.Windows;
using System.Windows.Markup;
using DanCI.BOQ.UI.ViewModels;

namespace DanCI.BOQ.UI.Views
{
    /// <summary>Fenetre NON MODALE de revue du BOQ. Aucune logique ici : tout est dans le modele de vue.</summary>
    public partial class BoqWindow : Window
    {
        public BoqWindow(BoqWindowViewModel viewModel)
        {
            InitializeComponent();
            // Formats francais (1 234,56) dans toutes les liaisons.
            Language = XmlLanguage.GetLanguage("fr-FR");
            DataContext = viewModel;
        }

        public BoqWindowViewModel ViewModel => (BoqWindowViewModel)DataContext;
    }
}
