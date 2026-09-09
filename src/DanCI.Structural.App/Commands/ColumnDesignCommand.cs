using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Revit.Bars;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Storage;
using DanCI.Structural.UI.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Column Design : lecture des poteaux, dimensionnement par le moteur, puis
    /// modelisation des armatures dans une transaction unique.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class ColumnDesignCommand : IExternalCommand
    {
        /// <summary>Derniers reglages utilises, reproposes a l'ouverture suivante.</summary>
        private static ColumnDesignSettings _lastSettings = new ColumnDesignSettings();

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
                selected = PickColumns(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (selected.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, "Aucun poteau structurel selectionne.");
                return Result.Cancelled;
            }

            var columns = new Dictionary<string, RevitColumn>();
            var skipped = new List<string>();
            foreach (Element element in selected)
            {
                string error;
                RevitColumn column = ColumnReader.TryRead(element, out error);
                if (column == null)
                {
                    skipped.Add(ColumnReader.Describe(element) + " : " + error);
                }
                else
                {
                    columns[column.Data.Id] = column;
                }
            }

            if (columns.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucun poteau exploitable." + Environment.NewLine + Environment.NewLine +
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

            // Poteaux deja dimensionnes par le plugin : on propose de remplacer leurs
            // armatures plutot que de les superposer silencieusement.
            var previous = new Dictionary<string, List<ElementId>>();
            foreach (RevitColumn column in columns.Values)
            {
                DesignRecord record = DesignDataStore.Read(column.Frame.Host);
                List<ElementId> alive = DesignDataStore.ExistingReinforcement(document, record);
                if (alive.Count > 0) previous[column.Data.Id] = alive;
            }

            bool replaceExisting = false;
            if (previous.Count > 0)
            {
                ReplaceChoice choice = AskAboutExistingReinforcement(previous);
                if (choice == ReplaceChoice.Cancel) return Result.Cancelled;
                replaceExisting = choice == ReplaceChoice.Replace;
            }

            var window = new ColumnDesignWindow(
                columns.Values.Select(c => c.Data).ToList(), _lastSettings.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastSettings = window.Settings;
            // Les reglages poses deviennent ceux du mode batch : il ne calcule
            // qu'avec des hypotheses que quelqu'un a reellement validees.
            SessionSettings.Column = window.Settings;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "DanCI - Generation des armatures"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var writer = new RebarWriter(document, types);
                foreach (ColumnDesignResult result in window.Results.Where(r => r != null && r.IsValid))
                {
                    RevitColumn column;
                    if (!columns.TryGetValue(result.Column.Id, out column)) continue;

                    if (replaceExisting && previous.ContainsKey(result.Column.Id))
                    {
                        document.Delete(previous[result.Column.Id]);
                        outcome.Messages.Add(string.Format(
                            "{0} : {1} armature(s) precedente(s) remplacee(s).",
                            result.Column.Name, previous[result.Column.Id].Count));
                    }

                    BuildOutcome written = writer.Write(column.Frame, result.Plan, result.Column.Name);
                    outcome.Merge(written);

                    StoreDesignRecord(column, result, window.Settings, written);
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

        private enum ReplaceChoice
        {
            Replace,
            Keep,
            Cancel
        }

        /// <summary>
        /// Demande quoi faire des armatures issues d'un calcul precedent : les remplacer, ou
        /// les conserver et ajouter les nouvelles par-dessus.
        /// </summary>
        private static ReplaceChoice AskAboutExistingReinforcement(
            Dictionary<string, List<ElementId>> previous)
        {
            int bars = previous.Values.Sum(list => list.Count);
            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = string.Format(
                    "{0} poteau(x) ont deja ete ferrailles par DanCI Structural Studio",
                    previous.Count),
                MainContent = string.Format(
                    "{0} ensemble(s) d'armatures issus d'un calcul precedent sont encore " +
                    "presents dans le modele.", bars),
                CommonButtons = TaskDialogCommonButtons.Cancel,
                DefaultButton = TaskDialogResult.CommandLink1
            };
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Remplacer les armatures existantes",
                "Les armatures du calcul precedent sont supprimees, les nouvelles sont creees.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Conserver et ajouter",
                "Les nouvelles armatures s'ajoutent aux anciennes. A n'utiliser qu'en connaissance " +
                "de cause : les armatures se superposeront.");

            TaskDialogResult result = dialog.Show();
            if (result == TaskDialogResult.CommandLink1) return ReplaceChoice.Replace;
            if (result == TaskDialogResult.CommandLink2) return ReplaceChoice.Keep;
            return ReplaceChoice.Cancel;
        }

        /// <summary>
        /// Attache au poteau la trace de son dimensionnement : moteur, norme, empreinte des
        /// donnees et armatures produites. C'est ce qui permettra de detecter un modele
        /// modifie et de mettre le ferraillage a jour.
        /// </summary>
        private static void StoreDesignRecord(RevitColumn column, ColumnDesignResult result,
                                              ColumnDesignSettings settings, BuildOutcome written)
        {
            var record = new DesignRecord
            {
                Module = "DanCI Column Design",
                EngineVersion = ProductInfo.CalculationEngineVersion,
                CodeLabel = result.CodeLabel,
                InputHash = ColumnDesignFingerprint.Compute(result.Column, settings),
                Summary = string.Format("{0} - cadres {1} - enrobage {2:0} mm - taux {3:0.00}",
                    result.Reinforcement.LongitudinalLabel,
                    result.Reinforcement.TransverseLabel,
                    result.Reinforcement.CoverMm,
                    result.MaxUtilization)
            };
            record.RebarIds.AddRange(written.Created);
            DesignDataStore.Write(column.Frame.Host, record);
        }

        private static List<Element> PickColumns(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            // 1. Poteaux deja selectionnes avant le lancement de la commande.
            List<Element> preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                .ToList();
            if (preselected.Count > 0) return preselected;

            // 2. Sinon, selection interactive.
            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new ColumnSelectionFilter(),
                "Selectionnez les poteaux a dimensionner, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId)).Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<ColumnDesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} poteau(x) arme(s).", outcome.ElementsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) longitudinale(s) en {1} lit(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));
            sb.AppendLine(string.Format("{0} nappe(s) de cadres et {1} nappe(s) d'epingles.",
                outcome.StirrupSets, outcome.CrossTieSets));

            List<ColumnDesignResult> withWarnings = results
                .Where(r => r != null && r.Warnings.Count > 0).ToList();
            if (withWarnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A reprendre :");
                foreach (ColumnDesignResult result in withWarnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Column.Name + " : " + warning);
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
