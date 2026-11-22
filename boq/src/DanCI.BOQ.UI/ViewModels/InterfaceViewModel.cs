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
        private string style;
        private string message = string.Empty;
        private bool isError;

        public InterfaceViewModel()
        {
            background = ThemeColors.Settings.Background;
            style = ThemeColors.Settings.IsClassic ? InterfaceSettings.Classic : InterfaceSettings.Futuristic;
            SetStyleCommand = new RelayCommand(p =>
            {
                Style = p as string == InterfaceSettings.Classic ? InterfaceSettings.Classic : InterfaceSettings.Futuristic;
                Save();
            });
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
                Style = InterfaceSettings.Futuristic;
                Save();
            });
        }

        public IReadOnlyList<BackgroundSwatch> Swatches { get; }
        public ICommand PickCommand { get; }
        public ICommand PreviewCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand SetStyleCommand { get; }

        /// <summary>« futuriste » ou « classique ».</summary>
        public string Style
        {
            get => style;
            private set
            {
                if (Set(ref style, value))
                {
                    Raise(nameof(IsClassic));
                    Raise(nameof(IsFuturistic));
                }
            }
        }

        public bool IsClassic => style == InterfaceSettings.Classic;
        public bool IsFuturistic => !IsClassic;

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
            ThemeColors.Preview(background, style);
            Report(IsClassic
                ? "Style classique appliqué (fond clair, contrôles Windows). Le tableau de bord et le panneau Diagramme restent sombres."
                : "Aperçu appliqué. Cliquez « Enregistrer » pour garder ce fond aux prochaines ouvertures.", false);
            return true;
        }

        private void Save()
        {
            if (!Preview()) return;
            try
            {
                ThemeColors.Save(background, style);
                Report(IsClassic
                    ? "Style classique enregistré (fond clair, comme les premières versions) : toutes les fenêtres DanCI BOQ, tous vos projets. Le tableau de bord et le panneau Diagramme restent sombres. Les couleurs des statuts et des lots déjà affichés s'ajustent à la prochaine ouverture de la fenêtre."
                    : "Style futuriste enregistré, fond " + background + " : toutes les fenêtres DanCI BOQ, tous vos projets.", false);
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
