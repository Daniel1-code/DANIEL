using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.GradeBeam;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Une longrine dimensionnee, accompagnee de son quantitatif.</summary>
    public sealed class GradeBeamReportItem
    {
        public GradeBeamDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public GradeBeamReportItem()
        {
        }

        public GradeBeamReportItem(GradeBeamDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
