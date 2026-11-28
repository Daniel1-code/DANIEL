using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.BOQ.Addin.Analysis;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.UI.Progress;
using DanCI.BOQ.UI.ViewModels;

namespace DanCI.BOQ.Addin.Commands
{
    /// <summary>
    /// Diagnostic du metre : analyse du modele entier en LECTURE SEULE, puis ecriture a cote du
    /// projet du metre brut, des lignes du BOQ et du rapport d'audit (CSV, Excel francais).
    /// C'est l'outil de controle contre un metre manuel, element par element.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    public sealed class DiagnosticCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uidoc = commandData.Application.ActiveUIDocument;
            if (uidoc == null || uidoc.Document.IsFamilyDocument)
            {
                message = "Ouvrez un projet Revit (pas une famille).";
                return Result.Cancelled;
            }

            try
            {
                AnalysisOutcome outcome;
                using (ProgressDialog progress = ProgressDialog.Start("DanCI — diagnostic du métré"))
                {
                    outcome = AnalysisService.Run(uidoc, new AnalysisScope { Mode = ScopeMode.Modele }, null, progress);
                }
                List<string> paths = AnalysisService.WriteCsv(uidoc.Document, outcome);
                ShowSummary(uidoc.Document, outcome, paths);
                return Result.Succeeded;
            }
            catch (OperationCanceledException)
            {
                BoqLog.Info("Diagnostic annulé par l'utilisateur.");
                TaskDialog.Show("Diagnostic du métré", "Diagnostic annulé : aucun fichier écrit.");
                return Result.Cancelled;
            }
            catch (Exception ex)
            {
                BoqLog.Error("Diagnostic du métré en échec.", ex);
                message = ex.Message + Environment.NewLine + "Journal : " + BoqLog.CurrentFile;
                return Result.Failed;
            }
        }

        private static void ShowSummary(Document doc, AnalysisOutcome outcome, List<string> paths)
        {
            CultureInfo fr = CultureInfo.GetCultureInfo("fr-FR");
            var text = new StringBuilder();
            text.AppendLine("Quantités extraites (modèle hôte entier, toutes phases, sans liens) :");
            foreach (var g in outcome.Quantities.GroupBy(q => new { q.Kind, q.Unit }).OrderBy(g => g.Key.Kind, StringComparer.Ordinal))
            {
                int estimated = g.Count(q => q.Reliability != Reliability.Measured);
                text.AppendLine("  • " + g.Key.Kind + " : " + g.Count() + " quantité(s), total " +
                                g.Sum(q => q.Value).ToString("N3", fr) + " " + g.Key.Unit.Symbol() +
                                (estimated > 0 ? "  (dont non mesurées : " + estimated + ")" : string.Empty));
            }
            if (outcome.Quantities.Count == 0)
            {
                text.AppendLine("  (aucune)");
            }
            text.AppendLine();
            text.AppendLine(outcome.Boq != null
                ? "BOQ : " + outcome.Boq.AllLines.Count() + " ligne(s), dont " + outcome.Boq.AllLines.Count(l => l.IsUnclassified) + " non classée(s)."
                : "BOQ non calculé : " + outcome.Failure);
            if (outcome.Audit != null)
            {
                text.AppendLine("Audit : " + outcome.Audit.ErrorCount + " erreur(s), " + outcome.Audit.WarningCount + " avertissement(s).");
            }
            text.AppendLine("Durée : " + outcome.Duration.TotalSeconds.ToString("0.0", fr) + " s.");

            var dialog = new TaskDialog("Diagnostic du métré — " + doc.Title)
            {
                MainInstruction = "Métré brut écrit (" + outcome.Quantities.Count + " quantités)",
                MainContent = text.ToString(),
                ExpandedContent = "Fichiers :" + Environment.NewLine + string.Join(Environment.NewLine, paths.Select(p => "  " + p)) +
                                  Environment.NewLine + Environment.NewLine + "Lecture seule : le modèle n'a pas été modifié.",
                FooterText = "Les quantités sont à contrôler contre un métré manuel avant tout usage.",
                CommonButtons = TaskDialogCommonButtons.Close,
                DefaultButton = TaskDialogResult.Close
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1, "Ouvrir le dossier des fichiers");
            if (dialog.Show() == TaskDialogResult.CommandLink1 && paths.Count > 0)
            {
                Process.Start(new ProcessStartInfo(Path.GetDirectoryName(paths[0])) { UseShellExecute = true });
            }
        }
    }
}
