using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Ferraillage retenu pour une semelle filante. Sections exprimees par metre courant.
    /// </summary>
    public sealed class StripFootingReinforcement
    {
        /// <summary>Armatures transversales inferieures, en chapeau de console.</summary>
        public MeshSelection Transverse { get; set; }

        /// <summary>Armatures longitudinales de repartition, filantes.</summary>
        public MeshSelection Longitudinal { get; set; }

        /// <summary>Armatures transversales superieures, si elles sont demandees.</summary>
        public MeshSelection TopTransverse { get; set; }

        public double CoverMm { get; set; }

        /// <summary>Hauteur utile des armatures transversales (mm).</summary>
        public double EffectiveDepthMm { get; set; }

        /// <summary>Attentes du voile : espacement le long de la semelle (mm).</summary>
        public double StarterSpacingMm { get; set; }
        public double StarterDiameterMm { get; set; }
        /// <summary>Retour horizontal en pied des attentes (mm).</summary>
        public double StarterReturnMm { get; set; }
        /// <summary>Depassement au-dessus de la semelle (mm).</summary>
        public double StarterProjectionMm { get; set; }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        /// <summary>Les armatures transversales exigent-elles un crochet d'extremite ?</summary>
        public bool TransverseNeedsHook { get; set; }

        public StripFootingReinforcement()
        {
            Transverse = new MeshSelection();
            Longitudinal = new MeshSelection();
            TopTransverse = new MeshSelection();
        }

        public bool HasTopMesh
        {
            get { return TopTransverse != null && TopTransverse.DiameterMm > 0; }
        }

        public bool HasStarters
        {
            get { return StarterSpacingMm > 0 && StarterDiameterMm > 0; }
        }

        public string TransverseLabel
        {
            get { return Transverse.DiameterMm <= 0 ? "-" : Transverse.Label; }
        }

        public string LongitudinalLabel
        {
            get { return Longitudinal.DiameterMm <= 0 ? "-" : Longitudinal.Label; }
        }

        public string StarterLabel
        {
            get
            {
                if (!HasStarters) return "-";
                return string.Format("HA{0:0} e={1:0} (2 files)",
                                     StarterDiameterMm, StarterSpacingMm);
            }
        }
    }
}
