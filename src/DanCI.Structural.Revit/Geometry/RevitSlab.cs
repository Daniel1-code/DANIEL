using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une dalle Revit lue par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitSlab
    {
        public SlabData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
