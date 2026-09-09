using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Documentation.Dashboard;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Documentation.Reports;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Engine.Wall;
using DanCI.Structural.Revit.Geometry;
using DanCI.Structural.Revit.Selection;
using DanCI.Structural.Revit.Views;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// DanCI Batch : verifie d'un coup tous les elements structurels de la VUE ACTIVE.
    ///
    /// CE QU'IL FAIT. Il collecte, classe, calcule et rend une synthese de projet : ce qui
    /// ne passe pas, pourquoi — par article, et non element par element — et combien.
    ///
    /// CE QU'IL NE FAIT PAS, ET C'EST DELIBERE.
    ///
    /// - IL NE POSE AUCUNE ARMATURE. Un lot entier ferraille en une commande, sans qu'on
    ///   ait regarde une seule coupe, est exactement la facon dont un modele se remplit de
    ///   barres que personne n'a validees. Le batch VERIFIE ; le ferraillage se pose module
    ///   par module, apres lecture.
    /// - IL N'INVENTE AUCUNE CHARGE. Il ne traite que les familles dont la fenetre a ete
    ///   ouverte et validee cette session. Les autres sont nommees et laissees de cote :
    ///   calculer un projet entier avec des charges par defaut produirait un resultat
    ///   d'apparence normale et faux sur tout le lot d'un seul coup.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    public sealed class BatchDesignCommand : IExternalCommand
    {
        /// <summary>Le dernier lot calcule, pour pouvoir retirer ses couleurs.</summary>
        private static ColourPlan _lastPlan;

        public Result Execute(ExternalCommandData commandData, ref string message,
                              ElementSet elements)
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
                message = "DanCI Structural Studio fonctionne dans un projet, pas dans "
                          + "l'editeur de familles.";
                return Result.Failed;
            }

            View view = document.ActiveView;
            BatchScope scope = BatchCollector.CollectFromView(document, view);

            if (scope.Candidates.Count == 0 && scope.Refused.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name,
                    "Aucun element structurel dans la vue active." + Environment.NewLine
                    + Environment.NewLine
                    + "Le lot porte sur CE QUE LA VUE MONTRE, et non sur le modele entier : "
                    + "un lot qu'on n'a pas choisi n'est pas un lot qu'on peut verifier. "
                    + "Ouvrez la vue qui contient les elements a verifier.");
                return Result.Cancelled;
            }

            var designed = new List<DesignedElement>();
            var skippedKinds = new List<string>();
            var readErrors = new List<string>();

            foreach (var group in scope.Candidates.GroupBy(c => c.Kind))
            {
                if (!SessionSettings.IsSet(group.Key))
                {
                    skippedKinds.Add(string.Format("{0} ({1}) : {2}",
                        DesignedElement.Label(group.Key), group.Count(),
                        SessionSettings.MissingReason(group.Key)));
                    continue;
                }

                foreach (BatchCandidate candidate in group)
                {
                    DesignedElement element = Design(candidate, readErrors);
                    if (element != null) designed.Add(element);
                }
            }

            if (designed.Count == 0)
            {
                TaskDialog.Show(ProductInfo.Name, NothingDesigned(scope, skippedKinds));
                return Result.Cancelled;
            }

            _lastPlan = ViewColourPlan.Build(designed);
            return Present(uiDocument, view, designed, scope, skippedKinds, readErrors);
        }

        // ------------------------------------------------------------------
        // Un element, un module
        // ------------------------------------------------------------------

        private static DesignedElement Design(BatchCandidate candidate, IList<string> errors)
        {
            string name = Describe(candidate.Element);
            try
            {
                switch (candidate.Kind)
                {
                    case ElementKind.Column: return Column(candidate, errors);
                    case ElementKind.Beam: return Beam(candidate, errors);
                    case ElementKind.Slab: return Slab(candidate, errors);
                    case ElementKind.Wall: return Wall(candidate, errors);
                    case ElementKind.IsolatedFooting: return Footing(candidate, errors);
                    case ElementKind.StripFooting: return StripFooting(candidate, errors);
                    case ElementKind.Stair: return Stair(candidate, errors);
                    default: return null;
                }
            }
            catch (Exception ex)
            {
                // Un element qui fait tomber son module ne doit pas faire tomber le lot,
                // mais il ne doit pas disparaitre non plus.
                errors.Add(name + " : " + ex.Message);
                return null;
            }
        }

        private static DesignedElement Wrap(BatchCandidate candidate, string name,
                                            bool isValid, List<Core.Results.CheckResult> checks,
                                            List<string> warnings,
                                            Reinforcement.Plan.ReinforcementPlan plan,
                                            SteelQuantities quantities)
        {
            return new DesignedElement
            {
                Name = name,
                Kind = candidate.Kind,
                ElementId = candidate.Element.Id.Value.ToString(),
                IsValid = isValid,
                Checks = checks,
                Warnings = warnings,
                Plan = plan,
                Quantities = quantities
            };
        }

        private static DesignedElement Column(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitColumn read = ColumnReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            ColumnDesignResult r = new ColumnDesignModule()
                .Design(read.Data, SessionSettings.Column, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Column, r.Plan));
        }

        private static DesignedElement Beam(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitBeam read = BeamReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            BeamDesignResult r = new BeamDesignModule()
                .Design(read.Data, SessionSettings.Beam, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Beam, r.Plan));
        }

        private static DesignedElement Slab(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitSlab read = SlabReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            SlabDesignResult r = new SlabDesignModule()
                .Design(read.Data, SessionSettings.Slab, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Slab, r.Plan));
        }

        private static DesignedElement Wall(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitWall read = WallReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            WallDesignResult r = new WallDesignModule()
                .Design(read.Data, SessionSettings.Wall, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Wall, r.Plan));
        }

        private static DesignedElement Footing(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitFooting read = FootingReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            FootingDesignResult r = new FootingDesignModule()
                .Design(read.Data, SessionSettings.IsolatedFooting, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Footing, r.Plan));
        }

        private static DesignedElement StripFooting(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitStripFooting read = StripFootingReader.TryRead(candidate.Element, out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            StripFootingDesignResult r = new StripFootingDesignModule()
                .Design(read.Data, SessionSettings.StripFooting, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Footing, r.Plan));
        }

        private static DesignedElement Stair(BatchCandidate candidate, IList<string> errors)
        {
            string error;
            RevitStair read = StairReader.TryRead(candidate.Element, new StairData(), out error);
            if (read == null) { errors.Add(Describe(candidate.Element) + " : " + error); return null; }

            StairDesignResult r = new StairDesignModule()
                .Design(read.Data, SessionSettings.Stair, null);
            return Wrap(candidate, read.Data.Name, r.IsValid, r.Checks, r.Warnings, r.Plan,
                        QuantityCalculator.Compute(r.Stair, r.Plan));
        }

        // ------------------------------------------------------------------
        // Restitution
        // ------------------------------------------------------------------

        private static Result Present(UIDocument uiDocument, View view,
                                      List<DesignedElement> designed, BatchScope scope,
                                      List<string> skippedKinds, List<string> readErrors)
        {
            DashboardSummary summary = ProjectDashboard.Build(designed, 5);

            var dialog = new TaskDialog(ProductInfo.Name)
            {
                MainInstruction = summary.IsDeliverable
                    ? string.Format("Lot conforme : {0} element(s) verifies.", summary.Total)
                    : string.Format("{0} element(s) sur {1} ne passent pas.",
                                    summary.NotCompliantCount + summary.FailedCount,
                                    summary.Total),
                MainContent = Overview(summary, scope, skippedKinds, readErrors),
                AllowCancellation = true,
                CommonButtons = TaskDialogCommonButtons.Close
            };

            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink1,
                "Colorer la vue active",
                "Pose les couleurs de controle sur cette vue seulement. Le modele n'est pas "
                + "modifie, et le retrait est disponible juste apres.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink2,
                "Retirer les couleurs",
                "Rend a la vue son affichage d'origine.");
            dialog.AddCommandLink(TaskDialogCommandLinkId.CommandLink3,
                "Exporter la note de synthese",
                "Le detail : articles en defaut, elements a reprendre, quantitatif par "
                + "famille.");

            TaskDialogResult answer = dialog.Show();

            switch (answer)
            {
                case TaskDialogResult.CommandLink1:
                    return Paint(uiDocument.Document, view, true);
                case TaskDialogResult.CommandLink2:
                    return Paint(uiDocument.Document, view, false);
                case TaskDialogResult.CommandLink3:
                    return Export(designed, scope, skippedKinds);
                default:
                    return Result.Succeeded;
            }
        }

        private static string Overview(DashboardSummary summary, BatchScope scope,
                                       List<string> skippedKinds, List<string> readErrors)
        {
            var sb = new StringBuilder();
            sb.AppendLine(string.Format(
                "Conformes {0}   A verifier {1}   Non conformes {2}   Echecs {3}",
                summary.CompliantCount, summary.ToVerifyCount,
                summary.NotCompliantCount, summary.FailedCount));

            if (summary.FailuresByClause.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Articles en defaut :");
                foreach (ClauseFailure failure in summary.FailuresByClause.Take(4))
                {
                    sb.AppendLine(string.Format("  {0} - {1} element(s)",
                                                failure.Reference, failure.ElementCount));
                }
            }

            if (skippedKinds.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Familles laissees de cote :");
                foreach (string skipped in skippedKinds) sb.AppendLine("  " + skipped);
            }

            if (scope.Refused.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("{0} element(s) non classes.", scope.Refused.Count));
            }

            if (readErrors.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("{0} element(s) illisibles.", readErrors.Count));
            }

            return sb.ToString();
        }

        private static Result Paint(Document document, View view, bool apply)
        {
            if (_lastPlan == null) return Result.Cancelled;

            PaintOutcome outcome;
            using (var transaction = new Transaction(document,
                       apply ? "DanCI - couleurs de controle"
                             : "DanCI - retrait des couleurs"))
            {
                transaction.Start();
                outcome = apply
                    ? ControlColourPainter.Apply(document, view, _lastPlan)
                    : ControlColourPainter.Clear(document, view, _lastPlan);
                transaction.Commit();
            }

            var sb = new StringBuilder();
            sb.AppendLine(string.Format("{0} element(s) {1}.", outcome.Painted,
                                        apply ? "colores" : "remis a leur affichage"));
            if (outcome.Skipped > 0)
            {
                sb.AppendLine(string.Format("{0} element(s) introuvables dans le modele.",
                                            outcome.Skipped));
            }
            foreach (string note in outcome.Messages) sb.AppendLine(note);

            if (apply && outcome.Painted > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Legende :");
                foreach (ControlColour colour in _lastPlan.Legend)
                {
                    sb.AppendLine("  " + colour.Hex + "  " + colour.Label);
                }
                sb.AppendLine();
                sb.AppendLine("Ces couleurs n'appartiennent qu'a cette vue et ne modifient "
                              + "pas le modele.");
            }

            TaskDialog.Show(ProductInfo.Name, sb.ToString());
            return Result.Succeeded;
        }

        private static Result Export(List<DesignedElement> designed, BatchScope scope,
                                     List<string> skippedKinds)
        {
            var header = new ReportHeader
            {
                StructuralCode = "EN 1992-1-1:2004+A1:2014, EN 1997-1, EN 1991-1-1 et EN 1990",
                ApplicationVersion = ProductInfo.ApplicationVersion,
                EngineVersion = ProductInfo.CalculationEngineVersion
            };

            var sb = new StringBuilder();
            sb.Append(CalculationReport.BuildProject(header, designed));

            sb.AppendLine("CE QUE CE LOT NE COUVRE PAS");
            sb.AppendLine(new string('-', 78));
            foreach (string warning in ElementClassifier.StandingWarnings)
            {
                sb.AppendLine("- " + warning);
                sb.AppendLine();
            }
            foreach (string skipped in skippedKinds)
            {
                sb.AppendLine("- " + skipped);
                sb.AppendLine();
            }
            foreach (BatchRefusal refusal in scope.Refused)
            {
                sb.AppendLine("- " + refusal.Name + " : " + refusal.Reason);
                sb.AppendLine();
            }

            var save = new Microsoft.Win32.SaveFileDialog
            {
                Title = "Exporter la note de synthese",
                FileName = "synthese-de-projet-DanCI.txt",
                DefaultExt = ".txt",
                Filter = "Fichier texte (*.txt)|*.txt"
            };
            if (save.ShowDialog() != true) return Result.Cancelled;

            try
            {
                System.IO.File.WriteAllText(save.FileName, sb.ToString(),
                                            new UTF8Encoding(true));
                TaskDialog.Show(ProductInfo.Name, "Fichier enregistre :"
                                + Environment.NewLine + save.FileName);
            }
            catch (Exception ex)
            {
                TaskDialog.Show(ProductInfo.Name, ex.Message);
                return Result.Failed;
            }
            return Result.Succeeded;
        }

        private static string NothingDesigned(BatchScope scope, List<string> skippedKinds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Aucun element n'a pu etre calcule dans cette vue.");
            sb.AppendLine();
            foreach (string skipped in skippedKinds) sb.AppendLine(skipped);
            if (scope.Refused.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(string.Format("{0} element(s) non classes.", scope.Refused.Count));
            }
            return sb.ToString();
        }

        private static string Describe(Element element)
        {
            try
            {
                return string.Format("{0} [{1}]", element.Name, element.Id.Value);
            }
            catch (Exception)
            {
                return "Element";
            }
        }
    }
}
