using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// Module Poutre : annonce clairement son etat d'avancement plutot que de laisser croire
    /// a une fonction disponible.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class BeamDesignCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = "DanCI Beam Design - en cours de developpement",
                MainContent =
                    "Ce module couvrira, selon l'EN 1992-1-1 :" + Environment.NewLine +
                    "  - la flexion (sections rectangulaires et en T) ;" + Environment.NewLine +
                    "  - l'effort tranchant avec bielle a inclinaison variable ;" + Environment.NewLine +
                    "  - les ancrages et les recouvrements ;" + Environment.NewLine +
                    "  - la generation des armatures 3D et les verifications detaillees." +
                    Environment.NewLine + Environment.NewLine +
                    "Il constitue la phase 2 de la feuille de route, apres la stabilisation et la " +
                    "validation du module Poteau."
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
