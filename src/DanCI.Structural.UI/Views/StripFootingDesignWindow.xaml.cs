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
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des semelles filantes. Elle n'orchestre pas le calcul :
    /// elle appelle <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class StripFootingDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 820;

        private static readonly double[] StarterDiameters = { 8, 10, 12, 14, 16, 20 };

        private readonly List<StripFootingData> _footings;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public StripFootingDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<StripFootingDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<StripFootingReportItem> Items { get; private set; }

        public StripFootingDesignWindow(List<StripFootingData> footings,
                                        StripFootingDesignSettings settings)
        {
            InitializeComponent();
            _footings = footings;
            Settings = settings;
            Results = new List<StripFootingDesignResult>();
            Items = new List<StripFootingReportItem>();

            FillDiameterLists();
            WriteSettingsToUi(settings);
            Calculate(false);
        }

        private void FillDiameterLists()
        {
            foreach (double diameter in MeshOptimizer.Diameters)
            {
                cmbDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
            foreach (double diameter in StarterDiameters)
            {
                cmbStarterDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
        }

        private void WriteSettingsToUi(StripFootingDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtUnitWeight.Text = Format(s.ConcreteUnitWeightKnM3);

            txtBearing.Text = Format(s.AllowableBearingPressureKpa);
            txtFriction.Text = Format(s.InterfaceFrictionAngleDeg);
            txtAdhesion.Text = Format(s.InterfaceAdhesionKpa);
            chkSelfWeight.IsChecked = s.IncludeSelfWeight;

            txtAxial.Text = Format(s.AxialLoadKnPerM);
            txtMoment.Text = Format(s.MomentKnmPerM);
            txtHorizontal.Text = Format(s.HorizontalLoadKnPerM);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            chkAgainstSoil.IsChecked = s.CastDirectlyAgainstSoil;
            txtCover.Text = Format(s.CoverMm);

            chkAutoDiameter.IsChecked = s.AutoMeshDiameter;
            cmbDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters, s.ForcedMeshDiameterMm);
            chkTopMesh.IsChecked = s.TopMesh;
            chkStarters.IsChecked = s.Starters;
            txtStarterSpacing.Text = Format(s.StarterSpacingMm);
            cmbStarterDiameter.SelectedIndex = IndexOf(StarterDiameters, s.StarterDiameterMm);
        }

        private bool ReadSettingsFromUi(out StripFootingDesignSettings settings,
                                        out List<string> errors)
        {
            settings = new StripFootingDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            settings.ConcreteUnitWeightKnM3 = ReadDouble(txtUnitWeight, "Poids du beton", errors);

            settings.AllowableBearingPressureKpa = ReadDouble(txtBearing,
                "Contrainte admissible du sol", errors);
            settings.InterfaceFrictionAngleDeg = ReadDouble(txtFriction,
                "Angle de frottement d'interface", errors);
            settings.InterfaceAdhesionKpa = ReadDouble(txtAdhesion, "Adherence d'interface", errors);
            settings.IncludeSelfWeight = chkSelfWeight.IsChecked == true;

            settings.AxialLoadKnPerM = ReadDouble(txtAxial, "Charge verticale", errors);
            settings.MomentKnmPerM = ReadDouble(txtMoment, "Moment transversal", errors);
            settings.HorizontalLoadKnPerM = ReadDouble(txtHorizontal,
                "Effort horizontal transversal", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CastDirectlyAgainstSoil = chkAgainstSoil.IsChecked == true;
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.AutoMeshDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedMeshDiameterMm = ValueAt(MeshOptimizer.Diameters,
                                                    cmbDiameter.SelectedIndex, 12.0);
            settings.TopMesh = chkTopMesh.IsChecked == true;
            settings.Starters = chkStarters.IsChecked == true;
            settings.StarterSpacingMm = ReadDouble(txtStarterSpacing,
                "Espacement des attentes", errors);
            settings.StarterDiameterMm = ValueAt(StarterDiameters,
                                                 cmbStarterDiameter.SelectedIndex, 10.0);

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            StripFootingDesignSettings settings;
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
            Results = DesignPipeline.Run(new StripFootingDesignModule(), _footings, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new StripFootingReportItem(r,
                    QuantityCalculator.Compute(r.Footing, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new StripFootingRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            int hooked = Results.Count(
                r => r.Reinforcement != null && r.Reinforcement.TransverseNeedsHook);

            var status = new StringBuilder();
            status.AppendFormat("{0} semelle(s) - {1} dimensionnee(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (hooked > 0) status.AppendFormat(", {0} avec crochets d'extremite", hooked);
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            var total = new SteelQuantities();
            foreach (StripFootingReportItem item in Items)
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

        private void OnCalculateClick(object sender, RoutedEventArgs e)
        {
            Calculate(true);
        }

        private void OnGenerateClick(object sender, RoutedEventArgs e)
        {
            if (!Calculate(true)) return;
            if (Results.Count(r => r.IsValid) == 0)
            {
                MessageBox.Show(this, "Aucune semelle n'a pu etre dimensionnee.",
                                "Generation impossible", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} semelle(s) ne satisfont pas toutes les verifications.{1}{1}" +
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
                StructuralCode = "EN 1992-1-1:2004+A1:2014 et EN 1997-1:2004",
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-semelles-filantes-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildStripFootings(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as StripFootingRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgFooting.Source = StripFootingPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildStripFootingElement(
                new StripFootingReportItem(row.Result, row.Quantities));
            imgFooting.Source = StripFootingPreview.Render(row.Result, PreviewWidth, PreviewHeight);
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
