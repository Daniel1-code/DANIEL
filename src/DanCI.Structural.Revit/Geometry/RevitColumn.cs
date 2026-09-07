using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Un poteau Revit lu par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitColumn
    {
        public ColumnData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
