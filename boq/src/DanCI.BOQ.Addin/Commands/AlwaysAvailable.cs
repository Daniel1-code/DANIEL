using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>
    /// Disponible meme sans document ouvert (par defaut, Revit grise les commandes
    /// externes tant qu'aucun projet n'est ouvert). Reserve aux commandes qui ne lisent
    /// pas le modele : A propos, diagnostic d'installation.
    /// </summary>
    public sealed class AlwaysAvailable : IExternalCommandAvailability
    {
        public bool IsCommandAvailable(UIApplication applicationData, CategorySet selectedCategories)
        {
            return true;
        }
    }
}
