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
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des poutres. Comme pour les poteaux, elle n'orchestre pas
    /// le calcul : elle appelle <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class BeamDesignWindow : Window
    {
        private const int PreviewWidth = 660;
        private const int PreviewHeight = 760;

        private readonly List<BeamData> _beams;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public BeamDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<BeamDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<BeamReportItem> Items { get; private set; }

        public BeamDesignWindow(List<BeamData> beams, BeamDesignSettings settings)
        {
            InitializeComponent();
            _beams = beams;
            Settings = settings;
            Results = new List<BeamDesignResult>();
            Items = new List<BeamReportItem>();

            FillDiameterLists();
            WriteSettingsToUi(settings);
            Calculate(false);
        }

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

        private void WriteSettingsToUi(BeamDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtRedistribution.Text = Format(s.RedistributionRatio);

            txtMspan.Text = Format(s.SpanMomentKnm);
            txtMleft.Text = Format(s.LeftSupportMomentKnm);
            txtMright.Text = Format(s.RightSupportMomentKnm);
            txtVleft.Text = Format(s.LeftShearKn);
            txtVright.Text = Format(s.RightShearKn);
            cmbSpanKind.SelectedIndex = (int)s.SpanKind;

            chkTSection.IsChecked = s.TreatAsTSection;
            txtFlangeWidth.Text = Format(s.FlangeWidthMm);
            txtFlangeThickness.Text = Format(s.FlangeThicknessMm);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            txtCover.Text = Format(s.CoverMm);
            txtAggregate.Text = Format(s.AggregateSizeMm);

            chkAutoDiameter.IsChecked = s.AutoLongitudinalDiameter;
            cmbDiameter.SelectedIndex = IndexOf(BarDatabase.LongitudinalDiameters,
                                                s.ForcedLongitudinalDiameterMm);
            chkAutoStirrup.IsChecked = s.AutoStirrupDiameter;
            cmbStirrupDiameter.SelectedIndex = IndexOf(BarDatabase.TransverseDiameters,
                                                       s.ForcedStirrupDiameterMm);
            txtLegs.Text = s.StirrupLegs.ToString(CultureInfo.CurrentCulture);
            txtMaxLayers.Text = s.MaxLayers.ToString(CultureInfo.CurrentCulture);
        }

        private bool ReadSettingsFromUi(out BeamDesignSettings settings, out List<string> errors)
        {
            settings = new BeamDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            settings.RedistributionRatio = ReadDouble(txtRedistribution, "Redistribution", errors);

            settings.SpanMomentKnm = ReadDouble(txtMspan, "Moment en travee", errors);
            settings.LeftSupportMomentKnm = ReadDouble(txtMleft, "Moment appui gauche", errors);
            settings.RightSupportMomentKnm = ReadDouble(txtMright, "Moment appui droit", errors);
            settings.LeftShearKn = ReadDouble(txtVleft, "Effort tranchant appui gauche", errors);
            settings.RightShearKn = ReadDouble(txtVright, "Effort tranchant appui droit", errors);
            settings.SpanKind = (BeamSpanKind)Math.Max(cmbSpanKind.SelectedIndex, 0);

            settings.TreatAsTSection = chkTSection.IsChecked == true;
            settings.FlangeWidthMm = ReadDouble(txtFlangeWidth, "Largeur de table", errors);
            settings.FlangeThicknessMm = ReadDouble(txtFlangeThickness, "Epaisseur de dalle", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);
            settings.AggregateSizeMm = ReadDouble(txtAggregate, "Granulat", errors);

            settings.AutoLongitudinalDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedLongitudinalDiameterMm = ValueAt(BarDatabase.LongitudinalDiameters,
                                                            cmbDiameter.SelectedIndex, 16.0);
            settings.AutoStirrupDiameter = chkAutoStirrup.IsChecked == true;
            settings.ForcedStirrupDiameterMm = ValueAt(BarDatabase.TransverseDiameters,
                                                       cmbStirrupDiameter.SelectedIndex, 8.0);
            settings.StirrupLegs = ReadInt(txtLegs, "Brins par cadre", errors);
            settings.MaxLayers = ReadInt(txtMaxLayers, "Lits maximum", errors);

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            BeamDesignSettings settings;
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
            Results = DesignPipeline.Run(new BeamDesignModule(), _beams, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new BeamReportItem(r, QuantityCalculator.Compute(r.Beam, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new BeamRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            var status = new StringBuilder();
            status.AppendFormat("{0} poutre(s) - {1} dimensionnee(s)", Results.Count,
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
            foreach (BeamReportItem item in Items)
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
            sb.AppendLine(string.Format("  longitudinales {0:0.0} kg  -  cadres {1:0.0} kg",
                total.LongitudinalMassKg, total.StirrupMassKg));
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
                MessageBox.Show(this, "Aucune poutre n'a pu etre dimensionnee.",
                                "Generation impossible", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} poutre(s) ne satisfont pas toutes les verifications.{1}{1}" +
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
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-poutres-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildBeams(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as BeamRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgBeam.Source = BeamPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildBeamElement(
                new BeamReportItem(row.Result, row.Quantities));
            imgBeam.Source = BeamPreview.Render(row.Result, PreviewWidth, PreviewHeight);
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
