using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Un voile Revit lu par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitWall
    {
        public WallData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
