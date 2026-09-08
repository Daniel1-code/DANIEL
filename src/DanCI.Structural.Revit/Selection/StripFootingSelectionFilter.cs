using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace DanCI.Structural.Revit.Selection
{
    /// <summary>Limite la selection interactive aux semelles filantes.</summary>
    public sealed class StripFootingSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            return element is WallFoundation;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
