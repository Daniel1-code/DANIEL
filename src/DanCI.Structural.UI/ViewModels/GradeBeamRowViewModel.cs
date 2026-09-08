using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.GradeBeam;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des longrines.</summary>
    public sealed class GradeBeamRowViewModel
    {
        public GradeBeamDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public GradeBeamRowViewModel(GradeBeamDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Beam.Name; } }

        public string Section
        {
            get
            {
                return string.Format("{0:0} x {1:0}", Result.Beam.WidthMm, Result.Beam.HeightMm);
            }
        }

        public string Portee { get { return string.Format("{0:0}", Result.Beam.SpanMm); } }

        public string Charge { get { return string.Format("{0:0.0}", Result.DesignLoadKnPerM); } }

        public string Moment { get { return string.Format("{0:0.0}", Result.SpanMomentKnm); } }

        public string Liaison
        {
            get
            {
                return Result.TieForceKn > 0
                    ? string.Format("+-{0:0.0}", Result.TieForceKn) : "-";
            }
        }

        public string NappeInferieure { get { return Result.Reinforcement.BottomBars.Label; } }

        public string NappeSuperieure { get { return Result.Reinforcement.TopBars.Label; } }

        public string Cadres
        {
            get
            {
                return Result.Reinforcement.StirrupSpacingMm > 0
                    ? string.Format("HA{0:0} e={1:0}", Result.Reinforcement.StirrupDiameterMm,
                                    Result.Reinforcement.StirrupSpacingMm)
                    : "-";
            }
        }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        public string Etat { get { return Result.Status; } }
    }
}
