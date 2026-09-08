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
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.UI.Controls;
using DanCI.Structural.UI.ViewModels;

namespace DanCI.Structural.UI.Views
{
    /// <summary>
    /// Fenetre de dimensionnement des volees d'escalier. Elle n'orchestre pas le calcul :
    /// elle appelle <see cref="DesignPipeline"/>.
    ///
    /// La geometrie de la marche est modifiable ici parce qu'elle est souvent absente du
    /// modele — et parce que c'est elle, via la pente, qui pilote tout le poids propre.
    /// Le resume sous les champs le montre en direct.
    /// </summary>
    public partial class StairDesignWindow : Window
    {
        private const int PreviewWidth = 700;
        private const int PreviewHeight = 860;

        private readonly List<StairData> _stairs;
        private bool _loaded;

        public StairDesignSettings Settings { get; private set; }

        public List<StairDesignResult> Results { get; private set; }

        public List<StairReportItem> Items { get; private set; }

        public StairDesignWindow(List<StairData> stairs, StairDesignSettings settings)
        {
            InitializeComponent();
            _stairs = stairs;
            Settings = settings;
            Results = new List<StairDesignResult>();
            Items = new List<StairReportItem>();

            FillLists();
            WriteSettingsToUi(settings);
            _loaded = true;
            UpdateGeometrySummary();
            Calculate(false);
        }

        private void FillLists()
        {
            foreach (double diameter in MeshOptimizer.Diameters)
            {
                cmbDiameter.Items.Add(string.Format("HA{0:0}", diameter));
            }
        }

        private void WriteSettingsToUi(StairDesignSettings s)
        {
            cmbAnnex.SelectedIndex = s.NationalAnnex == NationalAnnexKind.France ? 1 : 0;
            txtFck.Text = Format(s.ConcreteStrengthMPa);
            txtFyk.Text = Format(s.SteelStrengthMPa);
            txtUnitWeight.Text = Format(s.ConcreteUnitWeightKnM3);

            StairData first = _stairs.Count > 0 ? _stairs[0] : new StairData();
            txtRiserCount.Text = first.RiserCount.ToString(CultureInfo.CurrentCulture);
            txtRiser.Text = Format(first.RiserHeightMm);
            txtTread.Text = Format(first.TreadDepthMm);
            txtWaist.Text = Format(first.WaistThicknessMm);
            txtWidth.Text = Format(first.WidthMm);
            txtLandingThickness.Text = Format(first.LandingThicknessMm);
            txtLandingSpan.Text = Format(first.LandingSpanMm);
            cmbSpanKind.SelectedIndex = (int)first.SpanKind;

            cmbCategory.SelectedIndex = (int)s.Category;
            txtVariable.Text = Format(s.VariableLoadKnM2);
            txtTreadFinish.Text = Format(s.TreadFinishKnM2);
            txtSoffitFinish.Text = Format(s.SoffitFinishKnM2);
            txtConcentrated.Text = Format(s.ConcentratedLoadKn);
            chkSelfWeight.IsChecked = s.IncludeSelfWeight;

            chkAutoCover.IsChecked = s.AutoCover;
            cmbExposure.SelectedIndex = (int)s.Exposure;
            txtCover.Text = Format(s.CoverMm);

            chkAutoDiameter.IsChecked = s.AutoMeshDiameter;
            cmbDiameter.SelectedIndex = IndexOf(MeshOptimizer.Diameters, s.ForcedMeshDiameterMm);
            chkTopBars.IsChecked = s.TopReinforcement;
            chkPartitions.IsChecked = s.SupportsPartitions;
            txtCrackWidth.Text = Format(s.CrackWidthLimitMm);
        }

