using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Beam;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une poutre dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class BeamReportItem
    {
        public BeamDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public BeamReportItem()
        {
        }

        public BeamReportItem(BeamDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
