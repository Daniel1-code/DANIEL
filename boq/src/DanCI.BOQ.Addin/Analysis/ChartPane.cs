using System;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.UI.Chart;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Panneau ancrable « DanCI — Diagramme du métré » : il s'affiche DANS l'interface de Revit,
    /// a cote de la vue 3D (a droite par defaut ; l'utilisateur peut le deplacer ou le detacher).
    /// Enregistre au demarrage de Revit (obligatoire pour un panneau ancrable).
    /// </summary>
    internal sealed class ChartPane : IDockablePaneProvider
    {
        public static readonly DockablePaneId Id = new DockablePaneId(new Guid("6C0F3B8E-2A4D-4E7B-9F51-DA7C1B0E3A21"));
        public const string Title = "DanCI — Diagramme du métré";

        public static bool Registered { get; private set; }

        public static void Register(UIControlledApplication application)
        {
            try
            {
                application.RegisterDockablePane(Id, Title, new ChartPane());
                Registered = true;
                BoqLog.Info("Panneau « " + Title + " » enregistré.");
            }
            catch (Exception ex)
            {
                // Sans panneau, le reste de l'extension fonctionne : on journalise et on continue.
                BoqLog.Error("Enregistrement du panneau « " + Title + " » impossible.", ex);
            }
        }

        /// <summary>Affiche le panneau (contexte API Revit).</summary>
        public static string Show(UIApplication app)
        {
            if (!Registered)
            {
                return "Le panneau « Diagramme » n'a pas été enregistré au démarrage de Revit (voir le journal). Redémarrez Revit.";
            }
            try
            {
                DockablePane pane = app.GetDockablePane(Id);
                pane.Show();
                return null;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Affichage du panneau « " + Title + " » impossible.", ex);
                return "Affichage du panneau « Diagramme » impossible : " + ex.Message;
            }
        }

        public void SetupDockablePane(DockablePaneProviderData data)
        {
            data.FrameworkElement = new ViewChartPanel(ViewChartViewModel.Current);
            data.InitialState = new DockablePaneState
            {
                DockPosition = DockPosition.Right,
                // A cote du navigateur de projet quand il est a droite.
                TabBehind = DockablePanes.BuiltInDockablePanes.ProjectBrowser
            };
            data.VisibleByDefault = false;
        }
    }
}
