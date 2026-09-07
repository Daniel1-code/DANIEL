using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Settings;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Documentation.Reports;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des poteaux : parametres a gauche, tableau et note de calcul
    /// au centre, coupe et quantitatif a droite. Elle n'orchestre pas le calcul : elle appelle
    /// <see cref="DesignPipeline"/>, le meme point d'entree que les tests et le mode batch.
    /// </summary>
    public partial class ColumnDesignWindow : Window
    {
        private const int PreviewPixels = 620;

        private readonly List<ColumnData> _columns;
        private readonly PresetStore<ColumnDesignSettings> _store =
            new PresetStore<ColumnDesignSettings>("column-presets.json");
        private List<Preset<ColumnDesignSettings>> _presets =
            new List<Preset<ColumnDesignSettings>>();
        private bool _loading;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public ColumnDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<ColumnDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<ColumnReportItem> Items { get; private set; }

        public ColumnDesignWindow(List<ColumnData> columns, ColumnDesignSettings settings)
        {
            InitializeComponent();
            _columns = columns;
            Settings = settings;
            Results = new List<ColumnDesignResult>();
            Items = new List<ColumnReportItem>();

            FillDiameterLists();
            ReloadPresets();
            WriteSettingsToUi(settings);
            Calculate(false);
        }

        // ------------------------------------------------------------------
        // Configurations enregistrees
        // ------------------------------------------------------------------

        private static List<Preset<ColumnDesignSettings>> BuiltInPresets()
        {
            var seismic = new ColumnDesignSettings
            {
                Seismic = true,
                TargetRatioPercent = 1.2,
                CoverMm = 35.0
            };
            var heavy = new ColumnDesignSettings
            {
                ConcreteStrengthMPa = 35.0,
                TargetRatioPercent = 2.0,
                CoverMm = 35.0
            };
            var verified = new ColumnDesignSettings
            {
                VerifyCapacity = true,
                AxialLoadKn = 1000.0,
                MomentAboutXKnm = 50.0,
                BucklingFactor = 0.7
            };

            return new List<Preset<ColumnDesignSettings>>
            {
                new Preset<ColumnDesignSettings>
                    { Name = "Poteau courant (C25/30, 1 %)", Settings = new ColumnDesignSettings() },
                new Preset<ColumnDesignSettings>
                    { Name = "Poteau sismique (zones critiques allongees)", Settings = seismic },
                new Preset<ColumnDesignSettings>
                    { Name = "Poteau fortement charge (C35/45, 2 %)", Settings = heavy },
                new Preset<ColumnDesignSettings>
                    { Name = "Poteau verifie N-M (exemple a adapter)", Settings = verified }
            };
        }

        private void ReloadPresets()
        {
            _loading = true;
            string previous = cmbPreset.Text;

            _presets = BuiltInPresets();
            _presets.AddRange(_store.Load());

            cmbPreset.Items.Clear();
            foreach (Preset<ColumnDesignSettings> preset in _presets) cmbPreset.Items.Add(preset.Name);
            cmbPreset.Text = previous;

            _loading = false;
        }

        private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var name = cmbPreset.SelectedItem as string;
            if (name == null) return;

            Preset<ColumnDesignSettings> preset = _presets.FirstOrDefault(p => p.Name == name);
            if (preset == null || preset.Settings == null) return;

            WriteSettingsToUi(preset.Settings);
            cmbPreset.Text = name;
            Calculate(false);
        }

        private void OnSavePresetClick(object sender, RoutedEventArgs e)
        {
            string name = (cmbPreset.Text ?? string.Empty).Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "Tapez un nom dans la liste deroulante avant d'enregistrer.",
                                "Configuration", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ColumnDesignSettings settings;
            List<string> errors;
            if (!ReadSettingsFromUi(out settings, out errors))
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                                "Parametres incorrects", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string error;
            if (!_store.AddOrReplace(name, settings, out error))
            {
                MessageBox.Show(this, error, "Enregistrement impossible",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ReloadPresets();
            cmbPreset.Text = name;
            txtStatus.Text = "Configuration \"" + name + "\" enregistree.";
        }

        private void OnDeletePresetClick(object sender, RoutedEventArgs e)
        {
            string name = (cmbPreset.Text ?? string.Empty).Trim();
            if (name.Length == 0) return;

            if (BuiltInPresets().Any(p => p.Name == name))
            {
                MessageBox.Show(this, "Les configurations livrees avec le plugin ne peuvent pas " +
                                      "etre supprimees.", "Configuration",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string error;
            if (!_store.Remove(name, out error))
            {
                MessageBox.Show(this, error, "Suppression impossible",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ReloadPresets();
            cmbPreset.Text = string.Empty;
            txtStatus.Text = "Configuration \"" + name + "\" supprimee.";
        }

        // ------------------------------------------------------------------
        // Transfert interface <-> reglages
        // ------------------------------------------------------------------

        private void FillDiameterLists()
        {
            foreach (double diameter in BarDatabase.LongitudinalDiameters)
            {
                cmbDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
            foreach (double diameter in BarDatabase.TransverseDiameters)
            {
                cmbStirrupDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
        }

        private void WriteSettingsToUi(ColumnDesignSettings s)
        {
            _loading = true;

            cmbCode.SelectedIndex = s.DetailingCode == DetailingCodeKind.Aci318 ? 1 : 0;
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtNed.Text = Format(s.AxialLoadKn);
            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            cmbDesignLife.SelectedIndex = s.DesignLife == DesignWorkingLife.Years100 ? 1 : 0;
            chkQualityControl.IsChecked = s.SpecialQualityControl;
            txtCover.Text = Format(s.CoverMm);
            txtAggregate.Text = Format(s.AggregateSizeMm);
            txtRatio.Text = Format(s.TargetRatioPercent);
            chkSeismic.IsChecked = s.Seismic;

            chkVerify.IsChecked = s.VerifyCapacity;
            txtMx.Text = Format(s.MomentAboutXKnm);
            txtMy.Text = Format(s.MomentAboutYKnm);
            txtBuckling.Text = Format(s.BucklingFactor);
            txtCreep.Text = Format(s.CreepCoefficient);

            chkAutoDiameter.IsChecked = s.AutoLongitudinalDiameter;
            cmbDiameter.SelectedIndex = IndexOf(BarDatabase.LongitudinalDiameters,
                                                s.ForcedLongitudinalDiameterMm);
            chkAutoCount.IsChecked = s.AutoBarCount;
            txtNx.Text = s.ForcedBarsAlongX.ToString(CultureInfo.CurrentCulture);
            txtNy.Text = s.ForcedBarsAlongY.ToString(CultureInfo.CurrentCulture);
            txtNcirc.Text = s.ForcedCircularBarCount.ToString(CultureInfo.CurrentCulture);

            chkAutoTransverse.IsChecked = s.AutoTransverse;
            cmbStirrupDiameter.SelectedIndex = IndexOf(BarDatabase.TransverseDiameters,
                                                       s.ForcedStirrupDiameterMm);
            txtSpacing.Text = Format(s.ForcedSpacingMm);
            chkCriticalZones.IsChecked = s.UseCriticalZones;
            chkCrossTies.IsChecked = s.AddCrossTies;

            txtFirstOffset.Text = Format(s.FirstStirrupOffsetMm);
            txtBottomOffset.Text = Format(s.BottomOffsetMm);
            chkAutoTop.IsChecked = s.TopExtensionMm < 0;
            txtTopExtension.Text = Format(s.TopExtensionMm < 0 ? 0 : s.TopExtensionMm);

            _loading = false;
        }

        private bool ReadSettingsFromUi(out ColumnDesignSettings settings, out List<string> errors)
        {
            settings = new ColumnDesignSettings();
            errors = new List<string>();

            settings.DetailingCode = cmbCode.SelectedIndex == 1
                ? DetailingCodeKind.Aci318 : DetailingCodeKind.Eurocode2;
            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            settings.AxialLoadKn = ReadDouble(txtNed, "Effort normal", errors);
            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.DesignLife = cmbDesignLife.SelectedIndex == 1
                ? DesignWorkingLife.Years100 : DesignWorkingLife.Years50;
            settings.SpecialQualityControl = chkQualityControl.IsChecked == true;
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);
            settings.AggregateSizeMm = ReadDouble(txtAggregate, "Granulat", errors);
            settings.TargetRatioPercent = ReadDouble(txtRatio, "Taux vise", errors);
            settings.Seismic = chkSeismic.IsChecked == true;

            settings.VerifyCapacity = chkVerify.IsChecked == true;
            settings.MomentAboutXKnm = ReadDouble(txtMx, "Moment autour de X", errors);
            settings.MomentAboutYKnm = ReadDouble(txtMy, "Moment autour de Y", errors);
            settings.BucklingFactor = ReadDouble(txtBuckling, "Coefficient de flambement", errors);
            settings.CreepCoefficient = ReadDouble(txtCreep, "Coefficient de fluage", errors);

            settings.AutoLongitudinalDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedLongitudinalDiameterMm = ValueAt(BarDatabase.LongitudinalDiameters,
                                                            cmbDiameter.SelectedIndex, 16.0);
            settings.AutoBarCount = chkAutoCount.IsChecked == true;
            settings.ForcedBarsAlongX = ReadInt(txtNx, "Barres par face // X", errors);
            settings.ForcedBarsAlongY = ReadInt(txtNy, "Barres par face // Y", errors);
            settings.ForcedCircularBarCount = ReadInt(txtNcirc, "Barres de la section ronde", errors);

            settings.AutoTransverse = chkAutoTransverse.IsChecked == true;
            settings.ForcedStirrupDiameterMm = ValueAt(BarDatabase.TransverseDiameters,
                                                       cmbStirrupDiameter.SelectedIndex, 8.0);
            settings.ForcedSpacingMm = ReadDouble(txtSpacing, "Espacement des cadres", errors);
            settings.UseCriticalZones = chkCriticalZones.IsChecked == true;
            settings.AddCrossTies = chkCrossTies.IsChecked == true;

            settings.FirstStirrupOffsetMm = ReadDouble(txtFirstOffset, "Position du premier cadre", errors);
            settings.BottomOffsetMm = ReadDouble(txtBottomOffset, "Retrait en pied", errors);
            settings.TopExtensionMm = chkAutoTop.IsChecked == true
                ? -1.0
                : ReadDouble(txtTopExtension, "Attentes", errors);

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        // ------------------------------------------------------------------
        // Calcul et actions
        // ------------------------------------------------------------------

        private bool Calculate(bool reportErrors)
        {
            ColumnDesignSettings settings;
            List<string> errors;
            if (!ReadSettingsFromUi(out settings, out errors))
            {
                if (reportErrors)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                                    "Parametres incorrects", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            Settings = settings;
            var module = new ColumnDesignModule();
            Results = DesignPipeline.Run(module, _columns, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new ColumnReportItem(r, QuantityCalculator.Compute(r.Column, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new ColumnRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            var status = new StringBuilder();
            status.AppendFormat("{0} element(s) - {1} dimensionne(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            SteelQuantities total = QuantityCsvReport.Total(Items);
            if (total.TotalMassKg <= 0)
            {
                txtQuantities.Text = "Aucun quantitatif disponible.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("Acier total : {0:0.0} kg", total.TotalMassKg));
            sb.AppendLine(string.Format("  longitudinales {0:0.0} kg  -  cadres {1:0.0} kg" +
                                        (total.CrossTieMassKg > 0 ? "  -  epingles {2:0.0} kg" : ""),
                total.LongitudinalMassKg, total.StirrupMassKg, total.CrossTieMassKg));
            sb.AppendLine(string.Format("Beton : {0:0.000} m3  ->  ratio {1:0} kg/m3",
                total.ConcreteVolumeM3, total.RatioKgPerM3));
            sb.AppendLine(string.Format("Longueur totale d'acier : {0:0.0} m", total.TotalLengthM));
            sb.Append(total.DiameterBreakdown());
            txtQuantities.Text = sb.ToString();
        }

        private void OnCalculateClick(object sender, RoutedEventArgs e)
        {
            Calculate(true);
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            if (!Calculate(true)) return;
            if (Results.Count(r => r.IsValid) == 0)
            {
                MessageBox.Show(this, "Aucun element n'a pu etre dimensionne.", "Generation impossible",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} element(s) ne satisfont pas toutes les verifications.{1}{1}" +
                                  "Generer quand meme les armatures ?", notCompliant, Environment.NewLine),
                    "Verifications", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            DialogResult = true;
            Close();
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0 && !Calculate(true)) return;
            var header = new ReportHeader
            {
                StructuralCode = Settings.DetailingCode == DetailingCodeKind.Aci318
                    ? "ACI 318-19" : "EN 1992-1-1:2004+A1:2014",
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };
            SaveText("note-de-calcul-DanCI.txt", "Fichier texte (*.txt)|*.txt",
                     "Exporter la note de calcul", CalculationReport.Build(header, Items));
        }

        private void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            if (Items.Count == 0 && !Calculate(true)) return;
            SaveText("quantitatif-DanCI.csv", "Fichier CSV (*.csv)|*.csv",
                     "Exporter le quantitatif", QuantityCsvReport.Build(Items));
        }

        private void SaveText(string fileName, string filter, string title, string content)
        {
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = title,
                FileName = fileName,
                DefaultExt = System.IO.Path.GetExtension(fileName),
                Filter = filter
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                // BOM UTF-8 : Excel ouvre alors le fichier avec les accents corrects.
                System.IO.File.WriteAllText(dialog.FileName, content, new UTF8Encoding(true));
                MessageBox.Show(this, "Fichier enregistre :" + Environment.NewLine + dialog.FileName,
                                title, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Enregistrement impossible",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void OnCloseClick(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void OnResultSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            var row = grdResults.SelectedItem as ColumnRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgSection.Source = SectionPreview.Render(null, PreviewPixels);
                return;
            }

            txtNotes.Text = CalculationReport.BuildElement(
                new ColumnReportItem(row.Result, row.Quantities));
            imgSection.Source = SectionPreview.Render(row.Result, PreviewPixels);
        }

        // ------------------------------------------------------------------
        // Utilitaires de saisie
        // ------------------------------------------------------------------

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private static int IndexOf(double[] values, double value)
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (Math.Abs(values[i] - value) < 1e-6) return i;
            }
            return 0;
        }

        private static double ValueAt(double[] values, int index, double fallback)
        {
            return index >= 0 && index < values.Length ? values[index] : fallback;
        }

        private static double ReadDouble(TextBox box, string label, List<string> errors)
        {
            string text = (box.Text ?? string.Empty).Trim().Replace(',', '.');
            double value;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            errors.Add(label + " : valeur numerique attendue.");
            return 0.0;
        }

        private static int ReadInt(TextBox box, string label, List<string> errors)
        {
            int value;
            if (int.TryParse((box.Text ?? string.Empty).Trim(), NumberStyles.Integer,
                             CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            errors.Add(label + " : nombre entier attendu.");
            return 0;
        }
    }
}
