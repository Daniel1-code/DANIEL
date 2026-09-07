using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Slab;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des dalles.</summary>
    public sealed class SlabRowViewModel
    {
        public SlabDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public SlabRowViewModel(SlabDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Slab.Name; } }

        public string Epaisseur { get { return string.Format("{0:0}", Result.Slab.ThicknessMm); } }

        public string Portee { get { return string.Format("{0:0}", Result.Slab.SpanMm); } }

        public string Charge
        {
            get
            {
                return Result.UltimateLoadKnM2 > 0
                    ? string.Format("{0:0.00}", Result.UltimateLoadKnM2) : "-";
            }
        }

        public string Moment { get { return string.Format("{0:0.0}", Result.SpanMomentKnmPerM); } }

        public string Nappe { get { return Result.Reinforcement.BottomMain.Label; } }

        public string Repartition { get { return Result.Reinforcement.BottomTransverse.Label; } }

        public string Chapeaux { get { return Result.Reinforcement.TopLabel; } }

        public string Fleche
        {
            get
            {
                return Result.Deflection == null
                    ? "-" : string.Format("{0:0.00}", Result.Deflection.Utilization);
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
