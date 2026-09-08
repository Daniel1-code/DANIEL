using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Nature du noeud entre la volee et le palier, vue depuis la sous-face.
    ///
    /// C'est la geometrie du noeud, et non l'intensite du moment, qui decide du
    /// ferraillage : un angle rentrant tendu ne se ferraille pas comme un angle sortant.
    /// </summary>
    public enum KneeJointKind
    {
        /// <summary>Aucun noeud : la volee porte seule, sans palier dans la portee.</summary>
        None,

        /// <summary>
        /// Angle RENTRANT en sous-face, tendu par le moment de flexion. C'est le cas de
        /// tout noeud volee-palier sous moment positif : une barre qui suivrait le pli
        /// developperait une resultante dirigee vers l'exterieur du beton et ferait sauter
        /// l'enrobage. Les barres doivent se croiser et s'ancrer dans la face opposee.
        /// </summary>
        ReentrantInTension
    }

    /// <summary>
    /// Ferraillage retenu pour une volee d'escalier. Dimensions en millimetres, sections
    /// exprimees par metre de largeur.
    /// </summary>
    public sealed class StairReinforcement
    {
        /// <summary>Nappe inferieure, sens porteur.</summary>
        public MeshSelection BottomMain { get; set; }

        /// <summary>Nappe inferieure, armatures de repartition.</summary>
        public MeshSelection BottomTransverse { get; set; }

        /// <summary>Chapeaux sur appui, sens porteur.</summary>
        public MeshSelection TopMain { get; set; }

        /// <summary>Repartition superieure.</summary>
        public MeshSelection TopTransverse { get; set; }

        public double CoverMm { get; set; }

        /// <summary>Hauteur utile de la paillasse, mesuree perpendiculairement a la pente (mm).</summary>
        public double EffectiveDepthMm { get; set; }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        /// <summary>Longueur des chapeaux depuis le nu d'appui (mm).</summary>
        public double TopBarLengthMm { get; set; }

        /// <summary>Nature du noeud volee-palier.</summary>
        public KneeJointKind KneeJoint { get; set; }

        /// <summary>
        /// Regles de disposition retenues. Le constructeur de plan n'a AUCUNE decision de
        /// ferraillage en dur : tout ce qui n'est pas impose par la geometrie vient d'ici.
        /// </summary>
        public StairDetailingRules Rules { get; set; }

        /// <summary>
        /// Longueur d'ancrage des barres croisees au noeud, mesuree au-dela du pli (mm).
        /// </summary>
        public double KneeAnchorageMm { get; set; }

        public StairReinforcement()
        {
            BottomMain = new MeshSelection();
            BottomTransverse = new MeshSelection();
            TopMain = new MeshSelection();
            TopTransverse = new MeshSelection();
            KneeJoint = KneeJointKind.None;
            Rules = new StairDetailingRules();
        }

        public bool HasTopReinforcement
        {
            get { return TopMain != null && TopMain.DiameterMm > 0; }
        }

        public bool HasKneeJoint { get { return KneeJoint != KneeJointKind.None; } }

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
