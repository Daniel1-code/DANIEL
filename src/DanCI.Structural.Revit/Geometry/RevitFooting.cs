using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une semelle Revit lue par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitFooting
    {
        public FootingData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
