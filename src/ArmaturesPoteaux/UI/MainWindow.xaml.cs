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
    /// Fenetre unique du plugin : parametres a gauche, resultats et note de calcul a droite.
    /// Le calcul est relance a chaque clic sur "Calculer" et automatiquement avant la generation.
    /// </summary>
    public partial class MainWindow : Window
    {
        private readonly List<ColumnGeometry> _columns;

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
            WriteInputToUi(input);

            txtStatus.Text = string.Format("{0} poteau(x) selectionne(s).", columns.Count);
            Calculate(false);
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
            cmbCode.SelectedIndex = input.Code == DesignCodeKind.Aci318 ? 1 : 0;
            txtFck.Text = Format(input.ConcreteStrengthMPa);
            txtFyk.Text = Format(input.SteelStrengthMPa);
            txtNed.Text = Format(input.AxialLoadKn);
            txtCover.Text = Format(input.CoverMm);
            txtAggregate.Text = Format(input.AggregateSizeMm);
            txtRatio.Text = Format(input.TargetRatioPercent);
            chkSeismic.IsChecked = input.Seismic;

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

            int failed = Results.Count(r => !r.IsValid);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            txtStatus.Text = string.Format(
                "{0} poteau(x) - {1} dimensionne(s), {2} a verifier, {3} en echec.",
                Results.Count, Results.Count(r => r.IsValid), warned, failed);
            return true;
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
            DialogResult = true;
            Close();
        }

        private void OnExportClick(object sender, RoutedEventArgs e)
        {
            if (Results.Count == 0 && !Calculate(true)) return;

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-armatures.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName, BuildFullReport(), Encoding.UTF8);
                MessageBox.Show(this, "Note de calcul enregistree.", "Export",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "Export impossible",
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
        }

        private string BuildFullReport()
        {
            var sb = new StringBuilder();
            sb.AppendLine("NOTE DE CALCUL - ARMATURES DE POTEAUX");
            sb.AppendLine("Genere le " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", CultureInfo.CurrentCulture));
            sb.AppendLine(string.Format(
                "Beton f_ck = {0:0} MPa - Acier f_yk = {1:0} MPa - Enrobage = {2:0} mm",
                Input.ConcreteStrengthMPa, Input.SteelStrengthMPa, Input.CoverMm));
            sb.AppendLine();
            foreach (DesignResult result in Results) sb.Append(result.BuildReport());
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
