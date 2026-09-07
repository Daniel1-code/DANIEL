using System;
using System.Reflection;
using Autodesk.Revit.UI;
using DanCI.Structural.UI.Resources;

namespace DanCI.Structural.App
{
    /// <summary>
    /// Point d'entree du produit : cree le ruban DanCI Structural Studio au demarrage de Revit.
    /// Les panneaux suivent la feuille de route : seuls les modules reellement disponibles
    /// portent une commande active, les autres annoncent clairement leur phase.
    /// </summary>
    public sealed class Application : IExternalApplication
    {
        private const string TabName = "DanCI Structural Studio";

        public Result OnStartup(UIControlledApplication application)
        {
            try
            {
                CreateRibbon(application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                TaskDialog.Show(ProductInfo.Name, "Le ruban n'a pas pu etre cree : " + ex.Message);
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
                // L'onglet existe deja (rechargement du plugin).
            }

            string assemblyPath = Assembly.GetExecutingAssembly().Location;

            RibbonPanel design = application.CreateRibbonPanel(TabName, "Design");

            var columnButton = new PushButtonData(
                "DanCIColumnDesign",
                "Column",
                assemblyPath,
                "DanCI.Structural.App.Commands.ColumnDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les poteaux beton arme.",
                LongDescription =
                    "Selectionnez un ou plusieurs poteaux structurels, verifiez le dimensionnement " +
                    "propose (diametres, nombre de barres, cadres, zones critiques, epingles, " +
                    "recouvrement), la coupe et le quantitatif, puis generez les armatures. " +
                    "La verification de resistance en flexion composee est optionnelle.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var column = design.AddItem(columnButton) as PushButton;
            if (column != null)
            {
                column.LargeImage = IconFactory.CreateColumnRebarIcon(32);
                column.Image = IconFactory.CreateColumnRebarIcon(16);
            }

            var beamButton = new PushButtonData(
                "DanCIBeamDesign",
                "Beam",
                assemblyPath,
                "DanCI.Structural.App.Commands.BeamDesignCommand")
            {
                ToolTip = "Module Poutre - en cours de developpement (phase 2).",
                LongDescription =
                    "Flexion, effort tranchant, ancrages et generation des armatures de poutres " +
                    "selon l'EN 1992-1-1. Ce module est en cours de developpement."
            };
            var beam = design.AddItem(beamButton) as PushButton;
            if (beam != null)
            {
                beam.LargeImage = IconFactory.CreateInfoIcon(32);
                beam.Image = IconFactory.CreateInfoIcon(16);
            }

            RibbonPanel management = application.CreateRibbonPanel(TabName, "Management");

            var aboutButton = new PushButtonData(
                "DanCIAbout",
                "About",
                assemblyPath,
                "DanCI.Structural.App.Commands.AboutCommand")
            {
                ToolTip = "Versions du produit, du moteur de calcul et de la bibliotheque Eurocode."
            };
            var about = management.AddItem(aboutButton) as PushButton;
            if (about != null)
            {
                about.LargeImage = IconFactory.CreateInfoIcon(32);
                about.Image = IconFactory.CreateInfoIcon(16);
            }
        }
    }
}
