using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Input;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.Mvvm;
using DanCI.BOQ.UI.Themes;

namespace DanCI.BOQ.UI.ViewModels
{
    public sealed class BackgroundSwatch
    {
        public string Name { get; set; }
        public string Color { get; set; }
        public string Brush => "#FF" + Color.TrimStart('#');
    }

    /// <summary>
    /// Parametres > Interface : couleur de fond du logiciel (preference de l'utilisateur, tous projets).
    /// Changement visible tout de suite ; « Enregistrer » le garde pour les prochaines ouvertures.
    /// </summary>
    public sealed class InterfaceViewModel : ObservableObject
    {
        private string background;
        private string message = string.Empty;
        private bool isError;

        public InterfaceViewModel()
        {
            background = ThemeColors.Settings.Background;
            Swatches = BackgroundShades.Presets.Select(p => new BackgroundSwatch { Name = p.Name, Color = p.Color }).ToList();
            PickCommand = new RelayCommand(p =>
            {
                if (p is BackgroundSwatch s)
                {
                    Background = s.Color;
                    Preview();
                }
            });
            PreviewCommand = new RelayCommand(_ => Preview());
            SaveCommand = new RelayCommand(_ => Save());
            ResetCommand = new RelayCommand(_ =>
            {
                Background = InterfaceSettings.DefaultBackground;
                Save();
            });
        }

        public IReadOnlyList<BackgroundSwatch> Swatches { get; }
        public ICommand PickCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }

        /// <summary>Couleur saisie (#RRGGBB).</summary>
        public string Background
        {
            get => background;
            set
            {
                if (Set(ref background, value))
                {
                    Raise(nameof(SampleBrush));
                }
            }
        }

        /// <summary>Echantillon de la couleur saisie (gris si elle n'est pas lisible).</summary>
        public string SampleBrush
        {
            get
            {
                string n = BackgroundShades.Normalize(background);
                return n == null ? "#FF3A3A3A" : "#FF" + n.TrimStart('#');
            }
        }

        public string Message
        {
            get => message;
            private set => Set(ref message, value);
        }

        public string MessageColor => isError ? "#FFFF4D6D" : "#FF21E6A1";

        public string FilePath => InterfaceSettingsStore.DefaultPath;

        private bool Preview()
        {
            string problem = BackgroundShades.Validate(background);
            if (problem != null)
            {
                Report(problem, true);
                return false;
            }
            Background = BackgroundShades.Normalize(background);
            ThemeColors.Preview(background);
            Report("Aperçu appliqué. Cliquez « Enregistrer » pour garder ce fond aux prochaines ouvertures.", false);
            return true;
        }

        private void Save()
        {
            if (!Preview()) return;
            try
            {
                ThemeColors.Save(background);
                Report("Fond enregistré (" + background + ") : il s'applique à toutes les fenêtres DanCI BOQ, pour tous vos projets.", false);
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                Report("Fond appliqué mais NON enregistré : " + ex.Message, true);
            }
        }

        private void Report(string text, bool error)
        {
            isError = error;
            Message = text;
            Raise(nameof(MessageColor));
        }
    }
}
