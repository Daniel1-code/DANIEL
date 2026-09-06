using System;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace ArmaturesPoteaux.Commands
{
    /// <summary>Rappelle la version du plugin et les regles appliquees.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            var dialog = new TaskDialog("A propos")
            {
                MainInstruction = "Armatures de poteaux " + version,
                MainContent =
                    "Genere les armatures longitudinales, les cadres et les epingles des poteaux " +
                    "beton arme rectangulaires et circulaires." + Environment.NewLine + Environment.NewLine +
                    "Regles appliquees :" + Environment.NewLine +
                    "  - Eurocode 2, EN 1992-1-1 art. 9.5 (poteaux), 8.4 et 8.7 (recouvrements)," +
                    Environment.NewLine +
                    "    dispositions sismiques EN 1998-1 art. 5.4.3.2.2 en option ;" + Environment.NewLine +
                    "  - ACI 318-19 art. 10.6, 25.7.2 et 25.5.5." + Environment.NewLine + Environment.NewLine +
                    "Le resultat reste un avant-projet de ferraillage : il doit etre verifie et " +
                    "valide par l'ingenieur responsable du projet."
            };
            dialog.Show();
            return Result.Succeeded;
        }
    }
}
