using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.GradeBeam;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Grade Beam : lecture des longrines, dimensionnement par le moteur, puis
    /// modelisation des armatures dans une transaction unique.
    ///
    /// Une longrine est modelisee dans Revit comme une poutre : c'est le lancement de
    /// cette commande, et non une devinette, qui declare qu'il s'agit d'un element de
    /// fondation.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class GradeBeamDesignCommand : IExternalCommand
    {
        private static GradeBeamDesignSettings _lastSettings = new GradeBeamDesignSettings();

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            UIDocument uiDocument = commandData.Application.ActiveUIDocument;
            if (uiDocument == null)
            {
                message = "Aucun document actif.";
                return Result.Failed;
            }

            Document document = uiDocument.Document;
            if (document.IsFamilyDocument)
            {
                message = "DanCI Structural Studio fonctionne dans un projet, pas dans l'editeur " +
                          "de familles.";
                return Result.Failed;
            }

            List<Element> selected;
            try
            {
                selected = PickBeams(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, "Aucune longrine selectionnee.");
                return Result.Cancelled;
            }

            var beams = new Dictionary<string, RevitGradeBeam>();
            var skipped = new List<string>();
            var remarks = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitGradeBeam beam = GradeBeamReader.TryRead(element, out error);
                if (beam == null)
                {
                    skipped.Add(GradeBeamReader.Describe(element) + " : " + error);
                }
                else
                {
                    beams[beam.Data.Id] = beam;
                    foreach (string remark in beam.Data.Remarks)
                    {
                        remarks.Add(beam.Data.Name + " : " + remark);
                    }
                }
            }

            if (beams.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucune longrine exploitable." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
            }

            if (remarks.Count > 0)
            {
                var dialog = new TaskDialog(ProductInfo.Name)
                {
                    MainInstruction = "Hypotheses retenues a la lecture des longrines",
                    MainContent = string.Join(Environment.NewLine + Environment.NewLine, remarks),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok
                };
                if (dialog.Show() != TaskDialogResult.Ok) return Result.Cancelled;
            }

            var types = new RebarTypeProvider(document);
            if (!types.HasAnyBarType)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Le projet ne contient aucun type de barre d'armature." + Environment.NewLine +
                    "Chargez d'abord une famille d'armatures (Insertion > Charger la famille > " +
                    "Structure > Armature), puis relancez la commande.");
                return Result.Cancelled;
            }

            var previous = new Dictionary<string, List<ElementId>>();
            foreach (RevitGradeBeam beam in beams.Values)
            {
                DesignRecord record = DesignDataStore.Read(beam.Frame.Host);
                List<ElementId> alive = DesignDataStore.ExistingReinforcement(document, record);
                if (alive.Count > 0) previous[beam.Data.Id] = alive;
            }

            bool replaceExisting = false;
            if (previous.Count > 0)
            {
                TaskDialogResult answer = AskAboutExistingReinforcement(previous.Count);
                if (answer == TaskDialogResult.Cancel) return Result.Cancelled;
                replaceExisting = answer == TaskDialogResult.CommandLink1;
            }

            var window = new GradeBeamDesignWindow(
                beams.Values.Select(b => b.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "DanCI - Armatures de longrines"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (GradeBeamDesignResult result in
                         window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitGradeBeam beam;
                    if (!beams.TryGetValue(result.Beam.Id, out beam)) continue;

                    if (replaceExisting && previous.ContainsKey(result.Beam.Id))
                    {
                        document.Delete(previous[result.Beam.Id]);
                        outcome.Messages.Add(string.Format(
                            "{0} : {1} armature(s) precedente(s) remplacee(s).",
                            result.Beam.Name, previous[result.Beam.Id].Count));
                    }

                    BuildOutcome written = writer.Write(beam.Frame, result.Plan, result.Beam.Name);
                    outcome.Merge(written);
                    StoreDesignRecord(beam, result, window.Settings, written);
                }

                if (outcome.Created.Count == 0)
                {
                    transaction.RollBack();
                }
                else
                {
                    transaction.Commit();
                }
            }

            outcome.Messages.AddRange(types.Notes);
            if (outcome.Created.Count > 0)
            {
                uiDocument.Selection.SetElementIds(outcome.Created);
            }

            ShowSummary(outcome, skipped, window.Results);
            return outcome.Created.Count > 0 ? Result.Succeeded : Result.Failed;
        }

        private static TaskDialogResult AskAboutExistingReinforcement(int count)
        {
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = string.Format(
                    "{0} longrine(s) ont deja ete ferraillees par DanCI Structural Studio", count),
                MainContent = "Des armatures issues d'un calcul precedent sont encore presentes " +
                              "dans le modele.",
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Remplacer les armatures existantes",
                "Les armatures du calcul precedent sont supprimees, les nouvelles sont creees.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Conserver et ajouter",
                "Les nouvelles armatures s'ajoutent aux anciennes et se superposeront.");
            return dialog.Show();
        }

        private static void StoreDesignRecord(RevitGradeBeam beam, GradeBeamDesignResult result,
                                              GradeBeamDesignSettings settings,
                                              BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Grade Beam",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = GradeBeamDesignFingerprint.Compute(result.Beam, settings),
                Summary = string.Format("{0} - {1} - liaison {2} - taux {3:0.00}",
                    result.Reinforcement.LongitudinalLabel,
                    result.Reinforcement.TransverseLabel,
                    result.TieForceKn > 0
                        ? string.Format("+-{0:0.0} kN", result.TieForceKn) : "aucune",
                    result.MaxUtilization)
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(beam.Frame.Host, record);
        }

        private static List<Element> PickBeams(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFraming)
                .ToList();
            if (preselected.Count > 0) return preselected;

            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new BeamSelectionFilter(),
                "Selectionnez les longrines a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId))
                             .Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<GradeBeamDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} longrine(s) armee(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) longitudinale(s) en {1} nappe(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));
            sb.AppendLine(string.Format("{0} nappe(s) de cadres.", outcome.StirrupSets));

            List<GradeBeamDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (GradeBeamDesignResult result in withWarnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Beam.Name + " : " + warning);
                    }
                }
            }

            if (outcome.Messages.Count > 0)
            {
                sb.AppendLine();
                foreach (string note in outcome.Messages) sb.AppendLine("  - " + note);
            }

            if (skipped.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Elements ignores :");
                foreach (string item in skipped) sb.AppendLine("  - " + item);
            }

            if (outcome.Errors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Erreurs :");
                foreach (string error in outcome.Errors) sb.AppendLine("  - " + error);
            }

            TaskDialog.Show(ProductInfo.Name, sb.ToString());
        }
    }
}
