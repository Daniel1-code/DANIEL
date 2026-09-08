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
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Engine.Pipeline;
using DanCI.Structural.Eurocodes.EC8;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des longrines. Elle n'orchestre pas le calcul : elle
    /// appelle <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class GradeBeamDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 820;

        private readonly List<GradeBeamData> _beams;
        private bool _loaded;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public GradeBeamDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<GradeBeamDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<GradeBeamReportItem> Items { get; private set; }

        public GradeBeamDesignWindow(List<GradeBeamData> beams, GradeBeamDesignSettings settings)
        {
            InitializeComponent();
            _beams = beams;
            Settings = settings;
            Results = new List<GradeBeamDesignResult>();
            Items = new List<GradeBeamReportItem>();

            FillDiameterLists();
            WriteSettingsToUi(settings);
            _loaded = true;
            UpdateTiePanels();
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

        private void WriteSettingsToUi(GradeBeamDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtUnitWeight.Text = Format(s.ConcreteUnitWeightKnM3);

            cmbSpanKind.SelectedIndex = _beams.Count > 0 ? (int)_beams[0].SpanKind : 0;
            cmbBedding.SelectedIndex = s.Bedding == GradeBeamBedding.SoilBearing ? 1 : 0;
            txtWallLoad.Text = Format(s.WallLoadKnPerM);
            txtBearing.Text = Format(s.AllowableBearingPressureKpa);
            chkSelfWeight.IsChecked = s.IncludeSelfWeight;

            chkSeismic.IsChecked = s.SeismicDesign;
            cmbGround.SelectedIndex = (int)s.Ground;
            txtAlpha.Text = Format(s.GroundAccelerationRatio);
            txtSoilFactor.Text = Format(s.SoilFactor);
            txtMeanColumn.Text = Format(s.MeanColumnAxialLoadKn);
            txtStoreys.Text = s.StoreyCount.ToString(CultureInfo.CurrentCulture);
            txtManualTie.Text = Format(s.ManualTieForceKn);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            chkAgainstSoil.IsChecked = s.CastDirectlyAgainstSoil;
            txtCover.Text = Format(s.CoverMm);

            chkAutoDiameter.IsChecked = s.AutoLongitudinalDiameter;
            cmbDiameter.SelectedIndex = IndexOf(BarDatabase.LongitudinalDiameters,
                                                s.ForcedLongitudinalDiameterMm);
            chkAutoStirrup.IsChecked = s.AutoStirrupDiameter;
            cmbStirrupDiameter.SelectedIndex = IndexOf(BarDatabase.TransverseDiameters,
                                                      s.ForcedStirrupDiameterMm);
            txtLegs.Text = s.StirrupLegs.ToString(CultureInfo.CurrentCulture);
            txtMaxLayers.Text = s.MaxLayers.ToString(CultureInfo.CurrentCulture);
            txtAggregate.Text = Format(s.AggregateSizeMm);
        }

        private bool ReadSettingsFromUi(out GradeBeamDesignSettings settings,
                                        out List<string> errors)
        {
            settings = new GradeBeamDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            settings.ConcreteUnitWeightKnM3 = ReadDouble(txtUnitWeight, "Poids du beton", errors);

            settings.Bedding = cmbBedding.SelectedIndex == 1
                ? GradeBeamBedding.SoilBearing : GradeBeamBedding.Suspended;
            settings.WallLoadKnPerM = ReadDouble(txtWallLoad, "Charge de mur", errors);
            settings.AllowableBearingPressureKpa = ReadDouble(txtBearing,
                "Contrainte admissible du sol", errors);
            settings.IncludeSelfWeight = chkSelfWeight.IsChecked == true;

            settings.SeismicDesign = chkSeismic.IsChecked == true;
            settings.Ground = (GroundType)Math.Max(cmbGround.SelectedIndex, 0);
            settings.GroundAccelerationRatio = ReadDouble(txtAlpha, "a_g / g", errors);
            settings.SoilFactor = ReadDouble(txtSoilFactor, "Coefficient de sol S", errors);
            settings.MeanColumnAxialLoadKn = ReadDouble(txtMeanColumn,
                "Effort normal moyen des poteaux", errors);
            settings.StoreyCount = ReadInt(txtStoreys, "Nombre de niveaux", errors);
            settings.ManualTieForceKn = ReadDouble(txtManualTie, "Liaison imposee", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CastDirectlyAgainstSoil = chkAgainstSoil.IsChecked == true;
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.AutoLongitudinalDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedLongitudinalDiameterMm = ValueAt(BarDatabase.LongitudinalDiameters,
                cmbDiameter.SelectedIndex, 16.0);
            settings.AutoStirrupDiameter = chkAutoStirrup.IsChecked == true;
            settings.ForcedStirrupDiameterMm = ValueAt(BarDatabase.TransverseDiameters,
                cmbStirrupDiameter.SelectedIndex, 8.0);
            settings.StirrupLegs = ReadInt(txtLegs, "Brins par cadre", errors);
            settings.MaxLayers = ReadInt(txtMaxLayers, "Lits maximum", errors);
            settings.AggregateSizeMm = ReadDouble(txtAggregate, "Granulat", errors);

            // Les conditions d'appui appartiennent a l'element, pas aux reglages.
            var spanKind = (GradeBeamSpanKind)Math.Max(cmbSpanKind.SelectedIndex, 0);
            foreach (GradeBeamData beam in _beams) beam.SpanKind = spanKind;

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            GradeBeamDesignSettings settings;
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
            Results = DesignPipeline.Run(new GradeBeamDesignModule(), _beams, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new GradeBeamReportItem(r,
                    QuantityCalculator.Compute(r.Beam, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new GradeBeamRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            int tied = Results.Count(r => r.TieForceKn > 0);

            var status = new StringBuilder();
            status.AppendFormat("{0} longrine(s) - {1} dimensionnee(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (tied > 0) status.AppendFormat(", {0} avec effort de liaison", tied);
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        private void UpdateQuantitiesSummary()
        {
            var total = new SteelQuantities();
            foreach (GradeBeamReportItem item in Items)
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

        private void UpdateTiePanels()
        {
            if (!_loaded) return;
            bool seismic = chkSeismic.IsChecked == true;
            grdSeismic.IsEnabled = seismic;
            grdManualTie.IsEnabled = !seismic;
        }

        private void OnSeismicChanged(object sender, RoutedEventArgs e)
        {
            UpdateTiePanels();
            if (_loaded) Calculate(false);
        }

        private void OnInputChanged(object sender, SelectionChangedEventArgs e)
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
                MessageBox.Show(this, "Aucune longrine n'a pu etre dimensionnee.",
                                "Generation impossible", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} longrine(s) ne satisfont pas toutes les verifications." +
                                  "{1}{1}Generer quand meme les armatures ?", notCompliant,
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
                StructuralCode = Settings.SeismicDesign
                    ? "EN 1992-1-1:2004+A1:2014, EN 1998-1 et EN 1998-5"
                    : "EN 1992-1-1:2004+A1:2014",
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-longrines-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildGradeBeams(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as GradeBeamRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgBeam.Source = GradeBeamPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildGradeBeamElement(
                new GradeBeamReportItem(row.Result, row.Quantities));
            imgBeam.Source = GradeBeamPreview.Render(row.Result, PreviewWidth, PreviewHeight);
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
