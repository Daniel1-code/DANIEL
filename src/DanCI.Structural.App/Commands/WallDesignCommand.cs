using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Wall Design : lecture des voiles, dimensionnement par le moteur, puis
    /// modelisation des nappes dans une transaction unique.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class WallDesignCommand : IExternalCommand
    {
        private static WallDesignSettings _lastSettings = new WallDesignSettings();

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
                selected = PickWalls(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, "Aucun mur selectionne.");
                return Result.Cancelled;
            }

            var walls = new Dictionary<string, RevitWall>();
            var skipped = new List<string>();
            var remarks = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitWall wall = WallReader.TryRead(element, out error);
                if (wall == null)
                {
                    skipped.Add(WallReader.Describe(element) + " : " + error);
                }
                else
                {
                    walls[wall.Data.Id] = wall;
                    foreach (string remark in wall.Data.Remarks)
                    {
                        remarks.Add(wall.Data.Name + " : " + remark);
                    }
                }
            }

            if (walls.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucun voile exploitable." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
            }

            if (remarks.Count > 0)
            {
                // Les hypotheses de lecture sont montrees avant le calcul, pas apres.
                var dialog = new TaskDialog(ProductInfo.Name)
                {
                    MainInstruction = "Hypotheses retenues a la lecture des voiles",
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
            foreach (RevitWall wall in walls.Values)
            {
                DesignRecord record = DesignDataStore.Read(wall.Frame.Host);
                List<ElementId> alive = DesignDataStore.ExistingReinforcement(document, record);
                if (alive.Count > 0) previous[wall.Data.Id] = alive;
            }

            bool replaceExisting = false;
            if (previous.Count > 0)
            {
                TaskDialogResult answer = AskAboutExistingReinforcement(previous.Count);
                if (answer == TaskDialogResult.Cancel) return Result.Cancelled;
                replaceExisting = answer == TaskDialogResult.CommandLink1;
            }

            var window = new WallDesignWindow(
                walls.Values.Select(w => w.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            // Les reglages poses deviennent ceux du mode batch : il ne calcule
            // qu'avec des hypotheses que quelqu'un a reellement validees.
            SessionSettings.Wall = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "DanCI - Armatures de voiles"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (WallDesignResult result in window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitWall wall;
                    if (!walls.TryGetValue(result.Wall.Id, out wall)) continue;

                    if (replaceExisting && previous.ContainsKey(result.Wall.Id))
                    {
                        document.Delete(previous[result.Wall.Id]);
                        outcome.Messages.Add(string.Format(
                            "{0} : {1} armature(s) precedente(s) remplacee(s).",
                            result.Wall.Name, previous[result.Wall.Id].Count));
                    }

                    BuildOutcome written = writer.Write(wall.Frame, result.Plan, result.Wall.Name);
                    outcome.Merge(written);
                    StoreDesignRecord(wall, result, window.Settings, written);
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
                    "{0} voile(s) ont deja ete ferrailles par DanCI Structural Studio", count),
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

        private static void StoreDesignRecord(RevitWall wall, WallDesignResult result,
                                              WallDesignSettings settings, BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Wall Design",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = WallDesignFingerprint.Compute(result.Wall, settings),
                Summary = string.Format("{0} - lambda {1:0.0} - enrobage {2:0} mm - taux {3:0.00}",
                    result.Reinforcement.VerticalLabel,
                    result.SlendernessRatio,
                    result.Reinforcement.CoverMm,
                    result.MaxUtilization)
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(wall.Frame.Host, record);
        }

        private static List<Element> PickWalls(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            e.Category.Id.Value == (long)BuiltInCategory.OST_Walls)
                .ToList();
            if (preselected.Count > 0) return preselected;

            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new WallSelectionFilter(),
                "Selectionnez les voiles a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId))
                             .Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<WallDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} voile(s) arme(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) en {1} nappe(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));
            if (outcome.CrossTieSets > 0)
            {
                sb.AppendLine(string.Format("{0} nappe(s) d'epingles de liaison.", outcome.CrossTieSets));
            }

            List<WallDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (WallDesignResult result in withWarnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Wall.Name + " : " + warning);
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
