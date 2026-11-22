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
    /// Fouilles en rigole sous les murs du niveau bas, sur tout leur trace : longueur du mur (moins
    /// une demi-largeur a chaque extremite jointe, pour ne pas compter deux fois les croisements) x
    /// largeur (epaisseur + 2 surlargeurs) x profondeur (sous le terrain naturel, ou sous la base du
    /// mur s'il n'est pas designe). TOUJOURS ESTIMEES. Un mur portant une semelle filante modelisee
    /// est ignore (sa fouille est comptee avec la semelle). Lecture seule.
    /// </summary>
    internal sealed class WallTrenchExtractor
    {
        public const string Name = "Fouilles sous murs";

        private readonly ElementDescriber describer;
        private readonly ExtractionSettings settings;
        private readonly GroundReference ground;
        private readonly IProgressSink progress;

        public WallTrenchExtractor(ElementDescriber describer, ExtractionSettings settings, GroundReference ground, IProgressSink progress)
        {
            this.describer = describer;
            this.settings = settings ?? new ExtractionSettings();
            this.ground = ground;
            this.progress = progress ?? NoProgress.Instance;
        }

        public void Extract(ExtractionReport report)
        {
            WallTrenchSettings s = settings.WallTrenches ?? new WallTrenchSettings();
            if (!s.Enabled) return;
            Document doc = describer.Document;
            var withFooting = new HashSet<long>(new FilteredElementCollector(doc).OfClass(typeof(WallFoundation)).Cast<WallFoundation>()
                .Select(f => f.WallId.Value));
            List<Wall> walls = StackedWalls.Expand(doc, new FilteredElementCollector(doc).OfClass(typeof(Wall))).OfType<Wall>()
                .Where(w => !w.IsStackedWall && w.WallType.Kind == WallKind.Basic && w.Location is LocationCurve)
                .Where(ground.IsGroundWall)
                // Un membre de mur empile peut etre renvoye par la collecte ET par son parent : une seule fois.
                .GroupBy(w => w.Id.Value).Select(g => g.First())
                .ToList();
            var candidates = new HashSet<long>(walls.Select(w => w.Id.Value));
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var inv = CultureInfo.InvariantCulture;
            double depth = Math.Max(0, s.DepthM);
            string top = ground.NaturalGround.HasValue
                ? "sous le terrain naturel (« " + settings.Excavation.NaturalGroundLevel + " »)"
                : "sous la base du mur (terrain naturel non désigné)";
            int count = 0, skippedFooting = 0;
            double total = 0;
            int index = 0;
            foreach (Wall w in walls)
            {
                progress.Step(Name, ++index, walls.Count);
                report.Count(report.ElementsScanned, Name);
                if (describer.ShouldSkip(w, out string reason))
                {
                    report.Count(report.Skipped, reason);
                    continue;
                }
                if (describer.PhaseAllows(w, out string status, out _) && status == PhaseStatus.Demolished) continue;
                if (s.StructuralOnly && (w.get_Parameter(BuiltInParameter.WALL_STRUCTURAL_SIGNIFICANT)?.AsInteger() ?? 0) != 1) continue;
                if (s.SkipWithWallFoundation && withFooting.Contains(w.Id.Value))
                {
                    skippedFooting++;
                    continue;
                }
                var curve = (LocationCurve)w.Location;
                double lengthM = RevitUnits.ToMillimeters(curve.Curve.Length) / 1000.0;
                double thicknessM = RevitUnits.ToMillimeters(w.Width) / 1000.0;
                double widthM = WallTrench.Width(thicknessM, s);
                int joined = 0;
                bool startJoined = false, endJoined = false;
                for (int end = 0; end <= 1; end++)
                {
                    try
                    {
                        if (curve.get_ElementsAtJoin(end).Cast<Element>().Any(e => e.Id != w.Id && candidates.Contains(e.Id.Value)))
                        {
                            joined++;
                            if (end == 0) startJoined = true; else endJoined = true;
                        }
                    }
                    catch (Autodesk.Revit.Exceptions.ApplicationException)
                    {
                        // extremite non jointe
                    }
                }
                double usefulM = WallTrench.Length(lengthM, widthM, joined);
                double volume = usefulM * widthM * depth;
                if (volume <= 1e-6) continue;

                ElementQuantity q = describer.Describe(w);
                q.Kind = QuantityKinds.Excavation;
                q.Unit = BoqUnit.CubicMeter;
                q.Value = volume;
                q.Material = string.Empty;
                q.Reliability = Reliability.Estimated;
                q.MethodCode = "WALL_TRENCH";
                q.MethodDescription = "Fouille en rigole sous mur : " + usefulM.ToString("0.00", fr) + " m (tracé " + lengthM.ToString("0.00", fr) + " m, " +
                                      joined + " extrémité(s) jointe(s) déduite(s)) × " + widthM.ToString("0.00", fr) + " m × " + depth.ToString("0.00", fr) + " m.";
                q.Assumption = "ESTIMÉ : mur sans semelle filante modélisée ; largeur = épaisseur " + thicknessM.ToString("0.00", fr) + " m + 2 × " +
                               s.SideMarginM.ToString("0.00", fr) + " m (min. " + s.MinWidthM.ToString("0.00", fr) + " m), profondeur " +
                               depth.ToString("0.00", fr) + " m " + top + " (extraction.json, « fouillesSousMurs »).";
                q.Parameters["Ouvrage"] = WallTrench.Work;
                q.Parameters["LongueurM"] = usefulM.ToString("0.###", inv);
                q.Parameters["LargeurM"] = widthM.ToString("0.###", inv);
                q.Parameters["ProfondeurM"] = depth.ToString("0.###", inv);
                q.Parameters[WallTrench.BuriedParameter] = (usefulM * thicknessM * depth).ToString("0.####", inv);
                // Trace 3D (coordonnees du modele hote) : rigole le long du mur, croisements deduits.
                double? topZ = ground.NaturalGroundGeometry ?? GroundReference.WallBaseGeometry(w);
                if (topZ.HasValue)
                {
                    double wFt = UnitUtils.ConvertToInternalUnits(widthM, UnitTypeId.Meters);
                    double dFt = UnitUtils.ConvertToInternalUnits(depth, UnitTypeId.Meters);
                    var pts = curve.Curve.Tessellate().Select(p => describer.ToHost(new XYZ(p.X, p.Y, topZ.Value))).ToList();
                    double zt = pts[0].Z;
                    q.Parameters[Excavation3D.Parameter] = Excavation3D.Encode(Excavation3D.AlongPolyline(
                        pts.Select(p => (p.X, p.Y)).ToList(), wFt, zt - dFt, zt, startJoined ? wFt / 2 : 0, endJoined ? wFt / 2 : 0));
                }
                report.Quantities.Add(q);
                count++;
                total += volume;
            }
            if (count > 0 || skippedFooting > 0)
            {
                report.Notes.Add(new BoqIssue(IssueSeverity.Info, "FOUILLES_SOUS_MURS",
                    count + " mur(s) du niveau bas : " + total.ToString("0.00", fr) + " m³ de fouilles en rigole ESTIMÉES (" + top + ")" +
                    (skippedFooting > 0 ? " ; " + skippedFooting + " mur(s) déjà sur semelle filante modélisée, non recreusé(s)" : string.Empty) + "."));
            }
        }
    }
}
