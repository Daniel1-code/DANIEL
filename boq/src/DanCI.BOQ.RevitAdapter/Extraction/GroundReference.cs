using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Reperes du « niveau bas » d'un document : cote du terrain naturel (si designee) ou du plus
    /// bas niveau portant des murs ou des sols ; sols poses au-dessus (dalles d'etage, toitures-
    /// terrasses) ; murs du niveau bas (ceux qui partent du sol).
    /// </summary>
    internal sealed class GroundReference
    {
        /// <summary>Tolerance au-dessus du niveau bas (pieds) : 1,0 m.</summary>
        private static readonly double Tolerance = UnitUtils.ConvertToInternalUnits(1.0, UnitTypeId.Meters);

        /// <summary>Cote du terrain naturel (pieds, repere des niveaux) ; null si non designee ou introuvable.</summary>
        public double? NaturalGround { get; private set; }
        /// <summary>Cote du niveau bas : terrain naturel, sinon plus bas niveau portant des murs ou des sols.</summary>
        public double Base { get; private set; }
        public HashSet<long> ElevatedFloors { get; } = new HashSet<long>();
        /// <summary>Cote du terrain naturel dans le repere de la GEOMETRIE (ProjectElevation) ; null si non designee.</summary>
        public double? NaturalGroundGeometry { get; private set; }

        public static GroundReference Compute(Document doc, ExtractionSettings settings)
        {
            var g = new GroundReference();
            string tn = settings?.Excavation?.NaturalGroundLevel;
            if (!string.IsNullOrWhiteSpace(tn))
            {
                Level l = new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>()
                    .FirstOrDefault(x => string.Equals(x.Name, tn, StringComparison.OrdinalIgnoreCase));
                if (l != null)
                {
                    double offset = UnitUtils.ConvertToInternalUnits(settings.Excavation.OffsetM, UnitTypeId.Meters);
                    g.NaturalGround = l.Elevation + offset;
                    g.NaturalGroundGeometry = l.ProjectElevation + offset;
                }
            }
            var floors = new FilteredElementCollector(doc).OfClass(typeof(Floor)).Cast<Floor>().ToList();
            var walls = new FilteredElementCollector(doc).OfClass(typeof(Wall)).Cast<Wall>().ToList();
            var bases = floors.Select(FloorElevation).Concat(walls.Select(WallBase)).Where(z => z.HasValue).Select(z => z.Value).ToList();
            g.Base = g.NaturalGround ?? (bases.Count > 0 ? bases.Min() : 0);
            foreach (Floor f in floors)
            {
                double? z = FloorElevation(f);
                if (z.HasValue && z.Value > g.Base + Tolerance) g.ElevatedFloors.Add(f.Id.Value);
            }
            return g;
        }

        /// <summary>Vrai si le mur part du niveau bas (sa base est a moins de 1 m au-dessus).</summary>
        public bool IsGroundWall(Wall w)
        {
            double? z = WallBase(w);
            return z.HasValue && z.Value <= Base + Tolerance;
        }

        public static double? FloorElevation(Floor f)
        {
            if (!(f.Document.GetElement(f.LevelId) is Level level)) return null;
            Parameter p = f.get_Parameter(BuiltInParameter.FLOOR_HEIGHTABOVELEVEL_PARAM);
            return level.Elevation + (p != null && p.HasValue ? p.AsDouble() : 0);
        }

        /// <summary>Base du mur dans le repere de la geometrie.</summary>
        public static double? WallBaseGeometry(Wall w)
        {
            Parameter lp = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (lp == null || !(w.Document.GetElement(lp.AsElementId()) is Level level)) return null;
            Parameter off = w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            return level.ProjectElevation + (off != null && off.HasValue ? off.AsDouble() : 0);
        }

        public static double? WallBase(Wall w)
        {
            Parameter lp = w.get_Parameter(BuiltInParameter.WALL_BASE_CONSTRAINT);
            if (lp == null || !(w.Document.GetElement(lp.AsElementId()) is Level level)) return null;
            Parameter off = w.get_Parameter(BuiltInParameter.WALL_BASE_OFFSET);
            return level.Elevation + (off != null && off.HasValue ? off.AsDouble() : 0);
        }
    }
}
