using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows.Data;
using System.Windows.Input;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.UI.Mvvm;

namespace DanCI.BOQ.UI.ViewModels
{
    /// <summary>Saisie d'un nombre : virgule ou point, espaces de milliers acceptes ; vide = pas de valeur.</summary>
    public static class NumberInput
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static bool TryParse(string text, out decimal? value)
        {
            value = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return true;
            }
            string s = text.Trim().Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(" ", string.Empty).Replace(',', '.');
            if (decimal.TryParse(s, NumberStyles.Number, CultureInfo.InvariantCulture, out decimal d))
            {
                value = d;
                return true;
            }
            return false;
        }

        public static string Format(decimal? value) => value.HasValue ? value.Value.ToString("0.####", Fr) : string.Empty;
    }

    /// <summary>Periode d'un lot dans l'echeancier (saisie).</summary>
    public sealed class LotPeriodRow : ObservableObject
    {
        private int start = 1, duration = 1;
        public string Lot { get; set; }
        public string Designation { get; set; }
        public int Start { get => start; set => Set(ref start, Math.Max(1, value)); }
        public int Duration { get => duration; set => Set(ref duration, Math.Max(1, value)); }
    }

    /// <summary>Majoration de pied de devis editable.</summary>
    public sealed class MarkupRow : ObservableObject
    {
        private string code, designation, percent;
        private MarkupBase markupBase = MarkupBase.Travaux;
        public string Code { get => code; set => Set(ref code, value); }
        public string Designation { get => designation; set => Set(ref designation, value); }
        public string Percent { get => percent; set => Set(ref percent, value); }
        public MarkupBase Base { get => markupBase; set => Set(ref markupBase, value); }
    }

    /// <summary>Prix general d'un article, editable.</summary>
    public sealed class PriceRow : ObservableObject
    {
        private string unitPrice, source, materials, labour, equipment;
        private Currency currency;
        private DateTime? date;

        public PriceRow(PriceEdit e)
        {
            Code = e.Code;
            LotCode = e.LotCode;
            Designation = e.Designation;
            Unit = e.Unit;
            unitPrice = NumberInput.Format(e.UnitPrice);
            currency = e.Currency;
            date = e.Date;
            source = e.Source;
            materials = NumberInput.Format(e.Materials);
            labour = NumberInput.Format(e.Labour);
            equipment = NumberInput.Format(e.Equipment);
            SpecificPrices = e.SpecificPrices;
        }

        public string Code { get; }
        public string LotCode { get; }
        public string Designation { get; }
        public BoqUnit Unit { get; }
        public string UnitSymbol => Unit.Symbol();
        public int SpecificPrices { get; }
        public string SpecificLabel => SpecificPrices == 0 ? string.Empty : SpecificPrices + " prix par type (conservés)";

        public string UnitPrice
        {
            get => unitPrice;
            set
            {
                if (Set(ref unitPrice, value))
                {
                    Raise(nameof(HasNoPrice));
                    if (Date == null && !string.IsNullOrWhiteSpace(value)) Date = DateTime.Today;
                }
            }
        }

        public bool HasNoPrice => string.IsNullOrWhiteSpace(UnitPrice);
        public Currency Currency { get => currency; set => Set(ref currency, value); }
        public DateTime? Date { get => date; set => Set(ref date, value); }
        public string Source { get => source; set => Set(ref source, value); }
        public string Materials { get => materials; set => Set(ref materials, value); }
        public string Labour { get => labour; set => Set(ref labour, value); }
        public string Equipment { get => equipment; set => Set(ref equipment, value); }

        /// <summary>Saisie convertie ; null + message si un nombre est illisible.</summary>
        public PriceEdit ToEdit(List<string> errors)
        {
            var e = new PriceEdit { Code = Code, LotCode = LotCode, Designation = Designation, Unit = Unit, Currency = Currency, Date = Date, Source = Source };
            bool ok = true;
            ok &= Parse(UnitPrice, "PU", errors, v => e.UnitPrice = v);
            ok &= Parse(Materials, "matériaux", errors, v => e.Materials = v);
            ok &= Parse(Labour, "main-d'œuvre", errors, v => e.Labour = v);
            ok &= Parse(Equipment, "matériel", errors, v => e.Equipment = v);
            return ok ? e : null;
        }

        private bool Parse(string text, string label, List<string> errors, Action<decimal?> assign)
        {
            if (NumberInput.TryParse(text, out decimal? v))
            {
                assign(v);
                return true;
            }
            errors.Add("Article " + Code + " : " + label + " « " + text + " » n'est pas un nombre.");
            return false;
        }
    }

    public sealed class Choice<T>
    {
        public Choice(T value, string label)
        {
            Value = value;
            Label = label;
        }

        public T Value { get; }
        public string Label { get; }
        public override string ToString() => Label;
    }

    /// <summary>
    /// Onglet « Paramètres du projet » : identite, devis (devise, TVA, majorations, taux),
    /// prix unitaires et page de garde. Enregistre dans le fichier projet .danciboq.json et dans
    /// la bibliotheque de prix du projet ; rien n'est ecrit dans le modele Revit.
    /// </summary>
    public sealed class SettingsViewModel : ObservableObject
    {
        private ProjectInfo revit = new ProjectInfo();
        private EstimateSettings installation = new EstimateSettings();
        private bool pricesDirty;

        public SettingsViewModel()
        {
            PricesView = CollectionViewSource.GetDefaultView(Prices);
            PricesView.Filter = o => FilterPrice(o as PriceRow);
            PricesView.GroupDescriptions.Add(new PropertyGroupDescription(nameof(PriceRow.LotCode)));
            Prices.CollectionChanged += (s, e) => Raise(nameof(PricesSummary));
            AddMarkupCommand = new RelayCommand(_ => Markups.Add(new MarkupRow { Code = "MAJ" + (Markups.Count + 1), Designation = "Nouvelle majoration", Percent = "0" }));
            RemoveMarkupCommand = new RelayCommand(p => Markups.Remove(p as MarkupRow), p => p is MarkupRow);
            ResetEstimateCommand = new RelayCommand(_ => LoadEstimate(ProjectEstimate.From(installation), fromInstallation: true));
            ChooseProjectImageCommand = new RelayCommand(_ => ProjectImage = PickImage("Image du projet (rendu, perspective, photo)") ?? ProjectImage);
            ChooseImportedPageCommand = new RelayCommand(_ => ImportedPage = PickImage("Page de garde réalisée à part (A4 portrait)") ?? ImportedPage);
            ChooseLogoCommand = new RelayCommand(_ => Logo = PickImage("Logo du projet") ?? Logo);
            InitTemplateCommands();
            InitScheduleCommands();
            ClearImageCommand = new RelayCommand(p =>
            {
                switch (p as string)
                {
                    case "image": ProjectImage = null; break;
                    case "page": ImportedPage = null; break;
                    case "logo": Logo = null; break;
                }
            });
        }

        // ------------------------------------------------------------ client et entreprise
        public PartyViewModel ClientParty { get; } = new PartyViewModel("Client (maître d'ouvrage)",
            "Fiche propre à CE projet (fichier .danciboq.json). Le nom du client remplace le champ « Maître d'ouvrage » de l'identité. Son logo figure sur la page de garde.");

        public PartyViewModel Company { get; } = new PartyViewModel("Notre entreprise (émetteur du devis)",
            "Fiche COMMUNE à tous les projets, enregistrée dans %AppData%\\DanCI\\BOQ\\entreprise.json (les mises à jour de l'extension n'y touchent pas). Nom, coordonnées, mentions légales (RCCM, Id. Nat., NIF), banque et logo figurent sur la page de garde et le PDF.");

        // ------------------------------------------------------------ identite
        public string Name { get; set; }
        public string Number { get; set; }
        public string Purpose { get; set; }
        public string Client { get; set; }
        public string Architect { get; set; }
        public string Engineer { get; set; }
        public string Contractor { get; set; }
        public string Location { get; set; }
        public string IssueDate { get; set; }
        public string QuoteReference { get; set; }
        public string Revision { get; set; }
        public string Status { get; set; }
        public string Author { get; set; }
        public string CheckedBy { get; set; }
        public string ValidityDays { get; set; }
        public string ExecutionTime { get; set; }
        public string PaymentTerms { get; set; }
        public string Notes { get; set; }
        public string ReferenceArea { get; set; }
        public string ReferenceAreaLabel { get; set; }

        /// <summary>Valeurs de Revit, affichees en aide a cote des champs (un champ vide les reprend).</summary>
        public ProjectInfo Revit => revit;

        // ------------------------------------------------------------ devis
        public IReadOnlyList<Choice<Currency>> Currencies { get; } = new[] { new Choice<Currency>(Currency.USD, "USD"), new Choice<Currency>(Currency.CDF, "CDF") };
        public IReadOnlyList<Choice<Currency?>> SecondaryCurrencies { get; } = new[]
        {
            new Choice<Currency?>(null, "(aucune)"), new Choice<Currency?>(Currency.USD, "USD"), new Choice<Currency?>(Currency.CDF, "CDF")
        };
        public IReadOnlyList<Choice<MarkupBase>> MarkupBases => MarkupBaseChoices;

        /// <summary>Statiques : les colonnes de DataGrid ne voient pas le DataContext de la fenetre.</summary>
        public static IReadOnlyList<Choice<MarkupBase>> MarkupBaseChoices { get; } = new[]
        {
            new Choice<MarkupBase>(MarkupBase.Travaux, "sur les travaux"), new Choice<MarkupBase>(MarkupBase.Cumul, "en cascade (cumul)")
        };
        public static IReadOnlyList<Currency> PriceCurrencies { get; } = new[] { Currency.USD, Currency.CDF };
        public IReadOnlyList<Choice<CoverTheme>> Themes { get; } = new[]
        {
            new Choice<CoverTheme>(CoverTheme.Futuriste, "Futuriste (fond nuit, cyan et orange)"),
            new Choice<CoverTheme>(CoverTheme.Classique, "Classique (fond blanc, économe en encre)")
        };

        private Currency currency;
        private Currency? secondaryCurrency;
        private string vatPercent, rate, rateSource;
        private DateTime? rateDate;
        private string estimateOrigin;

        public Currency Currency { get => currency; set => Set(ref currency, value); }
        public Currency? SecondaryCurrency { get => secondaryCurrency; set => Set(ref secondaryCurrency, value); }
        public string VatPercent { get => vatPercent; set => Set(ref vatPercent, value); }
        public string Rate { get => rate; set => Set(ref rate, value); }
        public DateTime? RateDate { get => rateDate; set => Set(ref rateDate, value); }
        public string RateSource { get => rateSource; set => Set(ref rateSource, value); }
        public ObservableCollection<MarkupRow> Markups { get; } = new ObservableCollection<MarkupRow>();

        /// <summary>D'ou viennent les valeurs affichees (installation ou projet).</summary>
        public string EstimateOrigin { get => estimateOrigin; private set => Set(ref estimateOrigin, value); }

        public ICommand AddMarkupCommand { get; }
        public ICommand RemoveMarkupCommand { get; }
        public ICommand ResetEstimateCommand { get; }

        // ------------------------------------------------------------ prix
        public ObservableCollection<PriceRow> Prices { get; } = new ObservableCollection<PriceRow>();
        public ICollectionView PricesView { get; }

        private string priceFilter;
        private bool onlyMissing;

        public string PriceFilter
        {
            get => priceFilter;
            set
            {
                if (Set(ref priceFilter, value)) PricesView.Refresh();
            }
        }

        public bool OnlyMissing
        {
            get => onlyMissing;
            set
            {
                if (Set(ref onlyMissing, value)) PricesView.Refresh();
            }
        }

        public string PricesSummary => Prices.Count == 0 ? "Catalogue non chargé." :
            Prices.Count(p => !p.HasNoPrice) + " / " + Prices.Count + " articles ont un prix.";

        private string priceLibraryInfo;

        public string PriceLibraryInfo { get => priceLibraryInfo; set => Set(ref priceLibraryInfo, value); }

        private bool FilterPrice(PriceRow row)
        {
            if (row == null) return false;
            if (OnlyMissing && !row.HasNoPrice) return false;
            if (string.IsNullOrWhiteSpace(PriceFilter)) return true;
            string f = PriceFilter.Trim();
            return (row.Code ?? string.Empty).IndexOf(f, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (row.Designation ?? string.Empty).IndexOf(f, StringComparison.CurrentCultureIgnoreCase) >= 0;
        }

        // ------------------------------------------------------------ page de garde
        private CoverTheme theme = CoverTheme.Futuriste;
        private string subtitle, projectImage, importedPage, logo;

        public CoverTheme Theme { get => theme; set => Set(ref theme, value); }
        public string Subtitle { get => subtitle; set => Set(ref subtitle, value); }
        public string ProjectImage { get => projectImage; set => Set(ref projectImage, value); }
        public string ImportedPage { get => importedPage; set => Set(ref importedPage, value); }
        public string Logo { get => logo; set => Set(ref logo, value); }

        public ICommand ChooseProjectImageCommand { get; }
        public ICommand ChooseImportedPageCommand { get; }
        public ICommand ChooseLogoCommand { get; }
        public ICommand ClearImageCommand { get; }

        private static string PickImage(string title)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Title = title, Filter = "Image (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg" };
            return dialog.ShowDialog() == true ? dialog.FileName : null;
        }

        // ------------------------------------------------------------ echeancier
        private bool useSchedule;
        private int scheduleMonths = 12;
        private DateTime? scheduleStart;

        /// <summary>Echeancier inclus dans les exports (PDF et Excel).</summary>
        public bool UseSchedule { get => useSchedule; set => Set(ref useSchedule, value); }
        public int ScheduleMonths { get => scheduleMonths; set => Set(ref scheduleMonths, Math.Max(1, Math.Min(120, value))); }
        public DateTime? ScheduleStart { get => scheduleStart; set => Set(ref scheduleStart, value); }
        public ObservableCollection<LotPeriodRow> LotPeriods { get; } = new ObservableCollection<LotPeriodRow>();
        public ICommand SpreadLotsCommand { get; private set; }
        public ICommand SequenceLotsCommand { get; private set; }

        /// <summary>Lots du catalogue (ordre du catalogue) et periodes deja saisies.</summary>
        public void LoadSchedule(DanCI.BOQ.Core.Planning.ProjectSchedule schedule, IEnumerable<(string Code, string Designation)> lots)
        {
            UseSchedule = schedule != null;
            ScheduleMonths = schedule?.TotalMonths ?? 12;
            ScheduleStart = schedule?.StartDate;
            LotPeriods.Clear();
            foreach (var (code, designation) in lots ?? Enumerable.Empty<(string, string)>())
            {
                var saved = schedule?.Lots?.FirstOrDefault(l => string.Equals(l.Lot, code, StringComparison.OrdinalIgnoreCase));
                LotPeriods.Add(new LotPeriodRow { Lot = code, Designation = designation, Start = saved?.StartMonth ?? 1, Duration = saved?.DurationMonths ?? ScheduleMonths });
            }
        }

        public DanCI.BOQ.Core.Planning.ProjectSchedule BuildSchedule(List<string> errors)
        {
            if (!UseSchedule) return null;
            foreach (LotPeriodRow p in LotPeriods.Where(p => p.Start + p.Duration - 1 > ScheduleMonths))
            {
                errors.Add("Échéancier : le lot " + p.Lot + " finit au mois " + (p.Start + p.Duration - 1) + ", après la durée totale (" + ScheduleMonths + " mois).");
            }
            return new DanCI.BOQ.Core.Planning.ProjectSchedule
            {
                TotalMonths = ScheduleMonths,
                StartDate = ScheduleStart,
                Lots = LotPeriods.Select(p => new DanCI.BOQ.Core.Planning.LotPeriod { Lot = p.Lot, StartMonth = p.Start, DurationMonths = p.Duration }).ToList()
            };
        }

        private void InitScheduleCommands()
        {
            SpreadLotsCommand = new RelayCommand(_ =>
            {
                foreach (LotPeriodRow p in LotPeriods) { p.Start = 1; p.Duration = ScheduleMonths; }
            });
            // Proposition de depart : lots enchaines dans l'ordre du catalogue avec chevauchement (a ajuster).
            SequenceLotsCommand = new RelayCommand(_ =>
            {
                int n = LotPeriods.Count;
                if (n == 0) return;
                for (int i = 0; i < n; i++)
                {
                    int start = 1 + (int)Math.Floor((double)i * ScheduleMonths / (n + 1));
                    int duration = Math.Max(1, Math.Min(ScheduleMonths - start + 1, (int)Math.Ceiling(2.0 * ScheduleMonths / (n + 1))));
                    LotPeriods[i].Start = start;
                    LotPeriods[i].Duration = duration;
                }
            });
        }

        // ------------------------------------------------------------ modeles de devis
        public ObservableCollection<QuoteTemplate> Templates { get; } = new ObservableCollection<QuoteTemplate>();

        private QuoteTemplate selectedTemplate;
        private string templateName, templateDescription, templateStatus;

        public QuoteTemplate SelectedTemplate
        {
            get => selectedTemplate;
            set
            {
                if (Set(ref selectedTemplate, value) && value != null)
                {
                    TemplateName = value.Name;
                    TemplateDescription = value.Description;
                }
            }
        }

        public string TemplateName { get => templateName; set => Set(ref templateName, value); }
        public string TemplateDescription { get => templateDescription; set => Set(ref templateDescription, value); }
        public string TemplateStatus { get => templateStatus; set => Set(ref templateStatus, value); }
        public bool IncludeEstimate { get; set; } = true;
        public bool IncludePrices { get; set; } = true;
        public bool IncludeCover { get; set; } = true;
        public bool IncludeConditions { get; set; } = true;
        public bool IncludeClient { get; set; }

        /// <summary>Dossier des modeles (modifiable pour les tests).</summary>
        public string TemplatesFolder { get; set; } = QuoteTemplateStore.DefaultFolder;

        public ICommand SaveTemplateCommand { get; private set; }
        public ICommand ApplyTemplateCommand { get; private set; }
        public ICommand DeleteTemplateCommand { get; private set; }
        public ICommand RefreshTemplatesCommand { get; private set; }

        private void InitTemplateCommands()
        {
            SaveTemplateCommand = new RelayCommand(_ => SaveTemplate(), _ => !string.IsNullOrWhiteSpace(TemplateName));
            ApplyTemplateCommand = new RelayCommand(_ => ApplyTemplate(SelectedTemplate), _ => SelectedTemplate != null);
            DeleteTemplateCommand = new RelayCommand(_ =>
            {
                if (SelectedTemplate == null) return;
                if (System.Windows.MessageBox.Show("Supprimer le modèle « " + SelectedTemplate.Name + " » ?", "DanCI — modèles",
                        System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question) != System.Windows.MessageBoxResult.Yes) return;
                QuoteTemplateStore.Delete(TemplatesFolder, SelectedTemplate.Name);
                TemplateStatus = "Modèle supprimé : " + SelectedTemplate.Name;
                RefreshTemplates();
            }, _ => SelectedTemplate != null);
            RefreshTemplatesCommand = new RelayCommand(_ => RefreshTemplates());
        }

        public void RefreshTemplates()
        {
            var issues = new List<BoqIssue>();
            Templates.Clear();
            foreach (QuoteTemplate t in QuoteTemplateStore.List(TemplatesFolder, issues))
            {
                Templates.Add(t);
            }
            if (issues.Count > 0)
            {
                TemplateStatus = "Modèle(s) illisible(s) ignoré(s) : " + string.Join(" | ", issues.Take(2).Select(i => i.File + " : " + i.Message));
            }
        }

        /// <summary>Modele a partir de la SAISIE en cours (elle n'a pas besoin d'etre enregistree).</summary>
        public QuoteTemplate BuildTemplate(List<string> errors)
        {
            var t = new QuoteTemplate { Name = TemplateName?.Trim(), Description = TemplateDescription?.Trim() };
            if (IncludeEstimate) t.Estimate = BuildEstimate(errors);
            if (IncludeCover)
            {
                CoverSettings c = BuildCover();
                c.ImportedPage = null; // propre a un projet
                c.ProjectImage = null;
                t.Cover = c;
            }
            if (IncludeConditions)
            {
                t.Conditions = new QuoteConditions { Status = Clean(Status), ValidityDays = Clean(ValidityDays), ExecutionTime = Clean(ExecutionTime), PaymentTerms = Clean(PaymentTerms), Notes = Clean(Notes) };
            }
            if (IncludeClient) t.Client = ClientParty.Build();
            if (IncludePrices)
            {
                t.Prices = BuildPrices(errors).Where(p => p.UnitPrice.HasValue).Select(PriceEditor.ToEntry).ToList();
            }
            return t;
        }

        private void SaveTemplate()
        {
            var errors = new List<string>();
            QuoteTemplate t = BuildTemplate(errors);
            if (errors.Count > 0)
            {
                TemplateStatus = "Modèle NON enregistré : " + string.Join(" | ", errors.Take(3));
                return;
            }
            string path = QuoteTemplateStore.Save(TemplatesFolder, t);
            TemplateStatus = "Modèle enregistré (" + t.Contents + ") : " + path;
            RefreshTemplates();
            SelectedTemplate = Templates.FirstOrDefault(x => x.Name == t.Name);
        }

        /// <summary>
        /// Applique un modele a la SAISIE : rien n'est enregistre ni recalcule avant « Enregistrer
        /// les paramètres du projet ». Un prix dont l'unite ne correspond pas a l'article est ignore.
        /// </summary>
        public string ApplyTemplate(QuoteTemplate t)
        {
            if (t == null) return null;
            var applied = new List<string>();
            if (t.Estimate != null)
            {
                LoadEstimate(t.Estimate, fromInstallation: false);
                EstimateOrigin = "Valeurs du modèle « " + t.Name + " » (à enregistrer).";
                applied.Add("TVA et majorations");
            }
            if (t.Cover != null)
            {
                Theme = t.Cover.Theme;
                if (!string.IsNullOrWhiteSpace(t.Cover.Subtitle)) Subtitle = t.Cover.Subtitle;
                if (!string.IsNullOrWhiteSpace(t.Cover.Logo)) Logo = t.Cover.Logo;
                applied.Add("page de garde");
            }
            if (t.Conditions != null)
            {
                Status = t.Conditions.Status ?? Status;
                ValidityDays = t.Conditions.ValidityDays ?? ValidityDays;
                ExecutionTime = t.Conditions.ExecutionTime ?? ExecutionTime;
                PaymentTerms = t.Conditions.PaymentTerms ?? PaymentTerms;
                Notes = t.Conditions.Notes ?? Notes;
                Raise(string.Empty);
                applied.Add("conditions");
            }
            if (t.Client != null && !t.Client.IsEmpty)
            {
                ClientParty.Load(t.Client);
                applied.Add("client");
            }
            if (t.Prices != null && t.Prices.Count > 0)
            {
                int done = 0, skipped = 0;
                foreach (PriceEntry p in t.Prices.Where(p => !p.IsTypeSpecific))
                {
                    PriceRow row = Prices.FirstOrDefault(r => r.Code == p.Code);
                    if (row == null || row.Unit != p.Unit)
                    {
                        skipped++;
                        continue;
                    }
                    row.UnitPrice = NumberInput.Format(p.UnitPrice);
                    row.Currency = p.Currency;
                    row.Date = p.Date;
                    row.Source = string.IsNullOrWhiteSpace(p.Source) ? "Modèle « " + t.Name + " »" : p.Source;
                    row.Materials = NumberInput.Format(p.Breakdown?.Materials);
                    row.Labour = NumberInput.Format(p.Breakdown?.Labour);
                    row.Equipment = NumberInput.Format(p.Breakdown?.Equipment);
                    done++;
                }
                applied.Add(done + " prix" + (skipped > 0 ? " (" + skipped + " ignoré(s) : article absent ou unité différente)" : string.Empty));
            }
            TemplateStatus = "Modèle « " + t.Name + " » appliqué : " + string.Join(", ", applied) +
                             ". Vérifiez, puis cliquez sur « Enregistrer les paramètres du projet ».";
            return TemplateStatus;
        }

        // ------------------------------------------------------------ chargement / enregistrement
        public void Load(ProjectFile project, string projectFilePath, ProjectInfo revitInfo, EstimateSettings installationSettings)
        {
            revit = revitInfo ?? new ProjectInfo();
            installation = installationSettings ?? new EstimateSettings();
            ProjectDetails d = project?.Details ?? new ProjectDetails();
            Name = d.Name; Number = d.Number; Purpose = d.Purpose; Client = d.Client; Architect = d.Architect; Engineer = d.Engineer;
            Contractor = d.Contractor; Location = d.Location; IssueDate = d.IssueDate; QuoteReference = d.QuoteReference; Revision = d.Revision;
            Status = d.Status; Author = d.Author; CheckedBy = d.CheckedBy; ValidityDays = d.ValidityDays; ExecutionTime = d.ExecutionTime;
            PaymentTerms = d.PaymentTerms; Notes = d.Notes; ReferenceArea = NumberInput.Format(d.ReferenceArea); ReferenceAreaLabel = d.ReferenceAreaLabel;
            Raise(string.Empty);

            LoadEstimate(project?.Estimate ?? ProjectEstimate.From(installation), fromInstallation: project?.Estimate == null);

            CoverSettings c = project?.Cover?.Resolve(projectFilePath) ?? new CoverSettings();
            Theme = c.Theme;
            Subtitle = c.Subtitle;
            ProjectImage = c.ProjectImage;
            ImportedPage = c.ImportedPage;
            Logo = c.Logo;
            RefreshTemplates();
        }

        private void LoadEstimate(ProjectEstimate e, bool fromInstallation)
        {
            Currency = e.Currency;
            SecondaryCurrency = e.SecondaryCurrency;
            VatPercent = NumberInput.Format(e.VatPercent);
            Rate = NumberInput.Format(e.ExchangeRate?.CdfPerUsd);
            RateDate = e.ExchangeRate?.Date;
            RateSource = e.ExchangeRate?.Source;
            Markups.Clear();
            foreach (Markup m in e.Markups ?? new List<Markup>())
            {
                Markups.Add(new MarkupRow { Code = m.Code, Designation = m.Designation, Percent = NumberInput.Format(m.Percent), Base = m.Base });
            }
            EstimateOrigin = fromInstallation
                ? "Valeurs actuelles : réglages de l'installation (config\\parametres_devis.json). Elles deviennent celles du projet à l'enregistrement."
                : "Valeurs propres à ce projet (fichier .danciboq.json).";
        }

        public void SetPrices(IEnumerable<PriceEdit> rows, string libraryInfo)
        {
            Prices.Clear();
            foreach (PriceEdit e in rows ?? Enumerable.Empty<PriceEdit>())
            {
                var row = new PriceRow(e);
                row.PropertyChanged += (s, a) =>
                {
                    pricesDirty = true;
                    if (a.PropertyName == nameof(PriceRow.UnitPrice)) Raise(nameof(PricesSummary));
                };
                Prices.Add(row);
            }
            pricesDirty = false;
            PriceLibraryInfo = libraryInfo;
            Raise(nameof(PricesSummary));
        }

        public bool PricesChanged => pricesDirty;

        /// <summary>Identite saisie (null si tout est vide : Revit fait foi).</summary>
        public ProjectDetails BuildDetails(List<string> errors)
        {
            decimal? area = null;
            if (!NumberInput.TryParse(ReferenceArea, out area) || area < 0m)
            {
                errors.Add("Surface de référence « " + ReferenceArea + " » : nombre positif attendu (m²).");
            }
            var d = new ProjectDetails
            {
                Name = Clean(Name), Number = Clean(Number), Purpose = Clean(Purpose), Client = Clean(Client), Architect = Clean(Architect),
                Engineer = Clean(Engineer), Contractor = Clean(Contractor), Location = Clean(Location), IssueDate = Clean(IssueDate),
                QuoteReference = Clean(QuoteReference), Revision = Clean(Revision), Status = Clean(Status), Author = Clean(Author),
                CheckedBy = Clean(CheckedBy), ValidityDays = Clean(ValidityDays), ExecutionTime = Clean(ExecutionTime),
                PaymentTerms = Clean(PaymentTerms), Notes = Clean(Notes), ReferenceArea = area > 0m ? area : null,
                ReferenceAreaLabel = Clean(ReferenceAreaLabel)
            };
            return d;
        }

        public ProjectEstimate BuildEstimate(List<string> errors)
        {
            var e = new ProjectEstimate { Currency = Currency, SecondaryCurrency = SecondaryCurrency };
            if (!NumberInput.TryParse(VatPercent, out decimal? vat) || !vat.HasValue)
            {
                errors.Add("TVA « " + VatPercent + " » : pourcentage attendu (ex. 16).");
            }
            else
            {
                e.VatPercent = vat.Value;
            }
            if (!NumberInput.TryParse(Rate, out decimal? r))
            {
                errors.Add("Taux de change « " + Rate + " » : nombre attendu (CDF pour 1 USD).");
            }
            else if (r.HasValue)
            {
                e.ExchangeRate = new ExchangeRate { CdfPerUsd = r.Value, Date = RateDate, Source = Clean(RateSource) };
            }
            foreach (MarkupRow m in Markups)
            {
                if (!NumberInput.TryParse(m.Percent, out decimal? p) || !p.HasValue)
                {
                    errors.Add("Majoration « " + m.Designation + " » : pourcentage « " + m.Percent + " » illisible.");
                    continue;
                }
                e.Markups.Add(new Markup { Code = Clean(m.Code), Designation = Clean(m.Designation), Percent = p.Value, Base = m.Base });
            }
            return e;
        }

        public CoverSettings BuildCover() => new CoverSettings
        {
            Theme = Theme,
            Subtitle = Clean(Subtitle),
            ProjectImage = Clean(ProjectImage),
            ImportedPage = Clean(ImportedPage),
            Logo = Clean(Logo)
        };

        public List<PriceEdit> BuildPrices(List<string> errors)
        {
            var edits = new List<PriceEdit>();
            foreach (PriceRow row in Prices)
            {
                PriceEdit e = row.ToEdit(errors);
                if (e != null) edits.Add(e);
            }
            return edits;
        }

        /// <summary>Identite finale (saisie, a defaut Revit), pour le tableau de bord.</summary>
        public ProjectInfo MergedInfo()
        {
            var ignored = new List<string>();
            return BuildDetails(ignored).Merge(revit);
        }

        private static string Clean(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
