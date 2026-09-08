using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.StripFooting;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une semelle filante dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class StripFootingReportItem
    {
        public StripFootingDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public StripFootingReportItem()
        {
        }

        public StripFootingReportItem(StripFootingDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
