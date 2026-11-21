using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.RevitAdapter;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>
    /// A propos : version de l'extension, Revit et runtime .NET reellement charges.
    /// Lecture seule, aucune transaction.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class AboutCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            try
            {
                HostInfo host = RevitHostReader.Read(commandData.Application.Application);
                string report = string.Join(Environment.NewLine, RuntimeReport.Build(host));
                BoqLog.Info("Commande À propos." + Environment.NewLine + report);

                var dialog = new TaskDialog("À propos — " + ProductInfo.Name)
                {
                    MainInstruction = ProductInfo.Name + " " + ProductInfo.ApplicationVersion,
                    MainContent =
                        report + Environment.NewLine + Environment.NewLine +
                        ProductInfo.Publisher + Environment.NewLine +
                        ProductInfo.Author,
                    ExpandedContent =
                        "Assembly : " + Assembly.GetExecutingAssembly().Location + Environment.NewLine +
                        "Journal  : " + BoqLog.CurrentFile,
                    FooterText =
                        "Un BOQ produit par cette extension doit être vérifié par l'ingénieur ou " +
                        "le métreur responsable avant diffusion.",
                    CommonButtons = TaskDialogCommonButtons.Close,
                    DefaultButton = TaskDialogResult.Close
                };
                dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ouvrir le dossier des journaux");

                if (dialog.Show() == TaskDialogResult.CommandLink1)
                {
                    Directory.CreateDirectory(BoqLog.Directory);
                    Process.Start(new ProcessStartInfo(BoqLog.Directory) { UseShellExecute = true });
                }
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Commande À propos en échec.", ex);
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
