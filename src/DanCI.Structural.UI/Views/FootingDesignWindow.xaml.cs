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
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des semelles isolees. Comme les autres, elle n'orchestre
    /// pas le calcul : elle appelle <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class FootingDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 800;

        private static readonly double[] StarterDiameters = { 10, 12, 14, 16, 20, 25, 32 };

        private readonly List<FootingData> _footings;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public FootingDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<FootingDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<FootingReportItem> Items { get; private set; }

        public FootingDesignWindow(List<FootingData> footings, FootingDesignSettings settings)
        {
            InitializeComponent();
            _footings = footings;
            Settings = settings;
            Results = new List<FootingDesignResult>();
            Items = new List<FootingReportItem>();

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

        private void WriteSettingsToUi(FootingDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtUnitWeight.Text = Format(s.ConcreteUnitWeightKnM3);

            txtBearing.Text = Format(s.AllowableBearingPressureKpa);
            txtFriction.Text = Format(s.InterfaceFrictionAngleDeg);
            txtAdhesion.Text = Format(s.InterfaceAdhesionKpa);
            chkSelfWeight.IsChecked = s.IncludeSelfWeight;

            txtAxial.Text = Format(s.AxialLoadKn);
            txtMx.Text = Format(s.MomentAboutXKnm);
            txtMy.Text = Format(s.MomentAboutYKnm);
            txtVx.Text = Format(s.ShearXKn);
            txtVy.Text = Format(s.ShearYKn);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            chkAgainstSoil.IsChecked = s.CastDirectlyAgainstSoil;
            txtCover.Text = Format(s.CoverMm);

            chkAutoDiameter.IsChecked = s.AutoMeshDiameter;
            cmbDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters, s.ForcedMeshDiameterMm);
            chkTopMesh.IsChecked = s.TopMesh;
            txtStarters.Text = s.StarterBarCount.ToString(CultureInfo.CurrentCulture);
            cmbStarterDiameter.SelectedIndex = IndexOf(StarterDiameters, s.StarterBarDiameterMm);
        }

        private bool ReadSettingsFromUi(out FootingDesignSettings settings, out List<string> errors)
        {
            settings = new FootingDesignSettings();
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

            settings.AxialLoadKn = ReadDouble(txtAxial, "Charge verticale", errors);
            settings.MomentAboutXKnm = ReadDouble(txtMx, "Moment autour de X", errors);
            settings.MomentAboutYKnm = ReadDouble(txtMy, "Moment autour de Y", errors);
            settings.ShearXKn = ReadDouble(txtVx, "Effort tranchant suivant X", errors);
            settings.ShearYKn = ReadDouble(txtVy, "Effort tranchant suivant Y", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CastDirectlyAgainstSoil = chkAgainstSoil.IsChecked == true;
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.AutoMeshDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedMeshDiameterMm = ValueAt(MeshOptimizer.Diameters,
                                                    cmbDiameter.SelectedIndex, 12.0);
            settings.TopMesh = chkTopMesh.IsChecked == true;
            settings.StarterBarCount = ReadInt(txtStarters, "Nombre d'attentes", errors);
            settings.StarterBarDiameterMm = ValueAt(StarterDiameters,
                                                    cmbStarterDiameter.SelectedIndex, 16.0);

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            FootingDesignSettings settings;
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
            Results = DesignPipeline.Run(new FootingDesignModule(), _footings, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new FootingReportItem(r, QuantityCalculator.Compute(r.Footing, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new FootingRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            var status = new StringBuilder();
            status.AppendFormat("{0} semelle(s) - {1} dimensionnee(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            var total = new SteelQuantities();
            foreach (FootingReportItem item in Items)
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
                                "Generation impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                FileName = "note-de-calcul-semelles-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildFootings(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as FootingRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgFooting.Source = FootingPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildFootingElement(
                new FootingReportItem(row.Result, row.Quantities));
            imgFooting.Source = FootingPreview.Render(row.Result, PreviewWidth, PreviewHeight);
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
