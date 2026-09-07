using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Geometry;

namespace DanCI.Structural.Reinforcement.Plan
{
    public enum RebarKind
    {
        Longitudinal,
        Stirrup,
        CrossTie
    }

    public enum SegmentKind
    {
        Line,
        Arc
    }

    /// <summary>Un segment de la fibre moyenne d'une barre, droit ou circulaire.</summary>
    public sealed class PlanSegment
    {
        public SegmentKind Kind { get; set; }

        public LocalPoint Start { get; set; }
        public LocalPoint End { get; set; }

        /// <summary>Arc uniquement : centre de l'arc.</summary>
        public LocalPoint Center { get; set; }
        /// <summary>Arc uniquement : rayon (mm).</summary>
        public double RadiusMm { get; set; }
        /// <summary>Arc uniquement : angle de depart (rad), mesure depuis l'axe local X.</summary>
        public double StartAngle { get; set; }
        /// <summary>Arc uniquement : angle d'arrivee (rad).</summary>
        public double EndAngle { get; set; }

        public static PlanSegment Line(LocalPoint start, LocalPoint end)
        {
            return new PlanSegment { Kind = SegmentKind.Line, Start = start, End = end };
        }

        public static PlanSegment Arc(LocalPoint center, double radiusMm, double startAngle, double endAngle)
        {
            return new PlanSegment
            {
                Kind = SegmentKind.Arc,
                Center = center,
                RadiusMm = radiusMm,
                StartAngle = startAngle,
                EndAngle = endAngle
            };
        }

        /// <summary>Longueur developpee du segment (mm).</summary>
        public double LengthMm
        {
            get
            {
                if (Kind == SegmentKind.Arc) return RadiusMm * Math.Abs(EndAngle - StartAngle);
                double dx = End.X - Start.X, dy = End.Y - Start.Y, dz = End.Z - Start.Z;
                return Math.Sqrt(dx * dx + dy * dy + dz * dz);
            }
        }
    }

    public enum LayoutKind
    {
        Single,
        FixedNumber,
        MaximumSpacing
    }

    /// <summary>Regle de repetition d'une barre le long d'une direction.</summary>
    public sealed class ArrayLayout
    {
        public LayoutKind Kind { get; set; }

        /// <summary>Direction de repetition dans le repere local.</summary>
        public LocalVector Direction { get; set; }

        /// <summary>Longueur totale du reseau (mm).</summary>
        public double ArrayLengthMm { get; set; }

        /// <summary>Nombre de positions, pour un reseau a nombre fixe.</summary>
        public int Count { get; set; }

        /// <summary>Espacement maximal (mm), pour un reseau a espacement impose.</summary>
        public double SpacingMm { get; set; }

        public bool IncludeFirst { get; set; }
        public bool IncludeLast { get; set; }

        public static ArrayLayout Single()
        {
            return new ArrayLayout { Kind = LayoutKind.Single, Count = 1 };
        }

        public static ArrayLayout FixedNumber(LocalVector direction, int count, double arrayLengthMm)
        {
            return new ArrayLayout
            {
                Kind = LayoutKind.FixedNumber,
                Direction = direction,
                Count = count,
                ArrayLengthMm = arrayLengthMm,
                IncludeFirst = true,
                IncludeLast = true
            };
        }

        public static ArrayLayout MaximumSpacing(LocalVector direction, double spacingMm,
                                                 double arrayLengthMm, bool includeFirst, bool includeLast)
        {
            return new ArrayLayout
            {
                Kind = LayoutKind.MaximumSpacing,
                Direction = direction,
                SpacingMm = spacingMm,
                ArrayLengthMm = arrayLengthMm,
                IncludeFirst = includeFirst,
                IncludeLast = includeLast
            };
        }

        /// <summary>Nombre de barres effectivement posees.</summary>
        public int EffectiveCount
        {
            get
            {
                switch (Kind)
                {
                    case LayoutKind.Single:
                        return 1;
                    case LayoutKind.FixedNumber:
                        return Count;
                    default:
                        if (ArrayLengthMm <= 0 || SpacingMm <= 0) return 0;
                        int count = (int)Math.Ceiling(ArrayLengthMm / SpacingMm) + 1;
                        if (!IncludeFirst) count--;
                        if (!IncludeLast) count--;
                        return count > 0 ? count : 0;
                }
            }
        }
    }

    /// <summary>
    /// Un groupe d'armatures identiques : une fibre moyenne, un diametre, une regle de
    /// repetition. C'est la seule chose que la couche Revit a besoin de savoir pour creer
    /// des objets Rebar, quel que soit le type d'element structurel.
    /// </summary>
    public sealed class RebarGroup
    {
        public string Label { get; set; }

        /// <summary>Repere de la barre dans la nomenclature, par exemple "C01-B01".</summary>
        public string Mark { get; set; }

        public RebarKind Kind { get; set; }

        public double DiameterMm { get; set; }

        /// <summary>Fibre moyenne, en coordonnees locales (mm).</summary>
        public List<PlanSegment> Path { get; private set; }

        /// <summary>Boucle fermee : cadre ou cerce.</summary>
        public bool IsClosedLoop { get; set; }

        public bool WithHooks { get; set; }

        public ArrayLayout Layout { get; set; }

        /// <summary>
        /// Normale au plan de la barre, imposee par le constructeur du plan. Sans elle, la
        /// couche Revit devrait deviner une direction perpendiculaire a la barre, ce qui
        /// n'est possible que pour un element vertical.
        /// </summary>
        public LocalVector Normal { get; set; }

        /// <summary>La normale a-t-elle ete renseignee explicitement ?</summary>
        public bool HasNormal { get; set; }

        /// <summary>Impose la normale du groupe.</summary>
        public RebarGroup WithNormal(LocalVector normal)
        {
            Normal = normal;
            HasNormal = true;
            return this;
        }

        public RebarGroup()
        {
            Path = new List<PlanSegment>();
            Layout = ArrayLayout.Single();
        }

        /// <summary>Longueur developpee d'une barre du groupe (mm), hors retours de crochets.</summary>
        public double BarLengthMm
        {
            get
            {
                double total = 0.0;
                foreach (PlanSegment segment in Path) total += segment.LengthMm;
                return total;
            }
        }

        public int BarCount
        {
            get { return Layout != null ? Layout.EffectiveCount : 1; }
        }
    }

    /// <summary>
    /// Le ferraillage complet d'un element, exprime independamment de Revit.
    /// Sortie du moteur, entree du modeleur, du quantitatif et de l'apercu graphique.
    /// </summary>
    public sealed class ReinforcementPlan
    {
        public List<RebarGroup> Groups { get; private set; }

        public ReinforcementPlan()
        {
            Groups = new List<RebarGroup>();
        }

        public void Add(RebarGroup group)
        {
            if (group != null) Groups.Add(group);
        }

        public IEnumerable<RebarGroup> OfKind(RebarKind kind)
        {
            foreach (RebarGroup group in Groups)
            {
                if (group.Kind == kind) yield return group;
            }
        }
    }
}