        private bool ReadSettingsFromUi(out StairDesignSettings settings, out List<string> errors)
        {
            settings = new StairDesignSettings();
            errors = new List<string>();

            settings.NationalAnnex = cmbAnnex.SelectedIndex == 1
                ? NationalAnnexKind.France : NationalAnnexKind.Recommended;
            settings.ConcreteStrengthMPa = ReadDouble(txtFck, "Resistance du beton", errors);
            settings.SteelStrengthMPa = ReadDouble(txtFyk, "Limite d'elasticite de l'acier", errors);
            settings.ConcreteUnitWeightKnM3 = ReadDouble(txtUnitWeight, "Poids du beton", errors);

            settings.Category = (UseCategory)Math.Max(cmbCategory.SelectedIndex, 0);
            settings.VariableLoadKnM2 = ReadDouble(txtVariable, "Charge d'exploitation", errors);
            settings.TreadFinishKnM2 = ReadDouble(txtTreadFinish, "Revetement de marche", errors);
            settings.SoffitFinishKnM2 = ReadDouble(txtSoffitFinish, "Enduit de sous-face", errors);
            settings.ConcentratedLoadKn = ReadDouble(txtConcentrated, "Charge concentree Q_k", errors);
            settings.IncludeSelfWeight = chkSelfWeight.IsChecked == true;

            settings.AutoCover = chkAutoCover.IsChecked == true;
            settings.Exposure = (ExposureClass)Math.Max(cmbExposure.SelectedIndex, 0);
            settings.CoverMm = ReadDouble(txtCover, "Enrobage", errors);

            settings.AutoMeshDiameter = chkAutoDiameter.IsChecked == true;
            settings.ForcedMeshDiameterMm = ValueAt(MeshOptimizer.Diameters,
                cmbDiameter.SelectedIndex, 12.0);
            settings.TopReinforcement = chkTopBars.IsChecked == true;
            settings.SupportsPartitions = chkPartitions.IsChecked == true;
            settings.CrackWidthLimitMm = ReadDouble(txtCrackWidth, "Ouverture de fissure", errors);

            // La geometrie appartient a l'element, pas aux reglages.
            int riserCount = ReadInt(txtRiserCount, "Nombre de contremarches", errors);
            double riser = ReadDouble(txtRiser, "Hauteur de contremarche", errors);
            double tread = ReadDouble(txtTread, "Giron", errors);
            double waist = ReadDouble(txtWaist, "Epaisseur de paillasse", errors);
            double width = ReadDouble(txtWidth, "Largeur de volee", errors);
            double landingThickness = ReadDouble(txtLandingThickness, "Epaisseur de palier", errors);
            double landingSpan = ReadDouble(txtLandingSpan, "Palier dans la portee", errors);
            var spanKind = (StairSpanKind)Math.Max(cmbSpanKind.SelectedIndex, 0);

            if (riserCount < 2) errors.Add("Une volee compte au moins deux contremarches.");
            if (riser <= 0 || tread <= 0)
                errors.Add("La hauteur de contremarche et le giron doivent etre positifs.");
            if (waist <= 0) errors.Add("L'epaisseur de paillasse doit etre positive.");
            if (width <= 0) errors.Add("La largeur de volee doit etre positive.");

            foreach (StairData stair in _stairs)
            {
                stair.RiserCount = riserCount;
                stair.RiserHeightMm = riser;
                stair.TreadDepthMm = tread;
                stair.WaistThicknessMm = waist;
                stair.WidthMm = width;
                stair.LandingThicknessMm = landingThickness;
                stair.LandingSpanMm = landingSpan;
                stair.SpanKind = spanKind;
            }

            errors.AddRange(settings.Validate());
            return errors.Count == 0;
        }

        private bool Calculate(bool reportErrors)
        {
            StairDesignSettings settings;
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
            Results = DesignPipeline.Run(new StairDesignModule(), _stairs, settings, null);

            Items = Results
                .Where(r => r != null)
                .Select(r => new StairReportItem(r,
                    QuantityCalculator.Compute(r.Stair, r.Plan)))
                .ToList();

            grdResults.ItemsSource = Items
                .Select(i => new StairRowViewModel(i.Result, i.Quantities))
                .ToList();
            if (grdResults.Items.Count > 0) grdResults.SelectedIndex = 0;

            UpdateQuantitiesSummary();

            int failed = Results.Count(r => !r.IsValid);
            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            int warned = Results.Count(r => r.IsValid && r.Warnings.Count > 0);
            int knees = Results.Count(r => r.IsValid && r.Reinforcement.HasKneeJoint);

            var status = new StringBuilder();
            status.AppendFormat("{0} volee(s) - {1} dimensionnee(s)", Results.Count,
                                Results.Count(r => r.IsValid));
            if (knees > 0) status.AppendFormat(", {0} avec noeud croise", knees);
            int concentrated = Results.Count(r => r.IsValid && r.ConcentratedLoadGoverns);
            if (concentrated > 0)
                status.AppendFormat(", {0} gouvernee(s) par Q_k", concentrated);
            if (warned > 0) status.AppendFormat(", {0} a verifier", warned);
            if (notCompliant > 0) status.AppendFormat(", {0} non conforme(s)", notCompliant);
            if (failed > 0) status.AppendFormat(", {0} en echec", failed);
            txtStatus.Text = status + ".";
            return true;
        }

