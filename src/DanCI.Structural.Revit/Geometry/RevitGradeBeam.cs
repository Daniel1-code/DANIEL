using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une longrine Revit : ses donnees de calcul et son repere.</summary>
    public sealed class RevitGradeBeam
    {
        public GradeBeamData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
