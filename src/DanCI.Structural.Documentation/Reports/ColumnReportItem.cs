using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Engine.Column;

namespace DanCI.Structural.Documentation.Reports
{
    /// <summary>Un poteau dimensionne, accompagne de son quantitatif.</summary>
    public sealed class ColumnReportItem
    {
        public ColumnDesignResult Result { get; set; }
        public SteelQuantities Quantities { get; set; }

        public ColumnReportItem()
        {
        }

        public ColumnReportItem(ColumnDesignResult result, SteelQuantities quantities)
        {
            Result = result;
            Quantities = quantities;
        }
    }
}
