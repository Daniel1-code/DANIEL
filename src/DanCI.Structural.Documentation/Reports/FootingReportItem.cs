using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.IsolatedFooting;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une semelle dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class FootingReportItem
    {
        public FootingDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public FootingReportItem()
        {
        }

        public FootingReportItem(FootingDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
