using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Isolated Footing Design : lecture des semelles, dimensionnement par le moteur,
    /// puis modelisation des nappes et des attentes dans une transaction unique.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class FootingDesignCommand : IExternalCommand
    {
        private static FootingDesignSettings _lastSettings = new FootingDesignSettings();

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
                TaskDialog.Show(ProductInfo.Name, "Aucune fondation structurelle selectionnee.");
                return Result.Cancelled;
            }

            var footings = new Dictionary<string, RevitFooting>();
            var skipped = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitFooting footing = FootingReader.TryRead(element, out error);
                if (footing == null)
                {
                    skipped.Add(FootingReader.Describe(element) + " : " + error);
                }
                else
                {
                    footings[footing.Data.Id] = footing;
                }
            }

            if (footings.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucune semelle exploitable." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
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
            foreach (RevitFooting footing in footings.Values)
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

            var window = new FootingDesignWindow(
                footings.Values.Select(f => f.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            // Les reglages poses deviennent ceux du mode batch : il ne calcule
            // qu'avec des hypotheses que quelqu'un a reellement validees.
            SessionSettings.IsolatedFooting = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "DanCI - Armatures de semelles"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (FootingDesignResult result in window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitFooting footing;
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

        private static void StoreDesignRecord(RevitFooting footing, FootingDesignResult result,
                                              FootingDesignSettings settings, BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Isolated Footing",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = FootingDesignFingerprint.Compute(result.Footing, settings),
                Summary = string.Format("{0} - attentes {1} - enrobage {2:0} mm - taux {3:0.00}",
                    result.Reinforcement.BottomLabel,
                    result.Reinforcement.StarterLabel,
                    result.Reinforcement.CoverMm,
                    result.MaxUtilization)
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(footing.Frame.Host, record);
        }

        private static List<Element> PickFootings(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralFoundation)
                .ToList();
            if (preselected.Count > 0) return preselected;

            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new FootingSelectionFilter(),
                "Selectionnez les semelles a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId))
                             .Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<FootingDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} semelle(s) armee(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) en {1} nappe(s) et attente(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));

            List<FootingDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (FootingDesignResult result in withWarnings)
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
