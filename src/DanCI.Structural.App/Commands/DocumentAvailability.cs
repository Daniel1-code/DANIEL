using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DanCI.Structural.App.Commands
{
    /// <summary>Le bouton n'est actif que si un projet est ouvert.</summary>
    public sealed class DocumentAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            return applicationData != null
                   && applicationData.ActiveUIDocument != null
                   && applicationData.ActiveUIDocument.Document != null
                   && !applicationData.ActiveUIDocument.Document.IsFamilyDocument;
        }
    }
}
