using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;
using ArmaturesPoteaux.Core;
using ArmaturesPoteaux.RevitOps;
using ArmaturesPoteaux.UI;

namespace ArmaturesPoteaux.Commands
{
    /// <summary>
    /// Commande principale : selection des poteaux, dimensionnement puis modelisation
    /// des armatures dans une seule transaction.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public class GenerateColumnRebarCommand : IExternalCommand
    {
        /// <summary>Derniers parametres utilises, reproposes a l'ouverture suivante.</summary>
        private static DesignInput _lastInput = new DesignInput();

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
                message = "Ce plugin fonctionne dans un projet, pas dans l'editeur de familles.";
                return Result.Failed;
            }

            List<Element> columns;
            try
            {
                columns = PickColumns(uiDocument);
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                return Result.Cancelled;
            }

            if (columns.Count == 0)
            {
                TaskDialog.Show("Armatures de poteaux",
                    "Aucun poteau structurel selectionne.");
                return Result.Cancelled;
            }

            var geometries = new List<ColumnGeometry>();
            var skipped = new List<string>();
            foreach (Element column in columns)
            {
                string error;
                ColumnGeometry geometry = ColumnInspector.TryExtract(column, out error);
                if (geometry == null)
                {
                    skipped.Add(ColumnInspector.DescribeElement(column) + " : " + error);
                }
                else
                {
                    geometries.Add(geometry);
                }
            }

            if (geometries.Count == 0)
            {
                TaskDialog.Show("Armatures de poteaux",
                    "Aucun poteau exploitable." + Environment.NewLine + Environment.NewLine +
                    string.Join(Environment.NewLine, skipped));
                return Result.Cancelled;
            }

            var types = new RebarTypeProvider(document);
            if (!types.HasAnyBarType)
            {
                TaskDialog.Show("Armatures de poteaux",
                    "Le projet ne contient aucun type de barre d'armature." + Environment.NewLine +
                    "Chargez d'abord une famille d'armatures (Insertion > Charger la famille > " +
                    "Structure > Armature), puis relancez la commande.");
                return Result.Cancelled;
            }

            var window = new MainWindow(geometries, _lastInput.Clone());
            new System.Windows.Interop.WindowInteropHelper(window)
            {
                Owner = commandData.Application.MainWindowHandle
            };

            bool? dialogResult = window.ShowDialog();
            _lastInput = window.Input;
            if (dialogResult != true) return Result.Cancelled;

            var outcome = new BuildOutcome();
            using (var transaction = new Transaction(document, "Generation des armatures de poteaux"))
            {
                transaction.Start();

                FailureHandlingOptions options = transaction.GetFailureHandlingOptions();
                options.SetFailuresPreprocessor(new WarningSwallower());
                options.SetClearAfterRollback(true);
                transaction.SetFailureHandlingOptions(options);

                var builder = new ColumnRebarBuilder(document, types, window.Input);
                foreach (DesignResult design in window.Results.Where(r => r.IsValid))
                {
                    outcome.Merge(builder.Build(design));
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

        private static List<Element> PickColumns(UIDocument uiDocument)
        {
            Document document = uiDocument.Document;

            // 1. Poteaux deja selectionnes avant le lancement de la commande.
            var preselected = uiDocument.Selection.GetElementIds()
                .Select(document.GetElement)
                .Where(e => e != null && e.Category != null &&
                            e.Category.Id.Value == (long)BuiltInCategory.OST_StructuralColumns)
                .ToList();
            if (preselected.Count > 0) return preselected;

            // 2. Sinon, selection interactive.
            IList<Reference> references = uiDocument.Selection.PickObjects(
                ObjectType.Element, new ColumnSelectionFilter(),
                "Selectionnez les poteaux a armer, puis validez (Terminer)");
            return references.Select(r => document.GetElement(r.ElementId)).Where(e => e != null).ToList();
        }

        private static void ShowSummary(BuildOutcome outcome, List<string> skipped,
                                        List<DesignResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} poteau(x) arme(s).", outcome.ColumnsProcessed));
            sb.AppendLine(string.Format("{0} barre(s) longitudinale(s) en {1} lit(s).",
                outcome.LongitudinalBars, outcome.LongitudinalSets));
            sb.AppendLine(string.Format("{0} nappe(s) de cadres et {1} nappe(s) d'epingles.",
                outcome.StirrupSets, outcome.CrossTieSets));

            var warnings = results.Where(r => r.Warnings.Count > 0).ToList();
            if (warnings.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("A verifier :");
                foreach (DesignResult result in warnings)
                {
                    foreach (string warning in result.Warnings)
                    {
                        sb.AppendLine("  - " + result.Geometry.HostName + " : " + warning);
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

            TaskDialog.Show("Armatures de poteaux", sb.ToString());
        }
    }
}
