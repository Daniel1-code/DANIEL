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
                ToolTip = "Dimensionne et ferraille les poutres beton arme.",
                LongDescription =
                    "Selectionnez une ou plusieurs poutres structurelles, saisissez les moments " +
                    "et efforts tranchants de calcul, verifiez le dimensionnement propose " +
                    "(flexion, effort tranchant, cadres a espacement variable, chapeaux, " +
                    "ancrages) puis generez les armatures. La table collaborante se declare " +
                    "dans la fenetre : la dalle n'appartient pas a l'element poutre.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var beam = design.AddItem(beamButton) as PushButton;
            if (beam != null)
            {
                beam.LargeImage = IconFactory.CreateBeamIcon(32);
                beam.Image = IconFactory.CreateBeamIcon(16);
            }

            var footingButton = new PushButtonData(
                "DanCIFootingDesign",
                "Footing",
                assemblyPath,
                "DanCI.Structural.App.Commands.FootingDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les semelles isolees.",
                LongDescription =
                    "Selectionnez une ou plusieurs semelles isolees, saisissez la charge en pied " +
                    "de poteau et la contrainte admissible du sol, verifiez le dimensionnement " +
                    "propose (capacite portante EN 1997-1, non-soulevement, glissement, " +
                    "renversement, flexion des consoles, poinconnement EN 1992-1-1 art. 6.4, " +
                    "effort tranchant) puis generez les nappes et les attentes. La contrainte " +
                    "admissible vient de l'etude geotechnique : le plugin ne la calcule pas.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var footing = design.AddItem(footingButton) as PushButton;
            if (footing != null)
            {
                footing.LargeImage = IconFactory.CreateFootingIcon(32);
                footing.Image = IconFactory.CreateFootingIcon(16);
            }

            var slabButton = new PushButtonData(
                "DanCISlabDesign",
                "Slab",
                assemblyPath,
                "DanCI.Structural.App.Commands.SlabDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les dalles pleines portant dans un sens.",
                LongDescription =
                    "Selectionnez un ou plusieurs planchers, verifiez le sens porteur lu dans " +
                    "le modele, saisissez les charges ou les moments, puis generez les nappes. " +
                    "Le calcul porte sur une bande de 1 m et couvre la flexion, l'effort " +
                    "tranchant, la fleche par l'elancement limite (art. 7.4.2) et la maitrise " +
                    "de la fissuration (art. 7.3.3). Sur une dalle, c'est presque toujours la " +
                    "fleche qui gouverne, pas la resistance.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var slab = design.AddItem(slabButton) as PushButton;
            if (slab != null)
            {
                slab.LargeImage = IconFactory.CreateSlabIcon(32);
                slab.Image = IconFactory.CreateSlabIcon(16);
            }

            var wallButton = new PushButtonData(
                "DanCIWallDesign",
                "Wall",
                assemblyPath,
                "DanCI.Structural.App.Commands.WallDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les voiles en beton arme.",
                LongDescription =
                    "Selectionnez un ou plusieurs murs structurels en beton, declarez les " +
                    "conditions de maintien et les sollicitations, puis generez les deux " +
                    "nappes. Le voile est verifie a deux echelles : hors plan comme un poteau " +
                    "de section 1 000 x t, avec elancement et second ordre ; dans son plan " +
                    "comme une console verticale, avec l'effort tranchant de contreventement " +
                    "et les barres de rive.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var wall = design.AddItem(wallButton) as PushButton;
            if (wall != null)
            {
                wall.LargeImage = IconFactory.CreateWallIcon(32);
                wall.Image = IconFactory.CreateWallIcon(16);
            }

            var stripButton = new PushButtonData(
                "DanCIStripFootingDesign",
                "Strip",
                assemblyPath,
                "DanCI.Structural.App.Commands.StripFootingDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les semelles filantes sous voile.",
                LongDescription =
                    "Selectionnez une ou plusieurs semelles filantes, saisissez la charge par " +
                    "metre courant et la contrainte admissible du sol, puis generez les " +
                    "armatures. Une semelle filante ne poinconne pas : la charge du voile " +
                    "arrive repartie sur toute sa longueur. Elle est presque toujours pilotee " +
                    "par la section minimale, et son debord court impose souvent un crochet " +
                    "d'extremite sur les armatures transversales.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var strip = design.AddItem(stripButton) as PushButton;
            if (strip != null)
            {
                strip.LargeImage = IconFactory.CreateStripFootingIcon(32);
                strip.Image = IconFactory.CreateStripFootingIcon(16);
            }

            var gradeButton = new PushButtonData(
                "DanCIGradeBeamDesign",
                "Grade Beam",
                assemblyPath,
                "DanCI.Structural.App.Commands.GradeBeamDesignCommand")
            {
                ToolTip = "Dimensionne et ferraille les longrines entre fondations.",
                LongDescription =
                    "Selectionnez une ou plusieurs longrines, declarez ce sur quoi elles " +
                    "reposent et, en zone sismique, la classe de sol. Une longrine n'est pas " +
                    "une poutre posee bas : c'est d'abord un tirant, son effort est axial et " +
                    "alterne, ses deux nappes filent d'un appui a l'autre, et l'article " +
                    "5.8.2(5) de l'EN 1998-1 impose 0,4 % en haut comme en bas.",
                AvailabilityClassName = "DanCI.Structural.App.Commands.DocumentAvailability"
            };
            var grade = design.AddItem(gradeButton) as PushButton;
            if (grade != null)
            {
                grade.LargeImage = IconFactory.CreateGradeBeamIcon(32);
                grade.Image = IconFactory.CreateGradeBeamIcon(16);
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
