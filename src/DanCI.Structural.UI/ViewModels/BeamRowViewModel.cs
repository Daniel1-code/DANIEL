using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Beam;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des poutres.</summary>
    public sealed class BeamRowViewModel
    {
        public BeamDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public BeamRowViewModel(BeamDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Beam.Name; } }

        public string Section { get { return Result.Beam.SectionLabel; } }

        public string Portee { get { return string.Format("{0:0}", Result.Beam.SpanMm); } }

        public string Travee { get { return Result.Reinforcement.BottomSpan.Label; } }

        public string Chapeaux
        {
            get
            {
                string left = Result.Reinforcement.TopLeft.Count > 0
                    ? Result.Reinforcement.TopLeft.Label : "-";
                string right = Result.Reinforcement.TopRight.Count > 0
                    ? Result.Reinforcement.TopRight.Label : "-";
                return left == right ? left : left + " / " + right;
            }
        }

        public string Cadres { get { return Result.Reinforcement.TransverseLabel; } }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        public string Etat { get { return Result.Status; } }
    }
}
