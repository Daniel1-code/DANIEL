using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using ArmaturesPoteaux.Core;
using ArmaturesPoteaux.Design;

namespace ArmaturesPoteaux.UI
{
    /// <summary>
    /// Fenetre unique du plugin : parametres a gauche, tableau et note de calcul au centre,
    /// coupe du poteau et quantitatif a droite. Le calcul est relance a chaque clic sur
    /// "Calculer" et automatiquement avant la generation.
    /// </summary>
    public partial class MainWindow : Window
    {
        private const int PreviewPixels = 620;

        private readonly List<ColumnGeometry> _columns;
        private List<Preset> _presets = new List<Preset>();
        private bool _loading;

        /// <summary>Parametres valides au moment de la generation.</summary>
        public DesignInput Input { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<DesignResult> Results { get; private set; }

        public MainWindow(List<ColumnGeometry> columns, DesignInput input)
        {
            InitializeComponent();
            _columns = columns;
            Input = input;
            Results = new List<DesignResult>();

            FillDiameterLists();
            ReloadPresets();
            WriteInputToUi(input);

            Calculate(false);
        }

        // ------------------------------------------------------------------
        // Configurations enregistrees
        // ------------------------------------------------------------------

        private void ReloadPresets()
        {
            _loading = true;
            string previous = cmbPreset.Text;

            _presets = PresetStore.BuiltIn();
            _presets.AddRange(PresetStore.LoadUserPresets());

            cmbPreset.Items.Clear();
            foreach (Preset preset in _presets) cmbPreset.Items.Add(preset.Name);
            cmbPreset.Text = previous;

            _loading = false;
        }

        private void OnPresetSelected(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var name = cmbPreset.SelectedItem as string;
            if (name == null) return;

            Preset preset = _presets.FirstOrDefault(p => p.Name == name);
            if (preset == null || preset.Input == null) return;

            WriteInputToUi(preset.Input);
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

            DesignInput input;
            List<string> errors;
            if (!ReadInputFromUi(out input, out errors))
            {
                MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                                "Parametres incorrects", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string error;
            if (!PresetStore.AddOrReplace(name, input, out error))
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

            if (PresetStore.BuiltIn().Any(p => p.Name == name))
            {
                MessageBox.Show(this, "Les configurations livrees avec le plugin ne peuvent pas " +
                                      "etre supprimees.", "Configuration",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string error;
            if (!PresetStore.Remove(name, out error))
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
        // Transfert interface <-> parametres
        // ------------------------------------------------------------------

        private void FillDiameterLists()
        {
            foreach (double d in DesignInput.LongitudinalDiameters)
            {
                cmbDiameter.Items.Add(string.Format("HA{0:0}", d));
            }
            foreach (double d in DesignInput.TransverseDiameters)
            {
                cmbStirrupDiameter.Items.Add(string.Format("HA{0:0}", d));
            }
        }

        private void WriteInputToUi(DesignInput input)
        {
            _loading = true;

            cmbCode.SelectedIndex = input.Code == DesignCodeKind.Aci318 ? 1 : 0;
            txtFck.Text = Format(input.ConcreteStrengthMPa);
            txtFyk.Text = Format(input.SteelStrengthMPa);
            txtNed.Text = Format(input.AxialLoadKn);
            txtCover.Text = Format(input.CoverMm);
            txtAggregate.Text = Format(input.AggregateSizeMm);
            txtRatio.Text = Format(input.TargetRatioPercent);
            chkSeismic.IsChecked = input.Seismic;

            chkVerify.IsChecked = input.VerifyCapacity;
            txtMx.Text = Format(input.MomentAboutXKnm);
            txtMy.Text = Format(input.MomentAboutYKnm);
            txtBuckling.Text = Format(input.BucklingFactor);
            txtCreep.Text = Format(input.CreepCoefficient);

            chkAutoDiameter.IsChecked = input.AutoLongitudinalDiameter;
            cmbDiameter.SelectedIndex = IndexOf(DesignInput.LongitudinalDiameters,
                                                input.ForcedLongitudinalDiameterMm);
            chkAutoCount.IsChecked = input.AutoBarCount;
            txtNx.Text = input.ForcedBarsAlongX.ToString(CultureInfo.CurrentCulture);
            txtNy.Text = input.ForcedBarsAlongY.ToString(CultureInfo.CurrentCulture);
            txtNcirc.Text = input.ForcedCircularBarCount.ToString(CultureInfo.CurrentCulture);

            chkAutoTransverse.IsChecked = input.AutoTransverse;
            cmbStirrupDiameter.SelectedIndex = IndexOf(DesignInput.TransverseDiameters,
                                                       input.ForcedStirrupDiameterMm);
            txtSpacing.Text = Format(input.ForcedSpacingMm);
            chkCriticalZones.IsChecked = input.UseCriticalZones;
            chkCrossTies.IsChecked = input.AddCrossTies;

            txtFirstOffset.Text = Format(input.FirstStirrupOffsetMm);
            txtBottomOffset.Text = Format(input.BottomOffsetMm);
            chkAutoTop.IsChecked = input.TopExtensionMm < 0;
            txtTopExtension.Text = Format(input.TopExtensionMm < 0 ? 0 : input.TopExtensionMm);

            _loading = false;
        }

        private bool ReadInputFromUi(out DesignInput input, out List<string> errors)
        {
            input = new DesignInput();
            errors = new List<string>();

            input.Code = cmbCode.SelectedIndex == 1 ? DesignCodeKind.Aci318 : DesignCodeKind.Eurocode2;
            input.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            input.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            input.AxialLoadKn = ReadDouble(txtNed, "Effort normal", errors);
            input.CoverMm = ReadDouble(txtCover, "Enrobage", errors);
            input.AggregateSizeMm = ReadDouble(txtAggregate, "Granulat", errors);
            input.TargetRatioPercent = ReadDouble(txtRatio, "Taux vise", errors);
            input.Seismic = chkSeismic.IsChecked == true;

            input.VerifyCapacity = chkVerify.IsChecked == true;
            input.MomentAboutXKnm = ReadDouble(txtMx, "Moment autour de X", errors);
            input.MomentAboutYKnm = ReadDouble(txtMy, "Moment autour de Y", errors);
            input.BucklingFactor = ReadDouble(txtBuckling, "Coefficient de flambement", errors);
            input.CreepCoefficient = ReadDouble(txtCreep, "Coefficient de fluage", errors);

            input.AutoLongitudinalDiameter = chkAutoDiameter.IsChecked == true;
            input.ForcedLongitudinalDiameterMm = ValueAt(DesignInput.LongitudinalDiameters,
                                                         cmbDiameter.SelectedIndex, 16.0);
            input.AutoBarCount = chkAutoCount.IsChecked == true;
            input.ForcedBarsAlongX = ReadInt(txtNx, "Barres par face // X", errors);
            input.ForcedBarsAlongY = ReadInt(txtNy, "Barres par face // Y", errors);
            input.ForcedCircularBarCount = ReadInt(txtNcirc, "Barres de la section ronde", errors);

            input.AutoTransverse = chkAutoTransverse.IsChecked == true;
            input.ForcedStirrupDiameterMm = ValueAt(DesignInput.TransverseDiameters,
                                                    cmbStirrupDiameter.SelectedIndex, 8.0);
            input.ForcedSpacingMm = ReadDouble(txtSpacing, "Espacement des cadres", errors);
            input.UseCriticalZones = chkCriticalZones.IsChecked == true;
            input.AddCrossTies = chkCrossTies.IsChecked == true;

            input.FirstStirrupOffsetMm = ReadDouble(txtFirstOffset, "Position du premier cadre", errors);
            input.BottomOffsetMm = ReadDouble(txtBottomOffset, "Retrait en pied", errors);
            input.TopExtensionMm = chkAutoTop.IsChecked == true
                ? -1.0
                : ReadDouble(txtTopExtension, "Attentes", errors);

            errors.AddRange(input.Validate());
            return errors.Count == 0;
        }

        // ------------------------------------------------------------------
        // Calcul et actions
        // ------------------------------------------------------------------

        private bool Calculate(bool reportErrors)
        {
            DesignInput input;
            List<string> errors;
            if (!ReadInputFromUi(out input, out errors))
            {
                if (reportErrors)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                                    "Parametres incorrects", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                return false;
            }

            Input = input;
            IDesignCode code = DesignCodeFactory.Create(input.Code);
            var designer = new ColumnRebarDesigner(code, input);

            Results = _columns.Select(designer.Design).ToList();
            grdResults.ItemsSource = Results.Select(r => new ColumnRow(r)).ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notResisting = Results.Count(r => r.Check != null && r.Check.Performed && !r.Check.Passes);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            var status = new StringBuilder();
            status.AppendFormat("{0} poteau(x) - {1} dimensionne(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notResisting > 0) status.AppendFormat(", {0} ne resiste(nt) pas", notResisting);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status.ToString() + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            SteelQuantities total = QuantityReport.Total(Results);
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
                MessageBox.Show(this, "Aucun poteau n'a pu etre dimensionne.", "Generation impossible",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int notResisting = Results.Count(r => r.Check != null && r.Check.Performed && !r.Check.Passes);
            if (notResisting > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} poteau(x) ne resistent pas aux efforts saisis.{1}{1}" +
                                  "Generer quand meme les armatures ?", notResisting, Environment.NewLine),
                    "Verification de resistance", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                if (answer != MessageBoxResult.Yes) return;
            }

            DialogResult = true;
            Close();
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (Results.Count == 0 && !Calculate(true)) return;
            SaveText("note-de-calcul-armatures.txt", "Fichier texte (*.txt)|*.txt",
                     "Exporter la note de calcul", BuildFullReport());
        }

        private void OnExportCsvClick(object sender, RoutedEventArgs e)
        {
            if (Results.Count == 0 && !Calculate(true)) return;
            SaveText("quantitatif-armatures.csv", "Fichier CSV (*.csv)|*.csv",
                     "Exporter le quantitatif", QuantityReport.BuildCsv(Results));
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
            var row = grdResults.SelectedItem as ColumnRow;
            txtNotes.Text = row != null ? row.Result.BuildReport() : string.Empty;
            imgSection.Source = row != null
                ? SectionPreview.Render(row.Result.Geometry, row.Result, PreviewPixels)
                : SectionPreview.Render(null, null, PreviewPixels);
        }

        private string BuildFullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NOTE DE CALCUL - ARMATURES DE POTEAUX");
            sb.AppendLine("Genere le " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture));
            sb.AppendLine(string.Format(
                "Beton f_ck = {0:0} MPa - Acier f_yk = {1:0} MPa - Enrobage = {2:0} mm",
                Input.ConcreteStrengthMPa, Input.SteelStrengthMPa, Input.CoverMm));
            if (Input.VerifyCapacity)
            {
                sb.AppendLine(string.Format(
                    "Verification N-M activee - NEd = {0:0} kN, Mx = {1:0.0} kN.m, My = {2:0.0} kN.m, " +
                    "l0 = {3:0.00} H, phi_ef = {4:0.0}",
                    Input.AxialLoadKn, Input.MomentAboutXKnm, Input.MomentAboutYKnm,
                    Input.BucklingFactor, Input.CreepCoefficient));
            }
            sb.AppendLine();
            foreach (DesignResult result in Results) sb.Append(result.BuildReport());

            SteelQuantities total = QuantityReport.Total(Results);
            if (total.TotalMassKg > 0)
            {
                sb.AppendLine("=== TOTAL ===");
                sb.AppendLine(string.Format("Acier : {0:0.0} kg pour {1:0.000} m3 de beton, soit {2:0} kg/m3",
                    total.TotalMassKg, total.ConcreteVolumeM3, total.RatioKgPerM3));
                sb.AppendLine("Repartition : " + total.DiameterBreakdown());
            }
            return sb.ToString();
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
