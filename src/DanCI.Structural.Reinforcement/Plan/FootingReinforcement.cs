using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>Ferraillage retenu pour une semelle isolee. Dimensions en millimetres.</summary>
    public sealed class FootingReinforcement
    {
        /// <summary>Nappe inferieure, barres filant suivant X.</summary>
        public MeshSelection BottomX { get; set; }

        /// <summary>Nappe inferieure, barres filant suivant Y.</summary>
        public MeshSelection BottomY { get; set; }

        /// <summary>Nappe superieure suivant X, si elle est demandee.</summary>
        public MeshSelection TopX { get; set; }

        /// <summary>Nappe superieure suivant Y, si elle est demandee.</summary>
        public MeshSelection TopY { get; set; }

        /// <summary>Attentes du poteau : nombre de barres.</summary>
        public int StarterBarCount { get; set; }

        /// <summary>Attentes du poteau : diametre (mm).</summary>
        public double StarterBarDiameterMm { get; set; }

        /// <summary>Longueur du retour horizontal des attentes en pied (mm).</summary>
        public double StarterReturnMm { get; set; }

        /// <summary>Hauteur des attentes au-dessus du dessus de la semelle (mm).</summary>
        public double StarterProjectionMm { get; set; }

        public double CoverMm { get; set; }

        /// <summary>Hauteur utile de la nappe inferieure suivant X (mm).</summary>
        public double EffectiveDepthXMm { get; set; }

        /// <summary>Hauteur utile de la nappe inferieure suivant Y (mm).</summary>
        public double EffectiveDepthYMm { get; set; }

        /// <summary>Hauteur utile moyenne, utilisee pour le poinconnement (mm).</summary>
        public double MeanEffectiveDepthMm
        {
            get { return (EffectiveDepthXMm + EffectiveDepthYMm) / 2.0; }
        }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        public FootingReinforcement()
        {
            BottomX = new MeshSelection();
            BottomY = new MeshSelection();
            TopX = new MeshSelection();
            TopY = new MeshSelection();
        }

        public bool HasTopMesh
        {
            get { return TopX != null && TopX.DiameterMm > 0; }
        }

        public string BottomLabel
        {
            get
            {
                if (BottomX.DiameterMm <= 0) return "-";
                return BottomX.Label == BottomY.Label
                    ? BottomX.Label + " (2 sens)"
                    : BottomX.Label + " // X, " + BottomY.Label + " // Y";
            }
        }

        public string StarterLabel
        {
            get
            {
                if (StarterBarCount <= 0) return "-";
                return string.Format("{0} HA{1:0}", StarterBarCount, StarterBarDiameterMm);
            }
        }
    }
}
