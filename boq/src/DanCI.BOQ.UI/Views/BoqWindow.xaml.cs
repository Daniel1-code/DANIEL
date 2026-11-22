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
            ApplyTheme();
            // Formats francais (1 234,56) dans toutes les liaisons.
            Language = XmlLanguage.GetLanguage("fr-FR");
            DataContext = viewModel;
        }

        public BoqWindowViewModel ViewModel => (BoqWindowViewModel)DataContext;

        /// <summary>Message si le theme n'a pas pu etre charge (fenetre alors en controles standard).</summary>
        public static string ThemeError { get; private set; }

        /// <summary>
        /// Theme futuriste des controles. Un echec (ressource introuvable, conflit avec un autre
        /// add-in) ne bloque jamais la fenetre : elle s'ouvre avec les controles Windows standard.
        /// </summary>
        private void ApplyTheme()
        {
            try
            {
                Resources.MergedDictionaries.Add(new DanCI.BOQ.UI.Themes.FuturisticTheme());
                ThemeError = null;
            }
            catch (System.Exception ex)
            {
                ThemeError = ex.GetType().Name + " : " + ex.Message;
            }
        }
    }
}
