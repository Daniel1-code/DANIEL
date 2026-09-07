namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>Diametres commerciaux disponibles pour le ferraillage.</summary>
    public static class BarDatabase
    {
        /// <summary>Diametres de barres longitudinales (mm).</summary>
        public static readonly double[] LongitudinalDiameters = { 8, 10, 12, 14, 16, 20, 25, 32, 40 };

        /// <summary>Diametres d'armatures transversales (mm).</summary>
        public static readonly double[] TransverseDiameters = { 6, 8, 10, 12 };

        /// <summary>Plus petit diametre transversal superieur ou egal au minimum requis.</summary>
        public static double SmallestTransverseAtLeast(double requiredMm)
        {
            foreach (double diameter in TransverseDiameters)
            {
                if (diameter >= requiredMm - 1e-9) return diameter;
            }
            return TransverseDiameters[TransverseDiameters.Length - 1];
        }
    }
}
