using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Stair;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une volee d'escalier dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class StairReportItem
    {
        public StairDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public StairReportItem()
        {
        }

        public StairReportItem(StairDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
