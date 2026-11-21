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
    /// Fouilles et beton de proprete sous les fondations. Base de calcul : la FACE INFERIEURE
    /// reelle de la fondation (la plus grande face plane tournee vers le bas) : aire, perimetre,
    /// cote du fond, et emprise dans son propre repere (une semelle tournee n'est pas mesuree sur
    /// sa boite englobante). Les deux quantites sont ESTIMEES : ni la fouille ni le beton de
    /// proprete ne sont modelises.
    /// </summary>
    internal sealed class EarthworkExtractor
    {
        public const string Name = "Terrassement";

        private readonly ElementDescriber describer;
        private readonly ExtractionSettings settings;
        private readonly SolidCache solids;
        private readonly IProgressSink progress;

        public EarthworkExtractor(ElementDescriber describer, ExtractionSettings settings, SolidCache solids, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings;
            this.solids = solids;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            Document doc = describer.Document;
            List<Element> foundations = new FilteredElementCollector(doc)
                .OfCategory(BuiltInCategory.OST_StructuralFoundation)
                .WhereElementIsNotElementType()
                .Where(e => !(e is Part))
                .ToList();
            if (foundations.Count == 0)
            {
                return;
            }

            double? groundZ = GroundElevation(doc);
            if (groundZ == null)
            {
                report.Notes.Add(new BoqIssue(IssueSeverity.Warning, "TERRASSEMENT_NON_CONFIGURE",
                    string.IsNullOrWhiteSpace(settings.Excavation?.NaturalGroundLevel)
                        ? "Fouilles non calculées : désignez le niveau du terrain naturel (extraction.json, « terrassement » → « niveauTerrainNaturel »)."
                        : "Fouilles non calculées : niveau « " + settings.Excavation.NaturalGroundLevel + " » introuvable dans le modèle."));
            }

            int index = 0;
            foreach (Element f in foundations)
            {
                progress.Step(Name, ++index, foundations.Count);
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(f, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (describer.PhaseAllows(f, out string status, out _) && status == PhaseStatus.Demolished)
                {
                    continue;
                }
                try
                {
                    ExtractFoundation(f, groundZ, report);
                }
                catch (Autodesk.Revit.Exceptions.ApplicationException ex)
                {
                    report.Note(IssueSeverity.Error, "EXTRACTION_ECHEC", "Fouille illisible pour la fondation " + f.Id.Value + " : " + ex.Message, f.Id.Value);
                }
            }
        }

        private void ExtractFoundation(Element f, double? groundZ, ExtractionReport report)
        {
            PlanarFace bottom = solids.Of(f)
                .SelectMany(s => s.Faces.Cast<Face>())
                .OfType<PlanarFace>()
                .Where(p => p.FaceNormal.Z < -0.99)
                .OrderByDescending(p => p.Area)
                .FirstOrDefault();
            if (bottom == null)
            {
                report.Note(IssueSeverity.Warning, "FONDATION_SANS_FOND",
                    "Fondation " + f.Id.Value + " sans face inférieure horizontale lisible : fouille et béton de propreté non calculés.", f.Id.Value);
                return;
            }

            double areaM2 = RevitUnits.ToSquareMeters(bottom.Area);
            double perimeterM = RevitUnits.ToMeters(OuterPerimeter(bottom));
            Extent(bottom, out double lengthM, out double widthM);
            double bottomZ = bottom.Origin.Z;
            var fr = CultureInfo.GetCultureInfo("fr-FR");

            // Beton de proprete.
            double blinding = BlindingRule.Volume(areaM2, perimeterM, settings.Blinding);
            if (blinding > 0)
            {
                ElementQuantity b = describer.Describe(f);
                b.Kind = QuantityKinds.BlindingConcrete;
                b.Unit = BoqUnit.CubicMeter;
                b.Value = blinding;
                b.Material = string.Empty;
                b.MethodCode = "BOTTOM_FACE_X_THICKNESS";
                b.MethodDescription = "Face inférieure de la fondation (" + areaM2.ToString("0.###", fr) + " m², périmètre " +
                                      perimeterM.ToString("0.##", fr) + " m) élargie du débord, × épaisseur.";
                b.Reliability = Reliability.Estimated;
                b.Assumption = "ESTIMÉ : béton de propreté non modélisé ; épaisseur " + settings.Blinding.ThicknessM.ToString("0.###", fr) +
                               " m, débord " + settings.Blinding.OverhangM.ToString("0.###", fr) + " m (conventions de extraction.json).";
                report.Quantities.Add(b);
            }

            // Fouille.
            if (groundZ == null)
            {
                return;
            }
            double depthM = RevitUnits.ToMeters(groundZ.Value - bottomZ) +
                            (settings.Excavation.IncludeBlinding ? settings.Blinding.ThicknessM : 0);
            ElementQuantity q = describer.Describe(f);
            q.Kind = QuantityKinds.Excavation;
            q.Unit = BoqUnit.CubicMeter;
            q.Material = string.Empty;
            q.MethodCode = "PIT_FROM_BOTTOM_FACE";
            q.Reliability = Reliability.Estimated;
            q.Parameters["EmpriseM"] = lengthM.ToString("0.###", CultureInfo.InvariantCulture) + " x " + widthM.ToString("0.###", CultureInfo.InvariantCulture);
            q.Parameters["ProfondeurM"] = depthM.ToString("0.###", CultureInfo.InvariantCulture);
            if (depthM <= 0)
            {
                q.Value = 0;
                q.MethodDescription = "Fond de fondation au-dessus du terrain naturel : pas de fouille.";
                q.Assumption = "ESTIMÉ : fond de fondation au-dessus du niveau « " + settings.Excavation.NaturalGroundLevel + " ».";
            }
            else
            {
                q.Value = ExcavationRule.PitVolume(lengthM, widthM, depthM, settings.Excavation.WorkingMarginM, settings.Excavation.SlopeHorizontalPerVertical);
                q.MethodDescription = "Fouille en tronc de pyramide sous la face inférieure de la fondation.";
                q.Assumption = ExcavationRule.Describe(lengthM, widthM, depthM, settings.Excavation);
            }
            report.Quantities.Add(q);
        }

        /// <summary>Cote du terrain naturel en coordonnees internes du document ; null si non designee.</summary>
        private double? GroundElevation(Document doc)
        {
            string name = settings.Excavation?.NaturalGroundLevel;
            if (string.IsNullOrWhiteSpace(name))
            {
                return null;
            }
            Level level = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                .FirstOrDefault(l => string.Equals(l.Name, name, StringComparison.OrdinalIgnoreCase));
            if (level == null)
            {
                return null;
            }
            // ProjectElevation : cote dans le repere interne, celui de la geometrie.
            return level.ProjectElevation + UnitUtils.ConvertToInternalUnits(settings.Excavation.OffsetM, UnitTypeId.Meters);
        }

        /// <summary>Perimetre du contour exterieur (la plus longue boucle d'aretes).</summary>
        private static double OuterPerimeter(PlanarFace face)
        {
            double best = 0;
            foreach (EdgeArray loop in face.EdgeLoops)
            {
                double length = 0;
                foreach (Edge edge in loop)
                {
                    length += edge.ApproximateLength;
                }
                best = Math.Max(best, length);
            }
            return best;
        }

        /// <summary>Emprise de la face dans son propre repere (XVector, YVector), en metres.</summary>
        private static void Extent(PlanarFace face, out double lengthM, out double widthM)
        {
            double minU = double.MaxValue, maxU = double.MinValue, minV = double.MaxValue, maxV = double.MinValue;
            foreach (EdgeArray loop in face.EdgeLoops)
            {
                foreach (Edge edge in loop)
                {
                    foreach (XYZ p in edge.Tessellate())
                    {
                        XYZ d = p - face.Origin;
                        double u = d.DotProduct(face.XVector), v = d.DotProduct(face.YVector);
                        minU = Math.Min(minU, u); maxU = Math.Max(maxU, u);
                        minV = Math.Min(minV, v); maxV = Math.Max(maxV, v);
                    }
                }
            }
            double a = RevitUnits.ToMeters(maxU - minU), b = RevitUnits.ToMeters(maxV - minV);
            lengthM = Math.Max(a, b);
            widthM = Math.Min(a, b);
        }
    }
}
