using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Ferraillage retenu pour une longrine. Les deux nappes filent d'un appui a l'autre :
    /// l'article 5.8.2(5) de l'EN 1998-1 exige 0,4 % en haut ET en bas, et l'effort de
    /// liaison etant alterne, les deux nappes travaillent tour a tour.
    /// </summary>
    public sealed class GradeBeamReinforcement
    {
        /// <summary>Nappe inferieure, filante.</summary>
        public BarSelection BottomBars { get; set; }

        /// <summary>Nappe superieure, filante.</summary>
        public BarSelection TopBars { get; set; }

        public double StirrupDiameterMm { get; set; }
        public int StirrupLegs { get; set; }
        /// <summary>Espacement des cadres (mm), constant sur toute la longrine.</summary>
        public double StirrupSpacingMm { get; set; }

        public double CoverMm { get; set; }
        public double EffectiveDepthMm { get; set; }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        public GradeBeamReinforcement()
        {
            BottomBars = BarSelection.None();
            TopBars = BarSelection.None();
            StirrupDiameterMm = 8.0;
            StirrupLegs = 2;
        }

        /// <summary>Nombre de cadres sur une portee donnee.</summary>
        public int StirrupCount(double spanMm)
        {
            if (StirrupSpacingMm <= 0) return 0;
            return (int)(spanMm / StirrupSpacingMm) + 1;
        }

        public string LongitudinalLabel
        {
            get
            {
                if (BottomBars.Count <= 0) return "-";
                return BottomBars.Label + " en bas, " + TopBars.Label + " en haut";
            }
        }

        public string TransverseLabel
        {
            get
            {
                if (StirrupSpacingMm <= 0) return "-";
                return string.Format("cadres HA{0:0} a {1} brins, e = {2:0} mm",
                                     StirrupDiameterMm, StirrupLegs, StirrupSpacingMm);
            }
        }
    }
}
