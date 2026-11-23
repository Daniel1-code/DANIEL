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
            // Apparence choisie (Parametres > Interface), mise a jour en direct.
            DanCI.BOQ.UI.Themes.ThemeColors.Attach(Resources);
            // Le tableau de bord reste un cockpit sombre, meme en style classique.
            DanCI.BOQ.UI.Themes.ThemeColors.Attach(DashboardRoot.Resources, darkOnly: true);
            System.EventHandler onStyle = (s, e) => ApplyTheme();
            DanCI.BOQ.UI.Themes.ThemeColors.StyleChanged += onStyle;
            Closed += (s, e) => DanCI.BOQ.UI.Themes.ThemeColors.StyleChanged -= onStyle;
            // Formats francais (1 234,56) dans toutes les liaisons.
            Language = XmlLanguage.GetLanguage("fr-FR");
            DataContext = viewModel;
        }

        public BoqWindowViewModel ViewModel => (BoqWindowViewModel)DataContext;

        // ---- Gantt interactif : glisser une barre decale la tache ; un simple clic montre ses elements.
        private void GanttBar_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            var thumb = (System.Windows.Controls.Primitives.Thumb)sender;
            var move = thumb.RenderTransform as System.Windows.Media.TranslateTransform;
            if (move == null)
            {
                move = new System.Windows.Media.TranslateTransform();
                thumb.RenderTransform = move;
            }
            move.X += e.HorizontalChange;
        }

        private void GanttBar_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            var thumb = (System.Windows.Controls.Primitives.Thumb)sender;
            double dx = (thumb.RenderTransform as System.Windows.Media.TranslateTransform)?.X ?? 0;
            thumb.RenderTransform = null;
            var row = thumb.DataContext as GanttRowView;
            if (System.Math.Abs(dx) < 3) ViewModel.SelectTaskCommand.Execute(row);
            else ViewModel.ShiftTask(row, dx);
        }

        /// <summary>Entree valide la duree saisie (comme Tab).</summary>
        private void DurationBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key != System.Windows.Input.Key.Enter) return;
            var box = (System.Windows.Controls.TextBox)sender;
            box.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty)?.UpdateSource();
            e.Handled = true;
        }

        /// <summary>Message si le theme n'a pas pu etre charge (fenetre alors en controles standard).</summary>
        public static string ThemeError { get; private set; }

        /// <summary>
        /// Theme futuriste des controles. Un echec (ressource introuvable, conflit avec un autre
        /// add-in) ne bloque jamais la fenetre : elle s'ouvre avec les controles Windows standard.
        /// </summary>
        private DanCI.BOQ.UI.Themes.FuturisticTheme theme;

        /// <summary>
        /// Style futuriste : theme des controles ajoute. Style classique : controles Windows standard
        /// (comme les premieres versions). Un echec du theme ne bloque jamais la fenetre.
        /// </summary>
        private void ApplyTheme()
        {
            try
            {
                bool classic = DanCI.BOQ.UI.Themes.ThemeColors.IsClassic;
                if (classic && theme != null)
                {
                    Resources.MergedDictionaries.Remove(theme);
                    theme = null;
                }
                else if (!classic && theme == null)
                {
                    theme = new DanCI.BOQ.UI.Themes.FuturisticTheme();
                    Resources.MergedDictionaries.Add(theme);
                }
                ThemeError = null;
            }
            catch (System.Exception ex)
            {
                ThemeError = ex.GetType().Name + " : " + ex.Message;
            }
        }
    }
}
