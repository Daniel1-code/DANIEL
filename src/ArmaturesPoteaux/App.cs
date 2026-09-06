using System;
using System.Reflection;
using Autodesk.Revit.UI;
using ArmaturesPoteaux.UI;

namespace ArmaturesPoteaux
{
    /// <summary>
    /// Point d'entree du plugin : cree l'onglet et les boutons du ruban au demarrage de Revit.
    /// </summary>
    public class App : IExternalApplication
    {
        private const string TabName = "Beton arme";
        private const string PanelName = "Poteaux";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                CreateRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Armatures de poteaux",
                    "Le ruban n'a pas pu etre cree : " + ex.Message);
                return Result.Failed;
            }
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            return Result.Succeeded;
        }

        private static void CreateRibbon(UIControlledApplication application)
        {
            try
            {
                application.CreateRibbonTab(TabName);
            }
            catch (Autodesk.Revit.Exceptions.ArgumentException)
            {
                // L'onglet existe deja (autre plugin ou rechargement).
            }

            RibbonPanel panel = application.CreateRibbonPanel(TabName, PanelName);
            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            var mainButtonData = new PushButtonData(
                "ArmaturesPoteauxGenerate",
                "Armer\nles poteaux",
                assemblyPath,
                "ArmaturesPoteaux.Commands.GenerateColumnRebarCommand")
            {
                ToolTip = "Genere les armatures des poteaux beton selon l'Eurocode 2 ou l'ACI 318.",
                LongDescription =
                    "Selectionnez un ou plusieurs poteaux structurels, verifiez le dimensionnement " +
                    "propose (diametres, nombre de barres, cadres, zones critiques, epingles et " +
                    "longueur de recouvrement) puis generez les armatures. La note de calcul " +
                    "justifie chaque valeur retenue et peut etre exportee.",
                AvailabilityClassName = "ArmaturesPoteaux.Commands.DocumentAvailability"
            };

            var mainButton = panel.AddItem(mainButtonData) as PushButton;
            if (mainButton != null)
            {
                mainButton.LargeImage = IconFactory.CreateColumnRebarIcon(32);
                mainButton.Image = IconFactory.CreateColumnRebarIcon(16);
            }

            panel.AddSeparator();

            var aboutButtonData = new PushButtonData(
                "ArmaturesPoteauxAbout",
                "A propos",
                assemblyPath,
                "ArmaturesPoteaux.Commands.AboutCommand")
            {
                ToolTip = "Version du plugin et regles de calcul appliquees."
            };

            var aboutButton = panel.AddItem(aboutButtonData) as PushButton;
            if (aboutButton != null)
            {
                aboutButton.Image = IconFactory.CreateInfoIcon(16);
                aboutButton.LargeImage = IconFactory.CreateInfoIcon(32);
            }
        }
    }
}
