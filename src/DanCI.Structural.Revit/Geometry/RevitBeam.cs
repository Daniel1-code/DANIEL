using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une poutre Revit lue par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitBeam
    {
        public BeamData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
