using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Coffrage : calcul GEOMETRIQUE sur les faces des solides de chaque element en beton.
    ///  1. chaque face est classee par sa normale (FormworkFaceRule) : laterale, sous-face,
    ///     dessus non coffre, contre le sol ;
    ///  2. sur les faces coffrees planes, la surface en contact avec un element voisin est
    ///     deduite. Methode : la face est extrudee de DELTA vers l'exterieur, le volume commun
    ///     de cette lame avec chaque voisin est calcule par Revit (BooleanOperationsUtils), et
    ///     surface de contact = volume / DELTA. Les trous et bords courbes sont geres par Revit.
    /// Les faces courbes (poteaux circulaires) sont comptees entieres ; si elles touchent un
    /// voisin, la quantite est marquee estimee (contact non deduit, legere surestimation).
    /// </summary>
    internal sealed class FormworkExtractor
    {
        public const string Name = "Coffrage";

        /// <summary>Epaisseur de la lame de detection des contacts : 1 cm.</summary>
        private static readonly double Delta = UnitUtils.ConvertToInternalUnits(0.01, UnitTypeId.Meters);

        private static readonly BuiltInCategory[] NeighbourCategories =
        {
            BuiltInCategory.OST_StructuralFoundation,
            BuiltInCategory.OST_StructuralColumns,
            BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors,
            BuiltInCategory.OST_Walls,
            BuiltInCategory.OST_Stairs,
            BuiltInCategory.OST_StairsRuns,
            BuiltInCategory.OST_StairsLandings,
            BuiltInCategory.OST_Roofs
        };

        private readonly ElementDescriber describer;
        private readonly FormworkSettings settings;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public FormworkExtractor(ElementDescriber describer, FormworkSettings settings, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings ?? new FormworkSettings();
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        /// <summary>Metre le coffrage des elements qui ont fourni un volume de beton.</summary>
        public void Extract(ExtractionReport report, IEnumerable<long> concreteElementIds)
        {
            Document doc = describer.Document;
            List<long> ids = concreteElementIds.Distinct().OrderBy(i => i).ToList();
            int index = 0;
            foreach (long id in ids)
            {
                progress.Step("Coffrage", ++index, ids.Count, 5);
                Element e = doc.GetElement(new ElementId(id));
                if (e == null)
                {
                    continue;
                }
                report.Count(report.ElementsScanned, Name);
                try
                {
                    ExtractElement(e, report);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ApplicationException || ex is InvalidOperationException)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC",
                        "Coffrage non calculable pour l'élément " + e.Id.Value + " : " + ex.Message, e.Id.Value);
                }
            }
        }

        private void ExtractElement(Element e, ExtractionReport report)
        {
            string category = ElementDescriber.CategoryName(e.Category);
            bool isFoundation = category == nameof(BuiltInCategory.OST_StructuralFoundation);
            bool isOnGrade = e is Floor && settings.SlabOnGradeTypes != null && !settings.SlabOnGradeTypes.IsEmpty &&
                             settings.SlabOnGradeTypes.IsMatch(describer.Document.GetElement(e.GetTypeId())?.Name);

            List<Solid> own = Solids(e);
            if (own.Count == 0)
            {
                return;
            }
            List<Element> neighbours = settings.DeductContacts ? Neighbours(e) : new List<Element>();

            double formed = 0, sides = 0, soffits = 0, tops = 0, soil = 0, contacts = 0;
            int curvedNearNeighbour = 0, failedContacts = 0;

            foreach (Solid solid in own)
            {
                foreach (Face face in solid.Faces)
                {
                    double area = face.Area;
                    if (area <= 0)
                    {
                        continue;
                    }
                    var planar = face as PlanarFace;
                    XYZ normal = planar != null ? planar.FaceNormal : CentreNormal(face);
                    FormworkFaceRole role = FormworkFaceRule.Classify(normal.Z, isFoundation, isOnGrade, settings);

                    if (role == FormworkFaceRole.Top) { tops += area; continue; }
                    if (role == FormworkFaceRole.AgainstSoil) { soil += area; continue; }

                    double contact = 0;
                    if (neighbours.Count > 0)
                    {
                        if (planar == null)
                        {
                            curvedNearNeighbour++;
                        }
                        else if (!TryContactArea(planar, neighbours, out contact))
                        {
                            failedContacts++;
                            contact = 0;
                        }
                    }
                    contact = Math.Min(contact, area);
                    contacts += contact;
                    formed += area - contact;
                    if (role == FormworkFaceRole.Side) sides += area; else soffits += area;
                }
            }

            ElementQuantity q = describer.Describe(e);
            q.Kind = QuantityKinds.Formwork;
            q.Unit = BoqUnit.SquareMeter;
            q.Value = RevitUnits.ToSquareMeters(formed);
            q.Material = string.Empty;
            q.MethodCode = "FACES_MINUS_CONTACTS";
            q.MethodDescription = "Faces latérales et sous-faces des solides de l'élément, moins les surfaces en contact avec " +
                                  "les éléments voisins (lame de 1 cm, intersection booléenne Revit). Dessus de pente < " +
                                  settings.MaxUncoveredTopSlopeDeg.ToString("0", CultureInfo.InvariantCulture) + "° exclus" +
                                  (isFoundation && settings.ExcludeFoundationBottoms ? ", sous-face de fondation exclue" : string.Empty) +
                                  (isOnGrade ? ", sous-face sur terre-plein exclue" : string.Empty) +
                                  (isFoundation && settings.FoundationsCastAgainstSoil ? ", flancs coulés pleine fouille exclus" : string.Empty) + ".";

            var inv = CultureInfo.InvariantCulture;
            q.Parameters["FacesLateralesM2"] = RevitUnits.ToSquareMeters(sides).ToString("0.####", inv);
            q.Parameters["SousFacesM2"] = RevitUnits.ToSquareMeters(soffits).ToString("0.####", inv);
            q.Parameters["ContactsDeduitsM2"] = RevitUnits.ToSquareMeters(contacts).ToString("0.####", inv);
            q.Parameters["DessusNonCoffresM2"] = RevitUnits.ToSquareMeters(tops).ToString("0.####", inv);
            q.Parameters["ContreSolM2"] = RevitUnits.ToSquareMeters(soil).ToString("0.####", inv);
            q.Parameters["NbVoisins"] = neighbours.Count.ToString(inv);

            if (curvedNearNeighbour > 0 || failedContacts > 0)
            {
                q.Reliability = Reliability.Estimated;
                q.Assumption = (curvedNearNeighbour > 0 ? curvedNearNeighbour + " face(s) courbe(s)" : string.Empty) +
                               (curvedNearNeighbour > 0 && failedContacts > 0 ? " et " : string.Empty) +
                               (failedContacts > 0 ? failedContacts + " face(s) au contact non calculable" : string.Empty) +
                               " près d'éléments voisins : contacts non déduits sur ces faces (surface surestimée).";
            }
            report.Quantities.Add(q);
        }

        private bool TryContactArea(PlanarFace face, List<Element> neighbours, out double contactArea)
        {
            contactArea = 0;
            Solid blade = Blade(face);
            if (blade == null)
            {
                return false;
            }

            double volume = 0;
            foreach (Element n in neighbours)
            {
                foreach (Solid other in Solids(n))
                {
                    try
                    {
                        Solid common = BooleanOperationsUtils.ExecuteBooleanOperation(blade, other, BooleanOperationsType.Intersect);
                        if (common != null)
                        {
                            volume += common.Volume;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.InvalidOperationException)
                    {
                        return false;
                    }
                }
            }
            contactArea = volume / Delta;
            return true;
        }

        /// <summary>
        /// Lame d'epaisseur DELTA posee sur la face, vers l'exterieur. Les contours sont d'abord
        /// orientes de facon coherente : le contour exterieur (plus grande aire) dans un sens, les
        /// trous dans l'autre. Si Revit refuse, on essaie l'orientation inverse ; null si les deux
        /// echouent (la face est alors comptee sans deduction et la quantite marquee estimee).
        /// </summary>
        private static Solid Blade(PlanarFace face)
        {
            List<CurveLoop> loops;
            try
            {
                loops = face.GetEdgesAsCurveLoops().ToList();
            }
            catch (Autodesk.Revit.Exceptions.ApplicationException)
            {
                return null;
            }
            if (loops.Count == 0)
            {
                return null;
            }
            var areas = loops.Select(l => SignedArea(l, face)).ToList();
            int outer = areas.Select((a, i) => new { a = Math.Abs(a), i }).OrderByDescending(x => x.a).First().i;
            for (int i = 0; i < loops.Count; i++)
            {
                bool positive = areas[i] > 0;
                bool wantPositive = i == outer;
                if (positive != wantPositive)
                {
                    loops[i].Flip();
                }
            }
            foreach (bool flipAll in new[] { false, true })
            {
                if (flipAll)
                {
                    foreach (CurveLoop l in loops) l.Flip();
                }
                try
                {
                    return GeometryCreationUtilities.CreateExtrusionGeometry(loops, face.FaceNormal, Delta);
                }
                catch (Exception ex) when (ex is Autodesk.Revit.Exceptions.ArgumentException || ex is Autodesk.Revit.Exceptions.InvalidOperationException)
                {
                    // essai suivant
                }
            }
            return null;
        }

        /// <summary>Aire algebrique d'un contour dans le repere (XVector, YVector) de la face (formule du lacet).</summary>
        private static double SignedArea(CurveLoop loop, PlanarFace face)
        {
            var points = new List<XYZ>();
            foreach (Curve c in loop)
            {
                IList<XYZ> pts = c.Tessellate();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    points.Add(pts[i]); // le dernier point est le premier de la courbe suivante
                }
            }
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                XYZ a = points[i] - face.Origin, b = points[(i + 1) % points.Count] - face.Origin;
                double ua = a.DotProduct(face.XVector), va = a.DotProduct(face.YVector);
                double ub = b.DotProduct(face.XVector), vb = b.DotProduct(face.YVector);
                sum += ua * vb - ub * va;
            }
            return sum / 2.0;
        }

        private List<Element> Neighbours(Element e)
        {
            BoundingBoxXYZ box = e.get_BoundingBox(null);
            if (box == null)
            {
                return new List<Element>();
            }
            var margin = new XYZ(2 * Delta, 2 * Delta, 2 * Delta);
            var outline = new Outline(box.Min - margin, box.Max + margin);
            return new FilteredElementCollector(describer.Document)
                .WherePasses(new ElementMulticategoryFilter(NeighbourCategories))
                .WherePasses(new BoundingBoxIntersectsFilter(outline))
                .WhereElementIsNotElementType()
                .Where(n => n.Id != e.Id && !ElementDescriber.IsSecondaryOption(n) && !(n is Wall w && w.IsStackedWall))
                .Where(n => Solids(n).Count > 0)
                .ToList();
        }

        private List<Solid> Solids(Element e) => solids.Of(e);

        private static XYZ CentreNormal(Face face)
        {
            BoundingBoxUV uv = face.GetBoundingBox();
            var centre = new UV((uv.Min.U + uv.Max.U) / 2, (uv.Min.V + uv.Max.V) / 2);
            return face.ComputeNormal(centre);
        }
    }
}
