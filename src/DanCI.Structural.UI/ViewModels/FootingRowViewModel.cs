using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.IsolatedFooting;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats des semelles.</summary>
    public sealed class FootingRowViewModel
    {
        public FootingDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public FootingRowViewModel(FootingDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Footing.Name; } }

        public string Dimensions { get { return Result.Footing.SectionLabel; } }

        public string Poteau
        {
            get
            {
                return string.Format("{0:0} x {1:0}", Result.Footing.ColumnWidthXMm,
                                     Result.Footing.ColumnWidthYMm);
            }
        }

        public string Sol
        {
            get
            {
                return Result.Pressure == null
                    ? "-" : string.Format("{0:0}", Result.Pressure.EffectivePressureKpa);
            }
        }

        public string NappeInferieure { get { return Result.Reinforcement.BottomLabel; } }

        public string Attentes { get { return Result.Reinforcement.StarterLabel; } }

        public string Poinconnement
        {
            get
            {
                if (Result.Punching == null || Result.Punching.Critical == null) return "-";
                return string.Format("{0:0.00}", Result.Punching.Critical.Utilization);
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
