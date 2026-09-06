using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace ArmaturesPoteaux.Commands
{
    /// <summary>Limite la selection interactive aux poteaux structurels.</summary>
    public class ColumnSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            if (element == null || element.Category == null) return false;
            return element.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
