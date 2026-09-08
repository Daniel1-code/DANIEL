using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une semelle filante Revit : ses donnees de calcul et son repere.</summary>
    public sealed class RevitStripFooting
    {
        public StripFootingData Data { get; set; }
        public RevitElementFrame Frame { get; set; }
    }
}
