using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.StripFooting;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des semelles filantes.</summary>
    public sealed class StripFootingRowViewModel
    {
        public StripFootingDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public StripFootingRowViewModel(StripFootingDesignResult result,
                                        SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Footing.Name; } }

        public string Largeur { get { return string.Format("{0:0}", Result.Footing.WidthMm); } }

        public string Epaisseur
        {
            get { return string.Format("{0:0}", Result.Footing.ThicknessMm); }
        }

        public string Debord { get { return string.Format("{0:0}", Result.Footing.OverhangMm); } }

        public string Sol
        {
            get
            {
                return Result.Pressure == null
                    ? "-" : string.Format("{0:0}", Result.Pressure.EffectivePressureKpa);
            }
        }

        public string Transversales { get { return Result.Reinforcement.TransverseLabel; } }

        public string Longitudinales { get { return Result.Reinforcement.LongitudinalLabel; } }

        public string Crochets
        {
            get { return Result.Reinforcement.TransverseNeedsHook ? "oui" : "non"; }
        }

        public string Attentes { get { return Result.Reinforcement.StarterLabel; } }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        public string Etat { get { return Result.Status; } }
    }
}
