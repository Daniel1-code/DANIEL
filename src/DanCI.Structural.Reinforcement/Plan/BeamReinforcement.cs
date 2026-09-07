using System.Collections.Generic;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>Une nappe de cadres a espacement constant le long de la poutre (mm).</summary>
    public sealed class BeamStirrupZone
    {
        public double StartMm { get; set; }
        public double EndMm { get; set; }
        public double SpacingMm { get; set; }
        public string Label { get; set; }

        /// <summary>Effort tranchant de calcul ayant dimensionne la zone (N).</summary>
        public double DesignShearN { get; set; }

        /// <summary>Section transversale requise A_sw/s (mm2/mm).</summary>
        public double RequiredAswPerMmMm2 { get; set; }

        public double LengthMm { get { return EndMm - StartMm; } }

        public int Count
        {
            get
            {
                if (LengthMm <= 0 || SpacingMm <= 0) return 0;
                return (int)System.Math.Ceiling(LengthMm / SpacingMm) + 1;
            }
        }
    }

    /// <summary>
    /// Ferraillage retenu pour une poutre. Dimensions en millimetres.
    /// </summary>
    public sealed class BeamReinforcement
    {
        /// <summary>Lit inferieur en travee.</summary>
        public BarSelection BottomSpan { get; set; }

        /// <summary>Chapeaux sur l'appui de gauche.</summary>
        public BarSelection TopLeft { get; set; }

        /// <summary>Chapeaux sur l'appui de droite.</summary>
        public BarSelection TopRight { get; set; }

        /// <summary>Barres de montage filantes en partie superieure.</summary>
        public BarSelection TopContinuous { get; set; }

        /// <summary>Aciers comprimes eventuels en travee.</summary>
        public BarSelection CompressionSpan { get; set; }

        // --- Armatures transversales ---
        public double StirrupDiameterMm { get; set; }
        /// <summary>Nombre de brins d'un cadre (2 pour un cadre simple).</summary>
        public int StirrupLegs { get; set; }
        public List<BeamStirrupZone> StirrupZones { get; private set; }

        // --- Cotes ---
        public double CoverMm { get; set; }
        /// <summary>Hauteur utile retenue pour le calcul (mm).</summary>
        public double EffectiveDepthMm { get; set; }
        /// <summary>Longueur d'ancrage de calcul l_bd (mm).</summary>
        public double AnchorageLengthMm { get; set; }
        /// <summary>Longueur de recouvrement l_0 (mm).</summary>
        public double LapLengthMm { get; set; }
        /// <summary>Decalage de la courbe des moments a_l (mm).</summary>
        public double ShiftLengthMm { get; set; }
        /// <summary>Longueur des chapeaux depuis le nu de l'appui (mm).</summary>
        public double TopBarLengthMm { get; set; }

        public BeamReinforcement()
        {
            BottomSpan = BarSelection.None();
            TopLeft = BarSelection.None();
            TopRight = BarSelection.None();
            TopContinuous = BarSelection.None();
            CompressionSpan = BarSelection.None();
            StirrupZones = new List<BeamStirrupZone>();
            StirrupLegs = 2;
        }

        public string LongitudinalLabel
        {
            get
            {
                string bottom = BottomSpan.Label;
                string top = TopLeft.Count > 0 || TopRight.Count > 0
                    ? string.Format(" / chapeaux {0}",
                        TopLeft.Count >= TopRight.Count ? TopLeft.Label : TopRight.Label)
                    : string.Empty;
                return bottom + top;
            }
        }

        public string TransverseLabel
        {
            get
            {
                if (StirrupDiameterMm <= 0 || StirrupZones.Count == 0) return "-";
                if (StirrupZones.Count == 1)
                {
                    return string.Format("HA{0:0} e={1:0}",
                        StirrupDiameterMm, StirrupZones[0].SpacingMm);
                }

                double tightest = double.MaxValue;
                double loosest = 0.0;
                foreach (BeamStirrupZone zone in StirrupZones)
                {
                    if (zone.SpacingMm < tightest) tightest = zone.SpacingMm;
                    if (zone.SpacingMm > loosest) loosest = zone.SpacingMm;
                }
                return string.Format("HA{0:0} e={1:0} aux appuis / {2:0} en travee",
                    StirrupDiameterMm, tightest, loosest);
            }
        }

        public int TotalStirrups
        {
            get
            {
                int total = 0;
                foreach (BeamStirrupZone zone in StirrupZones) total += zone.Count;
                return total;
            }
        }
    }
}
