using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Beam Design : lecture des poutres, dimensionnement par le moteur, puis
    /// modelisation des armatures dans une transaction unique.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class BeamDesignCommand : IExternalCommand
    {
        private static BeamDesignSettings _lastSettings = new BeamDesignSettings();

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
                TaskDialog.Show(ProductInfo.Name, "Aucune poutre structurelle selectionnee.");
                return Result.Cancelled;
            }

            var beams = new Dictionary<string, RevitBeam>();
            var skipped = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitBeam beam = BeamReader.TryRead(element, out error);
                if (beam == null)
                {
                    skipped.Add(BeamReader.Describe(element) + " : " + error);
                }
                else
                {
                    beams[beam.Data.Id] = beam;
                }
            }

            if (beams.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucune poutre exploitable." + Environment.NewLine + Environment.NewLine +
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
            foreach (RevitBeam beam in beams.Values)
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

            var window = new BeamDesignWindow(
                beams.Values.Select(b => b.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            // Les reglages poses deviennent ceux du mode batch : il ne calcule
            // qu'avec des hypotheses que quelqu'un a reellement validees.
            SessionSettings.Beam = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "DanCI - Armatures de poutres"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (BeamDesignResult result in window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitBeam beam;
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
                    "{0} poutre(s) ont deja ete ferraillees par DanCI Structural Studio", count),
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

        private static void StoreDesignRecord(RevitBeam beam, BeamDesignResult result,
                                              BeamDesignSettings settings, BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Beam Design",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = BeamDesignFingerprint.Compute(result.Beam, settings),
                Summary = string.Format("Travee {0} - {1} - enrobage {2:0} mm - taux {3:0.00}",
                    result.Reinforcement.BottomSpan.Label,
                    result.Reinforcement.TransverseLabel,
                    result.Reinforcement.CoverMm,
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
                "Selectionnez les poutres a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId)).Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<BeamDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} poutre(s) armee(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) longitudinale(s) en {1} lit(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));
            sb.AppendLine(string.Format("{0} nappe(s) de cadres.", outcome.StirrupSets));

            List<BeamDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (BeamDesignResult result in withWarnings)
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
