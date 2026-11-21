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