        /// <summary>
        /// Montre en direct ce que la geometrie de la marche implique. La pente et le
        /// facteur 1/cos alpha ne sont pas des details : ils pilotent le poids propre.
        /// </summary>
        private void UpdateGeometrySummary()
        {
            if (!_loaded) return;

            var probe = new StairData();
            int riserCount;
            double riser, tread, waist, landingSpan;
            int.TryParse((txtRiserCount.Text ?? string.Empty).Trim(), out riserCount);
            TryParse(txtRiser.Text, out riser);
            TryParse(txtTread.Text, out tread);
            TryParse(txtWaist.Text, out waist);
            TryParse(txtLandingSpan.Text, out landingSpan);

            if (riserCount < 2 || riser <= 0 || tread <= 0)
            {
                txtGeometrySummary.Text = string.Empty;
                return;
            }

            probe.RiserCount = riserCount;
            probe.RiserHeightMm = riser;
            probe.TreadDepthMm = tread;
            probe.WaistThicknessMm = waist;
            probe.LandingSpanMm = landingSpan;
            probe.SpanKind = (StairSpanKind)Math.Max(cmbSpanKind.SelectedIndex, 0);

            string blondel = probe.BlondelValueMm >= 600.0 && probe.BlondelValueMm <= 650.0
                ? "confortable" : "hors plage 600-650";

            txtGeometrySummary.Text = string.Format(CultureInfo.CurrentCulture,
                "{0} girons - denivele {1:0} mm - projection {2:0} mm - pente {3:0.0} deg " +
                "(cos alpha = {4:0.000}){5}Portee de calcul {6:0} mm. Paillasse vue " +
                "verticalement : {7:0} mm, soit t / cos alpha.{5}Blondel 2R + G = {8:0} mm, {9} " +
                "(regle d'ergonomie, hors Eurocode).",
                riserCount - 1, probe.TotalRiseMm, probe.TotalGoingMm, probe.SlopeAngleDegrees,
                probe.SlopeCosine, Environment.NewLine, probe.SpanMm,
                waist / Math.Max(probe.SlopeCosine, 0.05), probe.BlondelValueMm, blondel);
        }

        private void UpdateQuantitiesSummary()
        {
            var total = new SteelQuantities();
            foreach (StairReportItem item in Items)
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
            sb.AppendLine("Le volume compte la paillasse suivant sa pente et les marches.");
            sb.Append(total.DiameterBreakdown());
            txtQuantities.Text = sb.ToString();
        }

        private void OnGeometryChanged(object sender, TextChangedEventArgs e)
        {
            UpdateGeometrySummary();
            if (_loaded) Calculate(false);
        }

        private void OnGeometryChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateGeometrySummary();
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
                MessageBox.Show(this, "Aucune volee n'a pu etre dimensionnee.",
                                "Generation impossible", MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                return;
            }

            int notCompliant = Results.Count(r => r.IsValid && r.HasFailedCheck);
            if (notCompliant > 0)
            {
                MessageBoxResult answer = MessageBox.Show(this,
                    string.Format("{0} volee(s) ne satisfont pas toutes les verifications." +
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
                StructuralCode = "EN 1992-1-1:2004+A1:2014, EN 1991-1-1 et EN 1990",
                NationalAnnex = Settings.NationalAnnex == NationalAnnexKind.France
                    ? "Annexe Nationale francaise (a completer)" : "Valeurs recommandees"
            };

            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de calcul",
                FileName = "note-de-calcul-escaliers-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (dialog.ShowDialog(this) != true) return;

            try
            {
                System.IO.File.WriteAllText(dialog.FileName,
                    CalculationReport.BuildStairs(header, Items), new UTF8Encoding(true));
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
            var row = grdResults.SelectedItem as StairRowViewModel;
            if (row == null)
            {
                txtNotes.Text = string.Empty;
                imgStair.Source = StairPreview.Render(null, PreviewWidth, PreviewHeight);
                return;
            }

            txtNotes.Text = CalculationReport.BuildStairElement(
                new StairReportItem(row.Result, row.Quantities));
            imgStair.Source = StairPreview.Render(row.Result, PreviewWidth, PreviewHeight);
        }

        private static string Format(double value)
        {
            return value.ToString("0.###", CultureInfo.CurrentCulture);
        }

        private static bool TryParse(string text, out double value)
        {
            return double.TryParse((text ?? string.Empty).Trim().Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out value);
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
            double value;
            if (TryParse(box.Text, out value)) return value;
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
