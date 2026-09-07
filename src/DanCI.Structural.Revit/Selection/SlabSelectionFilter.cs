using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace DanCI.Structural.Revit.Selection
{
    /// <summary>Limite la selection interactive aux planchers.</summary>
    public sealed class SlabSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            if (element == null || element.Category == null) return false;
            return element.Category.Id.Value == (long)BuiltInCategory.OST_Floors;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
