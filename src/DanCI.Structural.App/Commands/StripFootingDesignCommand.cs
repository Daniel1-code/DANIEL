using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Strip Footing : lecture des semelles filantes, dimensionnement par le moteur,
    /// puis modelisation des armatures dans une transaction unique.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class StripFootingDesignCommand : IExternalCommand
    {
        private static StripFootingDesignSettings _lastSettings = new StripFootingDesignSettings();

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
                selected = PickFootings(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, "Aucune semelle filante selectionnee.");
                return Result.Cancelled;
            }

            var footings = new Dictionary<string, RevitStripFooting>();
            var skipped = new List<string>();
            var remarks = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitStripFooting footing = StripFootingReader.TryRead(element, out error);
                if (footing == null)
                {
                    skipped.Add(StripFootingReader.Describe(element) + " : " + error);
                }
                else
                {
                    footings[footing.Data.Id] = footing;
                    foreach (string remark in footing.Data.Remarks)
                    {
                        remarks.Add(footing.Data.Name + " : " + remark);
                    }
                }
            }

            if (footings.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucune semelle filante exploitable." + Environment.NewLine +
                    Environment.NewLine + string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
            }

            if (remarks.Count > 0)
            {
                // Les hypotheses de lecture sont montrees avant le calcul, pas apres.
                var dialog = new TaskDialog(ProductInfo.Name)
                {
                    MainInstruction = "Hypotheses retenues a la lecture des semelles",
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
            foreach (RevitStripFooting footing in footings.Values)
            {
                DesignRecord record = DesignDataStore.Read(footing.Frame.Host);
                List<ElementId> alive = DesignDataStore.ExistingReinforcement(document, record);
                if (alive.Count > 0) previous[footing.Data.Id] = alive;
            }

            bool replaceExisting = false;
            if (previous.Count > 0)
            {
                TaskDialogResult answer = AskAboutExistingReinforcement(previous.Count);
                if (answer == TaskDialogResult.Cancel) return Result.Cancelled;
                replaceExisting = answer == TaskDialogResult.CommandLink1;
            }

            var window = new StripFootingDesignWindow(
                footings.Values.Select(f => f.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document,
                       "DanCI - Armatures de semelles filantes"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (StripFootingDesignResult result in
                         window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitStripFooting footing;
                    if (!footings.TryGetValue(result.Footing.Id, out footing)) continue;

                    if (replaceExisting && previous.ContainsKey(result.Footing.Id))
                    {
                        document.Delete(previous[result.Footing.Id]);
                        outcome.Messages.Add(string.Format(
                            "{0} : {1} armature(s) precedente(s) remplacee(s).",
                            result.Footing.Name, previous[result.Footing.Id].Count));
                    }

                    BuildOutcome written = writer.Write(footing.Frame, result.Plan,
                                                        result.Footing.Name);
                    outcome.Merge(written);
                    StoreDesignRecord(footing, result, window.Settings, written);
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
                    "{0} semelle(s) ont deja ete ferraillees par DanCI Structural Studio", count),
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

        private static void StoreDesignRecord(RevitStripFooting footing,
                                              StripFootingDesignResult result,
                                              StripFootingDesignSettings settings,
                                              BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Strip Footing",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = StripFootingDesignFingerprint.Compute(result.Footing, settings),
                Summary = string.Format("{0} - enrobage {1:0} mm - taux {2:0.00}{3}",
                    result.Reinforcement.TransverseLabel,
                    result.Reinforcement.CoverMm,
                    result.MaxUtilization,
                    result.Reinforcement.TransverseNeedsHook ? " - crochets" : "")
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(footing.Frame.Host, record);
        }

        private static List<Element> PickFootings(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e is WallFoundation)
                .ToList();
            if (preselected.Count > 0) return preselected;

            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new StripFootingSelectionFilter(),
                "Selectionnez les semelles filantes a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId))
                             .Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<StripFootingDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} semelle(s) armee(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) en {1} nappe(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));

            List<StripFootingDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (StripFootingDesignResult result in withWarnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Footing.Name + " : " + warning);
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
