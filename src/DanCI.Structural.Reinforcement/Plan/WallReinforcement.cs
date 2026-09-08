using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Ferraillage retenu pour un voile. Un voile porte deux nappes, une par parement :
    /// les sections annoncees sont **totales**, les deux nappes cumulees, comme sur un
    /// plan de ferraillage.
    /// </summary>
    public sealed class WallReinforcement
    {
        /// <summary>Aciers verticaux, par nappe et par metre de longueur.</summary>
        public MeshSelection VerticalPerFace { get; set; }

        /// <summary>Aciers horizontaux, par nappe et par metre de hauteur.</summary>
        public MeshSelection HorizontalPerFace { get; set; }

        public double CoverMm { get; set; }

        /// <summary>Hauteur utile de la bande verticale, flexion hors plan (mm).</summary>
        public double EffectiveDepthMm { get; set; }

        /// <summary>Nombre d'epingles de liaison au metre carre ; 0 si non exigees.</summary>
        public double LinksPerSquareMetre { get; set; }

        /// <summary>Diametre des epingles de liaison (mm).</summary>
        public double LinkDiameterMm { get; set; }

        /// <summary>Barres de rive, a chaque extremite du voile.</summary>
        public int EdgeBarCount { get; set; }
        public double EdgeBarDiameterMm { get; set; }

        public double AnchorageLengthMm { get; set; }
        public double LapLengthMm { get; set; }

        public WallReinforcement()
        {
            VerticalPerFace = new MeshSelection();
            HorizontalPerFace = new MeshSelection();
        }

        /// <summary>Section verticale totale des deux nappes (mm2/m).</summary>
        public double VerticalTotalMm2PerM
        {
            get { return 2.0 * VerticalPerFace.AreaPerMetreMm2; }
        }

        /// <summary>Section horizontale totale des deux nappes (mm2/m).</summary>
        public double HorizontalTotalMm2PerM
        {
            get { return 2.0 * HorizontalPerFace.AreaPerMetreMm2; }
        }

        public bool HasLinks { get { return LinksPerSquareMetre > 0 && LinkDiameterMm > 0; } }

        public bool HasEdgeBars { get { return EdgeBarCount > 0 && EdgeBarDiameterMm > 0; } }

        public string VerticalLabel
        {
            get
            {
                if (VerticalPerFace.DiameterMm <= 0) return "-";
                return VerticalPerFace.Label + " par nappe (2 nappes)";
            }
        }

        public string HorizontalLabel
        {
            get
            {
                if (HorizontalPerFace.DiameterMm <= 0) return "-";
                return HorizontalPerFace.Label + " par nappe (2 nappes)";
            }
        }

        public string EdgeLabel
        {
            get
            {
                if (!HasEdgeBars) return "-";
                return string.Format("{0} HA{1:0} par extremite", EdgeBarCount, EdgeBarDiameterMm);
            }
        }
    }
}
