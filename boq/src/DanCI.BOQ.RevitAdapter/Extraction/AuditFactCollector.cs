using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Constats d'audit qui ne se voient que dans le modele (§6) : materiaux absents ou par
    /// defaut, pieces non placees ou non fermees, murs sans hauteur liee, liens decharges,
    /// doublons signales par Revit, chevauchements de beton non joints (double comptage).
    /// LECTURE SEULE.
    /// </summary>
    internal sealed class AuditFactCollector
    {
        private static readonly BuiltInCategory[] StructuralCategories =
        {
            BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls, BuiltInCategory.OST_StairsRuns, BuiltInCategory.OST_StairsLandings,
            BuiltInCategory.OST_Roofs
        };

        private static readonly string[] DefaultMaterialPrefixes = { "Par défaut", "Par defaut", "Default", "Défaut", "Defaut", "<" };

        /// <summary>En deca de ce volume commun (1 cm³), deux elements ne se chevauchent pas.</summary>
        private const double OverlapToleranceM3 = 1e-6;

        private readonly ElementDescriber describer;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public AuditFactCollector(ElementDescriber describer, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Collect(ExtractionReport report, IReadOnlyCollection<long> concreteElementIds)
        {
            Document doc = describer.Document;
            Materials(doc, report);
            Rooms(doc, report);
            Walls(doc, report);
            Links(doc, report);
            RevitDuplicateWarnings(doc, report);
            Overlaps(doc, report, concreteElementIds);
        }

        private void Materials(Document doc, ExtractionReport report)
        {
            List<Element> elements = new FilteredElementCollector(doc)
                .WherePasses(new ElementMulticategoryFilter(StructuralCategories))
                .WhereElementIsNotElementType()
                .Where(e => !describer.ShouldSkip(e, out _) && !(e is Wall w && (w.IsStackedWall || w.WallType.Kind != WallKind.Basic)))
                .ToList();
            int index = 0;
            foreach (Element e in elements)
            {
                progress.Step("Audit : matériaux", ++index, elements.Count, 50);
                ICollection<ElementId> ids = e.GetMaterialIds(false);
                if (ids.Count == 0)
                {
                    if (e is FamilyInstance fi && fi.StructuralMaterialType == Autodesk.Revit.DB.Structure.StructuralMaterialType.Concrete)
                    {
                        continue; // deja signale par l'extraction du beton (BETON_SANS_MATERIAU)
                    }
                    report.Note(IssueSeverity.Warning, "SANS_MATERIAU",
                        "Élément " + e.Id.Value + " (" + e.Category?.Name + ", " + e.Name + ") sans matériau : il ne sera ni reconnu comme béton ni classé.", e.Id.Value);
                    continue;
                }
                foreach (ElementId id in ids)
                {
                    string name = describer.Name(id);
                    if (DefaultMaterialPrefixes.Any(p => name.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
                    {
                        report.Note(IssueSeverity.Warning, "MATERIAU_PAR_DEFAUT",
                            "Élément " + e.Id.Value + " (" + e.Category?.Name + ", " + e.Name + ") : matériau « " + name + " ».", e.Id.Value);
                    }
                }
            }
        }

        private void Rooms(Document doc, ExtractionReport report)
        {
            foreach (Room room in new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_Rooms).WhereElementIsNotElementType().OfType<Room>())
            {
                if (ElementDescriber.IsSecondaryOption(room))
                {
                    continue;
                }
                string label = "Pièce " + room.Id.Value + " « " + room.Number + " " + room.Name + " »";
                if (room.Location == null)
                {
                    report.Note(IssueSeverity.Warning, "PIECE_NON_PLACEE", label + " non placée : ses finitions ne pourront pas être métrées.", room.Id.Value);
                }
                else if (describer.InScope(room) && room.Area <= 0)
                {
                    report.Note(IssueSeverity.Warning, "PIECE_NON_FERMEE", label + " non fermée (surface nulle) : contour à compléter.", room.Id.Value);
                }
            }
        }

        private void Walls(Document doc, ExtractionReport report)
        {
            foreach (Wall wall in new FilteredElementCollector(doc).OfClass(typeof(Wall)).WhereElementIsNotElementType().Cast<Wall>())
            {
                if (describer.ShouldSkip(wall, out _) || wall.IsStackedWall || wall.WallType.Kind != WallKind.Basic)
                {
                    continue;
                }
                Parameter top = wall.get_Parameter(BuiltInParameter.WALL_HEIGHT_TYPE);
                if (top != null && top.StorageType == StorageType.ElementId && top.AsElementId() == ElementId.InvalidElementId)
                {
                    report.Note(IssueSeverity.Warning, "MUR_SANS_HAUTEUR_LIEE",
                        "Mur " + wall.Id.Value + " (" + wall.Name + ") à hauteur non liée : sa hauteur ne suit pas les niveaux, à vérifier.", wall.Id.Value);
                }
            }
        }

        private static void Links(Document doc, ExtractionReport report)
        {
            foreach (RevitLinkType link in new FilteredElementCollector(doc).OfClass(typeof(RevitLinkType)).Cast<RevitLinkType>())
            {
                LinkedFileStatus status = link.GetLinkedFileStatus();
                if (status != LinkedFileStatus.Loaded)
                {
                    report.Note(IssueSeverity.Warning, "LIEN_DECHARGE",
                        "Lien Revit « " + link.Name + " » non chargé (" + status + ") : ses éléments ne sont pas métrés.", link.Id.Value);
                }
            }
        }

        private void RevitDuplicateWarnings(Document doc, ExtractionReport report)
        {
            foreach (FailureMessage m in doc.GetWarnings())
            {
                if (m.GetFailureDefinitionId() != BuiltInFailures.OverlapFailures.DuplicateInstances)
                {
                    continue;
                }
                List<long> ids = m.GetFailingElements().Select(id => id.Value).OrderBy(i => i).ToList();
                if (ids.Count == 0 || !ids.Any(i => describer.InScope(doc.GetElement(new ElementId(i)))))
                {
                    continue;
                }
                var issue = new BoqIssue(IssueSeverity.Error, "DOUBLON_REVIT",
                    "Revit signale des instances identiques au même endroit (" + string.Join(", ", ids) + ") : double comptage. " + m.GetDescriptionText());
                issue.ElementIds.AddRange(ids);
                report.Notes.Add(issue);
            }
        }

        /// <summary>
        /// Deux elements en beton qui partagent un volume ne sont pas joints : leur volume commun
        /// est compte deux fois. Chaque paire est testee une fois (bounding boxes, puis booleen).
        /// </summary>
        private void Overlaps(Document doc, ExtractionReport report, IReadOnlyCollection<long> concreteIds)
        {
            var set = new HashSet<long>(concreteIds);
            var ordered = concreteIds.Distinct().OrderBy(i => i).ToList();
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            int index = 0;
            foreach (long id in ordered)
            {
                progress.Step("Audit : chevauchements", ++index, ordered.Count, 10);
                Element a = doc.GetElement(new ElementId(id));
                BoundingBoxXYZ box = a?.get_BoundingBox(null);
                if (box == null)
                {
                    continue;
                }
                // Une seule collecte filtree par boite englobante, puis seuls les elements en beton
                // d'identifiant superieur : chaque paire n'est testee qu'une fois.
                List<Element> candidates = new FilteredElementCollector(doc)
                    .WherePasses(new BoundingBoxIntersectsFilter(new Outline(box.Min, box.Max)))
                    .WhereElementIsNotElementType()
                    .Where(b => b.Id.Value > id && set.Contains(b.Id.Value))
                    .ToList();
                foreach (Element b in candidates)
                {
                    double common = 0;
                    try
                    {
                        foreach (Solid sa in solids.Of(a))
                        {
                            foreach (Solid sb in solids.Of(b))
                            {
                                Solid inter = BooleanOperationsUtils.ExecuteBooleanOperation(sa, sb, BooleanOperationsType.Intersect);
                                common += inter?.Volume ?? 0;
                            }
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        continue;
                    }
                    double m3 = RevitUnits.ToCubicMeters(common);
                    if (m3 > OverlapToleranceM3)
                    {
                        var issue = new BoqIssue(IssueSeverity.Error, "CHEVAUCHEMENT_NON_JOINT",
                            "Éléments " + a.Id.Value + " (" + a.Category?.Name + ") et " + b.Id.Value + " (" + b.Category?.Name +
                            ") non joints : " + m3.ToString("0.###", fr) + " m³ de béton comptés deux fois. Joindre la géométrie dans Revit.");
                        issue.ElementIds.Add(a.Id.Value);
                        issue.ElementIds.Add(b.Id.Value);
                        report.Notes.Add(issue);
                    }
                }
            }
        }
    }
}
