using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace DanCI.Structural.App.Commands
{
    /// <summary>Versions du produit et regles de calcul appliquees.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var dialog = new TaskDialog("A propos")
            {
                MainInstruction = ProductInfo.Name + " " + ProductInfo.ApplicationVersion,
                MainContent =
                    ProductInfo.Tagline + Environment.NewLine + Environment.NewLine +
                    "Versions :" + Environment.NewLine +
                    "  - Application         : " + ProductInfo.ApplicationVersion + Environment.NewLine +
                    "  - Moteur de calcul    : " + ProductInfo.CalculationEngineVersion + Environment.NewLine +
                    "  - Bibliotheque EC     : " + ProductInfo.EurocodeLibraryVersion + Environment.NewLine +
                    "  - Schema des donnees  : " + ProductInfo.DesignDataSchemaVersion +
                    Environment.NewLine + Environment.NewLine +
                    "Modules disponibles :" + Environment.NewLine +
                    "  - DanCI Column Design (poteaux rectangulaires et circulaires)." +
                    Environment.NewLine + Environment.NewLine +
                    "Regles appliquees :" + Environment.NewLine +
                    "  - EN 1992-1-1:2004+A1:2014 art. 9.5 (poteaux), 8.2, 8.4 et 8.7," +
                    Environment.NewLine +
                    "    3.1.7, 5.8.3, 5.8.8 et 5.8.9 pour la flexion composee ;" + Environment.NewLine +
                    "  - dispositions sismiques EN 1998-1 art. 5.4.3.2.2 en option ;" +
                    Environment.NewLine +
                    "  - ACI 318-19 art. 10.6, 10.7.3 et 25.7.2 pour les projets hors Europe." +
                    Environment.NewLine + Environment.NewLine +
                    "Le resultat est un avant-projet de ferraillage : il doit etre verifie et " +
                    "valide par l'ingenieur responsable du projet."
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
