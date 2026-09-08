using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Wall;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Un voile dimensionne, accompagne de son quantitatif.</summary>
    public sealed class WallReportItem
    {
        public WallDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public WallReportItem()
        {
        }

        public WallReportItem(WallDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
