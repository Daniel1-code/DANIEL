using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Documentation.Reports;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des dalles. Elle n'orchestre pas le calcul : elle appelle
    /// <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class SlabDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 800;

        private readonly List<SlabData> _slabs;
        private bool _loaded;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public SlabDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<SlabDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<SlabReportItem> Items { get; private set; }

        public SlabDesignWindow(List<SlabData> slabs, SlabDesignSettings settings)
        {
            InitializeComponent();
            _slabs = slabs;
            Settings = settings;
            Results = new List<SlabDesignResult>();
            Items = new List<SlabReportItem>();

            FillDiameterList();
            WriteSettingsToUi(settings);
            _loaded = true;
            UpdateActionPanels();
            Calculate(false);
        }

        private void FillDiameterList()
        {
            foreach (double diameter in MeshOptimizer.Diameters)
            {
                cmbDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
        }

        private void WriteSettingsToUi(SlabDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);

            // Les dalles lues partagent en general la meme condition d'appui ; la premiere
            // sert de valeur affichee, et le choix s'applique ensuite a toutes.
            cmbSpanKind.SelectedIndex = _slabs.Count > 0 ? (int)_slabs[0].SpanKind : 0;

            cmbMomentSource.SelectedIndex = s.MomentSource == SlabMomentSource.Entered ? 1 : 0;
            txtPermanent.Text = Format(s.PermanentLoadKnM2);
            txtVariable.Text = Format(s.VariableLoadKnM2);
            cmbCategory.SelectedIndex = (int)s.Category;
            txtUnitWeight.Text = Format(s.ConcreteUnitWeightKnM3);
            chkSelfWeight.IsChecked = s.IncludeSelfWeight;

            txtSpanMoment.Text = Format(s.SpanMomentKnmPerM);
            txtSupportMoment.Text = Format(s.SupportMomentKnmPerM);
            txtShear.Text = Format(s.ShearKnPerM);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            txtCover.Text = Format(s.CoverMm);

            chkPartitions.IsChecked = s.SupportsPartitions;
            txtCrackWidth.Text = Format(s.CrackWidthLimitMm);

            chkAutoDiameter.IsChecked = s.AutoMeshDiameter;
            cmbDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters, s.ForcedMeshDiameterMm);
            chkTopReinforcement.IsChecked = s.TopReinforcement;
        }

        private bool ReadSettingsFromUi(out SlabDesignSettings settings, out List<string> errors)
        {
            settings = new SlabDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);

            settings.MomentSource = cmbMomentSource.SelectedIndex == 1
                ? SlabMomentSource.Entered : SlabMomentSource.FromLoads;
            settings.PermanentLoadKnM2 = ReadDouble(txtPermanent, "Charge permanente", errors);
            settings.VariableLoadKnM2 = ReadDouble(txtVariable, "Charge d'exploitation", errors);
            settings.Category = (UseCategory)Math.Max(cmbCategory.SelectedIndex, 0);
            settings.ConcreteUnitWeightKnM3 = ReadDouble(txtUnitWeight, "Poids du beton", errors);
            settings.IncludeSelfWeight = chkSelfWeight.IsChecked == true;

            settings.SpanMomentKnmPerM = ReadDouble(txtSpanMoment, "Moment en travee", errors);
            settings.SupportMomentKnmPerM = ReadDouble(txtSupportMoment, "Moment sur appui", errors);
            settings.ShearKnPerM = ReadDouble(txtShear, "Effort tranchant", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.SupportsPartitions = chkPartitions.IsChecked == true;
            settings.CrackWidthLimitMm = ReadDouble(txtCrackWidth, "Ouverture de fissure", errors);

            settings.AutoMeshDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedMeshDiameterMm = ValueAt(MeshOptimizer.Diameters,
                                                    cmbDiameter.SelectedIndex, 10.0);
            settings.TopReinforcement = chkTopReinforcement.IsChecked == true;

            // La condition d'appui appartient a l'element, pas aux reglages : elle est
            // appliquee a toutes les dalles selectionnees.
            var spanKind = (SlabSpanKind)Math.Max(cmbSpanKind.SelectedIndex, 0);
            foreach (SlabData slab in _slabs) slab.SpanKind = spanKind;

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            SlabDesignSettings settings;
            List<string> errors;
            if (!ReadSettingsFromUi(out settings, out errors))
            {
                if (reportErrors)
                {
                    MessageBox.Show(this, string.Join(Environment.NewLine, errors),
                                    "Parametres incorrects", MessageBoxButton.OK,
                                    MessageBoxImage.Warning);
                }
                return false;
            }

            Settings = settings;
            Results = DesignPipeline.Run(new SlabDesignModule(), _slabs, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new SlabReportItem(r, QuantityCalculator.Compute(r.Slab, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new SlabRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            int deflectionDriven = Results.Count(
                r => r.Deflection != null && r.Deflection.Utilization > 0.85);

            var status = new StringBuilder();
            status.AppendFormat("{0} dalle(s) - {1} dimensionnee(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (deflectionDriven > 0)
            {
                status.AppendFormat(", {0} pilotee(s) par la fleche", deflectionDriven);
            }
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            var total = new SteelQuantities();
            foreach (SlabReportItem item in Items)
            {
                if (item.Quantities != null) total.Merge(item.Quantities);
            }

            if (total.TotalMassKg <= 0)
            {
                txtQuantities.Text = "Aucun quantitatif disponible.";
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("Acier total : {0:0.0} kg", total.TotalMassKg));
            sb.AppendLine(string.Format("Beton : {0:0.000} m3  ->  ratio {1:0} kg/m3",
                total.ConcreteVolumeM3, total.RatioKgPerM3));
            sb.Append(total.DiameterBreakdown());
            txtQuantities.Text = sb.ToString();
        }

        private void UpdateActionPanels()
        {
            if (!_loaded) return;
            bool fromLoads = cmbMomentSource.SelectedIndex != 1;
            grdLoads.IsEnabled = fromLoads;
            grdMoments.IsEnabled = !fromLoads;
        }

        private void OnMomentSourceChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateActionPanels();
            if (_loaded) Calculate(false);
        }

        private void OnSpanKindChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loaded) Calculate(false);
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
                MessageBox.Show(this, "Aucune dalle n'a pu etre dimensionnee.",
                                "Generation impossible", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} dalle(s) ne satisfont pas toutes les verifications.{1}{1}" +
                                  "Generer quand meme les armatures ?", notCompliant,
                                  Environment.NewLine),
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
                StructuralCode = "EN 1992-1-1:2004+A1:2014 et EN 1990:2002",
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-dalles-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildSlabs(header, Items), new UTF8Encoding(true));
                MessageBox.Show(this, "Fichier enregistre :" + Environment.NewLine + dialog.FileName,
                                "Export", MessageBoxButton.OK, MessageBoxImage.Information);
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
            var row = grdResults.SelectedItem as SlabRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgSlab.Source = SlabPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildSlabElement(
                new SlabReportItem(row.Result, row.Quantities));
            imgSlab.Source = SlabPreview.Render(row.Result, PreviewWidth, PreviewHeight);
        }

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
    }
}
