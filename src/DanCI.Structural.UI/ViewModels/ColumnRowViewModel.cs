using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Column;

namespace DanCI.Structural.UI.ViewModels
{
    /// <summary>Une ligne du tableau de resultats.</summary>
    public sealed class ColumnRowViewModel
    {
        public ColumnDesignResult Result { get; private set; }
        public SteelQuantities Quantities { get; private set; }

        public ColumnRowViewModel(ColumnDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }

        public string Element { get { return Result.Column.Name; } }

        public string Section { get { return Result.Column.SectionLabel; } }

        public string Longitudinales { get { return Result.Reinforcement.LongitudinalLabel; } }

        public string Transversales { get { return Result.Reinforcement.TransverseLabel; } }

        public string Taux { get { return string.Format("{0:0.00} %", Result.SteelRatioPercent); } }

        public string Poids
        {
            get { return Quantities == null ? "-" : string.Format("{0:0.0}", Quantities.TotalMassKg); }
        }

        public string Ratio
        {
            get { return Quantities == null ? "-" : string.Format("{0:0}", Quantities.RatioKgPerM3); }
        }

        public string Utilisation { get { return string.Format("{0:0.00}", Result.MaxUtilization); } }

        public string Etat { get { return Result.Status; } }
    }
}
