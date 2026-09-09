using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Stair Design : lecture des volees, dimensionnement par le moteur, puis
    /// modelisation des armatures dans une transaction unique.
    ///
    /// Un escalier Revit porte la geometrie de marche mais n'accepte pas forcement
    /// d'armatures. La commande ne le suppose pas : elle pose la question a l'API, et si
    /// la reponse est non, elle le dit avant le calcul et propose de continuer sans poser
    /// de barres — le calcul, l'apercu, le quantitatif et la note restent utiles.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class StairDesignCommand : IExternalCommand
    {
        private static StairDesignSettings _lastSettings = new StairDesignSettings();
        private static StairData _lastGeometry = new StairData();

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
                selected = PickStairs(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, "Aucune volee selectionnee.");
                return Result.Cancelled;
            }

            var stairs = new Dictionary<string, RevitStair>();
            var skipped = new List<string>();
            var remarks = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitStair stair = StairReader.TryRead(element, _lastGeometry, out error);
                if (stair == null)
                {
                    skipped.Add(StairReader.Describe(element) + " : " + error);
                }
                else
                {
                    stairs[stair.Data.Id] = stair;
                    foreach (string remark in stair.Data.Remarks)
                    {
                        remarks.Add(stair.Data.Name + " : " + remark);
                    }
                }
            }

            if (stairs.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucune volee exploitable." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
            }

            if (remarks.Count > 0)
            {
                var dialog = new TaskDialog(ProductInfo.Name)
                {
                    MainInstruction = "Hypotheses retenues a la lecture des volees",
                    MainContent = string.Join(Environment.NewLine + Environment.NewLine, remarks),
                    CommonButtons = TaskDialogCommonButtons.Ok | TaskDialogCommonButtons.Cancel,
                    DefaultButton = TaskDialogResult.Ok
                };
                if (dialog.Show() != TaskDialogResult.Ok) return Result.Cancelled;
            }

            // Combien de volees peuvent reellement recevoir des barres ? La question est
            // posee avant le calcul, pour que personne ne decouvre la reponse a la fin.
            List<RevitStair> hostable = stairs.Values.Where(s => s.CanHostRebar).ToList();
            if (hostable.Count == 0 && !ConfirmCalculationWithoutModelling(stairs.Values.First()))
            {
                return Result.Cancelled;
            }

            var types = new RebarTypeProvider(document);
            if (hostable.Count > 0 && !types.HasAnyBarType)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Le projet ne contient aucun type de barre d'armature." + Environment.NewLine +
                    "Chargez d'abord une famille d'armatures (Insertion > Charger la famille > " +
                    "Structure > Armature), puis relancez la commande.");
                return Result.Cancelled;
            }

            var previous = new Dictionary<string, List<ElementId>>();
            foreach (RevitStair stair in hostable)
            {
                DesignRecord record = DesignDataStore.Read(stair.Frame.Host);
                List<ElementId> alive = DesignDataStore.ExistingReinforcement(document, record);
                if (alive.Count > 0) previous[stair.Data.Id] = alive;
            }

            bool replaceExisting = false;
            if (previous.Count > 0)
            {
                TaskDialogResult answer = AskAboutExistingReinforcement(previous.Count);
                if (answer == TaskDialogResult.Cancel) return Result.Cancelled;
                replaceExisting = answer == TaskDialogResult.CommandLink1;
            }

            var window = new StairDesignWindow(
                stairs.Values.Select(s => s.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            // Les reglages poses deviennent ceux du mode batch : il ne calcule
            // qu'avec des hypotheses que quelqu'un a reellement validees.
            SessionSettings.Stair = window.Settings;
            if (window.Results.Count > 0 && window.Results[0] != null)
            {
                _lastGeometry = window.Results[0].Stair;
            }
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            if (hostable.Count > 0)
            {
                using (var transaction = new Transaction(document, "DanCI - Armatures d'escalier"))
                {
                    transaction.Start();

                    FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(new WarningSwallower());
                    options.SetClearAfterRollback(true);
                    transaction.SetFailureHandlingOptions(options);

                    var writer = new RebarWriter(document, types);
                    foreach (StairDesignResult result in
                             window.Results.Where(r => r != null && r.IsValid))
                    {
                        RevitStair stair;
                        if (!stairs.TryGetValue(result.Stair.Id, out stair)) continue;
                        if (!stair.CanHostRebar) continue;

                        if (replaceExisting && previous.ContainsKey(result.Stair.Id))
                        {
                            document.Delete(previous[result.Stair.Id]);
                            outcome.Messages.Add(string.Format(
                                "{0} : {1} armature(s) precedente(s) remplacee(s).",
                                result.Stair.Name, previous[result.Stair.Id].Count));
                        }

                        BuildOutcome written = writer.Write(stair.Frame, result.Plan,
                                                            result.Stair.Name);
                        outcome.Merge(written);
                        StoreDesignRecord(stair, result, window.Settings, written);
                    }

                    if (outcome.Created.Count == 0) transaction.RollBack();
                    else transaction.Commit();
                }

                outcome.Messages.AddRange(types.Notes);
            }

            foreach (RevitStair stair in stairs.Values.Where(s => !s.CanHostRebar))
            {
                outcome.Messages.Add(stair.Data.Name + " : " + stair.RebarHostMessage);
            }

            if (outcome.Created.Count > 0)
            {
                uiDocument.Selection.SetElementIds(outcome.Created);
            }

            ShowSummary(outcome, skipped, window.Results, hostable.Count);
            // Un calcul mene sans pose de barres reste un succes : c'est le cas prevu.
            return Result.Succeeded;
        }

        /// <summary>
        /// Prevenir AVANT le calcul, pas apres : l'utilisateur doit savoir tout de suite
        /// que les barres ne seront pas posees, et choisir en connaissance de cause.
        /// </summary>
        private static bool ConfirmCalculationWithoutModelling(RevitStair stair)
        {
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = "Les armatures ne pourront pas etre posees dans le modele",
                MainContent = stair.RebarHostMessage,
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Calculer quand meme",
                "Le dimensionnement, l'apercu, le quantitatif et la note de calcul sont " +
                "produits. Aucune barre n'est creee dans le modele.");
            return dialog.Show() == TaskDialogResult.CommandLink1;
        }

        private static TaskDialogResult AskAboutExistingReinforcement(int count)
        {
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = string.Format(
                    "{0} volee(s) ont deja ete ferraillees par DanCI Structural Studio", count),
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

        private static void StoreDesignRecord(RevitStair stair, StairDesignResult result,
                                              StairDesignSettings settings, BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Stair Design",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = StairDesignFingerprint.Compute(result.Stair, settings),
                Summary = string.Format("{0} - repartition {1} - noeud {2} - taux {3:0.00}",
                    result.Reinforcement.BottomMain.Label,
                    result.Reinforcement.BottomTransverse.Label,
                    result.Reinforcement.HasKneeJoint ? "croise" : "sans objet",
                    result.MaxUtilization)
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(stair.Frame.Host, record);
        }

        private static List<Element> PickStairs(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            (e.Category.Id.Value == (long)BuiltInCategory.OST_Stairs
                             || e.Category.Id.Value == (long)BuiltInCategory.OST_Floors))
                .ToList();
            if (preselected.Count > 0) return preselected;

            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new StairSelectionFilter(),
                "Selectionnez les volees a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId))
                             .Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<StairDesignResult> results, int hostableCount)
        {
            var sb = new StringBuilder();
            if (hostableCount > 0)
            {
                sb.AppendLine(string.Format("{0} volee(s) armee(s).", outcome.ElementsProcessed));
                sb.AppendLine(string.Format("{0} barre(s) en {1} nappe(s).",
                    outcome.LongitudinalBars, outcome.LongitudinalSets));
            }
            else
            {
                sb.AppendLine("Calcul produit sans pose d'armatures dans le modele.");
            }

            List<StairDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (StairDesignResult result in withWarnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Stair.Name + " : " + warning);
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
