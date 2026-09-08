using Autodesk.Revit.DB;
using Autodesk.Revit.UI.Selection;

namespace DanCI.Structural.Revit.Selection
{
    /// <summary>
    /// Limite la selection interactive aux escaliers et aux planchers.
    ///
    /// Les deux sont acceptes a dessein : l'escalier porte la geometrie de marche, le
    /// plancher structurel incline est le seul des deux a pouvoir recevoir des armatures.
    /// C'est le lecteur qui dira lequel a ete choisi et ce qu'il permet.
    /// </summary>
    public sealed class StairSelectionFilter : ISelectionFilter
    {
        public bool AllowElement(Element element)
        {
            if (element == null || element.Category == null) return false;
            long category = element.Category.Id.Value;
            return category == (long)BuiltInCategory.OST_Stairs
                   || category == (long)BuiltInCategory.OST_Floors;
        }

        public bool AllowReference(Reference reference, XYZ position)
        {
            return false;
        }
    }
}
