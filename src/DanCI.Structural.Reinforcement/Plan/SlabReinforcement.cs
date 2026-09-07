using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Ferraillage retenu pour une bande de dalle. Dimensions en millimetres, sections
    /// exprimees par metre de largeur.
    /// </summary>
    public sealed class SlabReinforcement
    {
        /// <summary>Nappe inferieure, sens porteur.</summary>
        public MeshSelection BottomMain { get; set; }

        /// <summary>Nappe inferieure, armatures de repartition.</summary>
        public MeshSelection BottomTransverse { get; set; }

        /// <summary>Chapeaux sur appui, sens porteur.</summary>
        public MeshSelection TopMain { get; set; }

        /// <summary>Repartition superieure, si des chapeaux sont poses.</summary>
        public MeshSelection TopTransverse { get; set; }

        public double CoverMm { get; set; }

        /// <summary>Hauteur utile du lit porteur inferieur (mm).</summary>
        public double EffectiveDepthMm { get; set; }

        /// <summary>Hauteur utile du lit porteur superieur (mm).</summary>
        public double TopEffectiveDepthMm { get; set; }

        /// <summary>Longueur des chapeaux depuis le nu d'appui (mm).</summary>
        public double TopBarLengthMm { get; set; }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        public SlabReinforcement()
        {
            BottomMain = new MeshSelection();
            BottomTransverse = new MeshSelection();
            TopMain = new MeshSelection();
            TopTransverse = new MeshSelection();
        }

        public bool HasTopReinforcement
        {
            get { return TopMain != null && TopMain.DiameterMm > 0; }
        }

        public string BottomLabel
        {
            get
            {
                if (BottomMain.DiameterMm <= 0) return "-";
                return BottomMain.Label + " // portee, " + BottomTransverse.Label + " repartition";
            }
        }

        public string TopLabel
        {
            get { return HasTopReinforcement ? TopMain.Label : "-"; }
        }
    }
}
