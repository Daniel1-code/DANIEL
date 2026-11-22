using System.Windows.Input;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.Mvvm;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Fiche editable d'une societe (le client du projet, ou notre entreprise), logo compris.</summary>
    public sealed class PartyViewModel : ObservableObject
    {
        private string logo;

        public PartyViewModel(string title, string hint)
        {
            Title = title;
            Hint = hint;
            ChooseLogoCommand = new RelayCommand(_ =>
            {
                var dialog = new Microsoft.Win32.OpenFileDialog { Title = "Logo — " + title, Filter = "Image (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
                if (dialog.ShowDialog() == true)
                {
                    Logo = dialog.FileName;
                }
            });
            ClearLogoCommand = new RelayCommand(_ => Logo = null, _ => !string.IsNullOrEmpty(Logo));
        }

        public string Title { get; }
        public string Hint { get; }

        public string ShortName { get; set; }
        public string Name { get; set; }
        public string Activity { get; set; }
        public string Address { get; set; }
        public string City { get; set; }
        public string Country { get; set; }
        public string Phone { get; set; }
        public string Email { get; set; }
        public string Website { get; set; }
        public string Contact { get; set; }
        public string ContactRole { get; set; }
        public string Rccm { get; set; }
        public string IdNat { get; set; }
        public string Nif { get; set; }
        public string Bank { get; set; }

        /// <summary>Chemin absolu du logo, ou null.</summary>
        public string Logo
        {
            get => logo;
            set
            {
                if (Set(ref logo, value))
                {
                    Raise(nameof(LogoPreview));
                    Raise(nameof(LogoLabel));
                }
            }
        }

        /// <summary>Source de l'apercu (null si absent ou introuvable : pas d'erreur de liaison).</summary>
        public string LogoPreview => !string.IsNullOrEmpty(Logo) && System.IO.File.Exists(Logo) ? Logo : null;

        public string LogoLabel => string.IsNullOrEmpty(Logo) ? "(aucun logo)" : (System.IO.File.Exists(Logo) ? Logo : "INTROUVABLE : " + Logo);

        public ICommand ChooseLogoCommand { get; }
        public ICommand ClearLogoCommand { get; }

        public void Load(PartyInfo p)
        {
            p = p ?? new PartyInfo();
            ShortName = p.ShortName; Name = p.Name; Activity = p.Activity; Address = p.Address; City = p.City; Country = p.Country;
            Phone = p.Phone; Email = p.Email; Website = p.Website; Contact = p.Contact; ContactRole = p.ContactRole;
            Rccm = p.Rccm; IdNat = p.IdNat; Nif = p.Nif; Bank = p.Bank;
            Raise(string.Empty);
            Logo = p.Logo;
        }

        /// <summary>Fiche saisie (logo en chemin absolu).</summary>
        public PartyInfo Build() => new PartyInfo
        {
            ShortName = C(ShortName), Name = C(Name), Activity = C(Activity), Address = C(Address), City = C(City), Country = C(Country),
            Phone = C(Phone), Email = C(Email), Website = C(Website), Contact = C(Contact), ContactRole = C(ContactRole),
            Rccm = C(Rccm), IdNat = C(IdNat), Nif = C(Nif), Bank = C(Bank), Logo = C(Logo)
        };

        private static string C(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
