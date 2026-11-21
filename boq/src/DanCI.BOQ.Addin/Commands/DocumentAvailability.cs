using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>Disponible seulement avec un projet ouvert (pas un document famille).</summary>
    public sealed class ProjectAvailability : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            Document doc = applicationData?.ActiveUIDocument?.Document;
            return doc != null && !doc.IsFamilyDocument;
        }
    }
}
