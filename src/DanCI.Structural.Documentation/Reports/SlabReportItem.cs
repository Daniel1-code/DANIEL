using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Slab;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une dalle dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class SlabReportItem
    {
        public SlabDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public SlabReportItem()
        {
        }

        public SlabReportItem(SlabDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
