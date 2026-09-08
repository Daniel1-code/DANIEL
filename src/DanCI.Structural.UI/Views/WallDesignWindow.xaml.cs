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
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des voiles. Elle n'orchestre pas le calcul : elle appelle
    /// <see cref="DesignPipeline"/>.
    /// </summary>
    public partial class WallDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 820;

        private static readonly double[] EdgeDiameters = { 12, 14, 16, 20, 25, 32 };

        private readonly List<WallData> _walls;
        private bool _loaded;

        /// <summary>Reglages valides au moment de la generation.</summary>
        public WallDesignSettings Settings { get; private set; }

        /// <summary>Dimensionnements a modeliser.</summary>
        public List<WallDesignResult> Results { get; private set; }

        /// <summary>Resultats accompagnes de leur quantitatif.</summary>
        public List<WallReportItem> Items { get; private set; }

        public WallDesignWindow(List<WallData> walls, WallDesignSettings settings)
        {
            InitializeComponent();
            _walls = walls;
            Settings = settings;
            Results = new List<WallDesignResult>();
            Items = new List<WallReportItem>();

            FillDiameterLists();
            WriteSettingsToUi(settings);
            _loaded = true;
            Calculate(false);
        }

        private void FillDiameterLists()
        {
            foreach (double diameter in MeshOptimizer.Diameters)
            {
                cmbVerticalDiameter.Items.Add(string.Format("HA{0:0}", diameter));
                cmbHorizontalDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
            foreach (double diameter in EdgeDiameters)
            {
                cmbEdgeDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
        }

        private void WriteSettingsToUi(WallDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);

            cmbRestraint.SelectedIndex = (int)s.Restraint;
            txtRestraintSpacing.Text = Format(s.RestraintSpacingMm);
            txtCreep.Text = Format(s.CreepCoefficient);

            txtAxial.Text = Format(s.AxialLoadKnPerM);
            txtOutOfPlane.Text = Format(s.OutOfPlaneMomentKnmPerM);
            txtInPlaneShear.Text = Format(s.InPlaneShearKn);
            txtInPlaneMoment.Text = Format(s.InPlaneMomentKnm);

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            txtCover.Text = Format(s.CoverMm);

            chkAutoVertical.IsChecked = s.AutoVerticalDiameter;
            cmbVerticalDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters,
                                                        s.ForcedVerticalDiameterMm);
            chkAutoHorizontal.IsChecked = s.AutoHorizontalDiameter;
            cmbHorizontalDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters,
                                                          s.ForcedHorizontalDiameterMm);
            chkEdgeBars.IsChecked = s.EdgeBars;
            txtEdgeCount.Text = s.EdgeBarCount.ToString(CultureInfo.CurrentCulture);
            cmbEdgeDiameter.SelectedIndex = IndexOf(EdgeDiameters, s.EdgeBarDiameterMm);
        }

        private bool ReadSettingsFromUi(out WallDesignSettings settings, out List<string> errors)
        {
            settings = new WallDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);

            settings.Restraint = (WallRestraint)Math.Max(cmbRestraint.SelectedIndex, 0);
            settings.RestraintSpacingMm = ReadDouble(txtRestraintSpacing,
                "Distance entre rives maintenues", errors);
            settings.CreepCoefficient = ReadDouble(txtCreep, "Coefficient de fluage", errors);

            settings.AxialLoadKnPerM = ReadDouble(txtAxial, "Effort normal", errors);
            settings.OutOfPlaneMomentKnmPerM = ReadDouble(txtOutOfPlane, "Moment hors plan", errors);
            settings.InPlaneShearKn = ReadDouble(txtInPlaneShear,
                "Effort tranchant dans le plan", errors);
            settings.InPlaneMomentKnm = ReadDouble(txtInPlaneMoment,
                "Moment dans le plan", errors);

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.AutoVerticalDiameter = chkAutoVertical.IsChecked == true;
            settings.ForcedVerticalDiameterMm = ValueAt(MeshOptimizer.Diameters,
                cmbVerticalDiameter.SelectedIndex, 10.0);
            settings.AutoHorizontalDiameter = chkAutoHorizontal.IsChecked == true;
            settings.ForcedHorizontalDiameterMm = ValueAt(MeshOptimizer.Diameters,
                cmbHorizontalDiameter.SelectedIndex, 8.0);
            settings.EdgeBars = chkEdgeBars.IsChecked == true;
            settings.EdgeBarCount = ReadInt(txtEdgeCount, "Nombre de barres de rive", errors);
            settings.EdgeBarDiameterMm = ValueAt(EdgeDiameters, cmbEdgeDiameter.SelectedIndex, 16.0);

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            WallDesignSettings settings;
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
            Results = DesignPipeline.Run(new WallDesignModule(), _walls, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new WallReportItem(r, QuantityCalculator.Compute(r.Wall, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new WallRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            int secondOrder = Results.Count(
                r => r.SecondOrder != null && r.SecondOrder.Required);

            var status = new StringBuilder();
            status.AppendFormat("{0} voile(s) - {1} dimensionne(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (secondOrder > 0)
            {
                status.AppendFormat(", {0} avec second ordre", secondOrder);
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
            foreach (WallReportItem item in Items)
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
                MessageBox.Show(this, "Aucun voile n'a pu etre dimensionne.",
                                "Generation impossible", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} voile(s) ne satisfont pas toutes les verifications.{1}{1}" +
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
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-voiles-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildWalls(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as WallRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgWall.Source = WallPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildWallElement(
                new WallReportItem(row.Result, row.Quantities));
            imgWall.Source = WallPreview.Render(row.Result, PreviewWidth, PreviewHeight);
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
