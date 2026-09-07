namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>Preferences de ferraillage imposees par l'utilisateur pour un poteau.</summary>
    public sealed class ColumnLayoutOptions
    {
        public double CoverMm { get; set; }
        public double AggregateSizeMm { get; set; }

        public bool AutoDiameter { get; set; }
        public double ForcedDiameterMm { get; set; }

        public bool AutoCount { get; set; }
        public int ForcedBarsAlongX { get; set; }
        public int ForcedBarsAlongY { get; set; }
        public int ForcedCircularBarCount { get; set; }

        public bool AutoTransverse { get; set; }
        public double ForcedTransverseDiameterMm { get; set; }

        public ColumnLayoutOptions()
        {
            CoverMm = 30.0;
            AggregateSizeMm = 20.0;
            AutoDiameter = true;
            ForcedDiameterMm = 16.0;
            AutoCount = true;
            ForcedBarsAlongX = 3;
            ForcedBarsAlongY = 3;
            ForcedCircularBarCount = 6;
            AutoTransverse = true;
            ForcedTransverseDiameterMm = 8.0;
        }
    }

    /// <summary>Une disposition de barres candidate, et son bilan.</summary>
    public sealed class ColumnBarLayout
    {
        public double DiameterMm { get; set; }
        public double TransverseDiameterMm { get; set; }
        public int CountAlongX { get; set; }
        public int CountAlongY { get; set; }
        public int TotalBars { get; set; }
        public double SteelAreaMm2 { get; set; }
        public double PitchXMm { get; set; }
        public double PitchYMm { get; set; }
        /// <summary>Plus petit espacement libre entre barres (mm).</summary>
        public double ClearSpacingMm { get; set; }
    }
}
