using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Addin.Analysis;
using DanCI.BOQ.Core.Diagnostics;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>Ouvre la fenetre non modale « Métré &amp; BOQ ». Aucune modification du modele.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ShowBoqCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                BoqHost.ShowWindow(commandData.Application);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Ouverture de la fenêtre BOQ en échec.", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>Ouvre la fenetre sur l'onglet « Tableau de bord ». Lecture seule.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ShowDashboardCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ShowTab(commandData, ref message, UI.ViewModels.BoqWindowViewModel.DashboardTab);

        internal static Result ShowTab(ExternalCommandData commandData, ref string message, int tab)
        {
            try
            {
                BoqHost.ShowWindow(commandData.Application, tab);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Ouverture de la fenêtre BOQ en échec.", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }

    /// <summary>Ouvre la fenetre sur l'onglet « Paramètres du projet ». Aucune modification du modele.</summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class ShowSettingsCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements) =>
            ShowDashboardCommand.ShowTab(commandData, ref message, UI.ViewModels.BoqWindowViewModel.SettingsTab);
    }
}
