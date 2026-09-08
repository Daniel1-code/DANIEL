using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Reinforcement.Schedule
{
    /// <summary>Un plan de ferraillage rattache a l'element qui le porte.</summary>
    public sealed class ScheduledElement
    {
        public string ElementName { get; set; }
        public ReinforcementPlan Plan { get; set; }

        public ScheduledElement()
        {
        }

        public ScheduledElement(string elementName, ReinforcementPlan plan)
        {
            ElementName = elementName;
            Plan = plan;
        }
    }

    /// <summary>
    /// Construit le carnet de ferraillage a partir des plans de plusieurs elements.
    ///
    /// DEUX CHOSES QUE CE CONSTRUCTEUR FAIT ET QUE LE QUANTITATIF NE FAISAIT PAS.
    ///
    /// 1. IL REGROUPE LES BARRES IDENTIQUES a l'echelle du projet. Jusqu'ici chaque
    ///    element numerotait ses barres pour lui-meme : deux cadres rigoureusement
    ///    identiques dans deux poutres portaient deux reperes differents, ce qui oblige le
    ///    facconnier a produire deux fois la meme forme. Le repere designe desormais une
    ///    FORME, et la colonne des emplois dit quels elements la partagent.
    ///
    /// 2. IL CALCULE LA LONGUEUR DE COUPE, pas le developpe d'angle a angle. Une barre
    ///    pliee coupe le coin par un arc de rayon (phi_m + phi)/2 : sommer les segments
    ///    surestime la longueur, de 2,3 % sur un cadre courant. C'est cette longueur-la
    ///    qui part en commande.
    ///
    /// Le tri est stable et lisible : par diametre croissant, puis par longueur de coupe
    /// decroissante. Les gros diametres et les grandes longueurs sont ce qu'on verifie en
    /// premier sur un carnet.
    /// </summary>
    public static class BarScheduleBuilder
    {
        /// <summary>Tolerance de regroupement sur une longueur (mm).</summary>
        private const double LengthTolerance = 1.0;

        /// <summary>Tolerance de regroupement sur un angle de pli (degres).</summary>
        private const double AngleTolerance = 1.0;

        /// <summary>Retour de crochet, en diametres, quand le groupe en porte.</summary>
        private const double HookReturnInDiameters = 10.0;

        public static BarSchedule Build(IEnumerable<ScheduledElement> elements,
                                        string markPrefix = "")
        {
            var schedule = new BarSchedule();
            if (elements == null) return schedule;

            var byShape = new Dictionary<string, BarScheduleRow>();
            var order = new List<BarScheduleRow>();

            foreach (ScheduledElement element in elements)
            {
                if (element == null || element.Plan == null) continue;

                foreach (RebarGroup group in element.Plan.Groups)
                {
                    int count = group.BarCount;
                    if (count <= 0 || group.DiameterMm <= 0) continue;

                    List<double> segments = SegmentLengths(group);
                    List<double> angles = group.BendAnglesDegrees();
                    string key = ShapeKey(group.DiameterMm, segments, angles, group.IsClosedLoop,
                                          group.WithHooks);

                    BarScheduleRow row;
                    if (!byShape.TryGetValue(key, out row))
                    {
                        row = NewRow(group, segments, angles);
                        byShape[key] = row;
                        order.Add(row);
                    }

                    row.Count += count;
                    row.Uses.Add(new BarScheduleUse
                    {
                        ElementName = element.ElementName,
                        GroupLabel = group.Label,
                        Count = count
                    });
                }
            }

            // Le repere est attribue APRES le tri : il suit l'ordre du carnet, ce qui rend
            // la lecture et le pointage sur chantier plus simples.
            schedule.Rows.AddRange(order
                .OrderBy(r => r.DiameterMm)
                .ThenByDescending(r => r.CutLengthMm));

            int index = 0;
            foreach (BarScheduleRow row in schedule.Rows)
            {
                index++;
                row.Mark = string.Format(CultureInfo.InvariantCulture, "{0}{1:00}",
                                         markPrefix ?? string.Empty, index);
            }

            return schedule;
        }

        private static BarScheduleRow NewRow(RebarGroup group, List<double> segments,
                                             List<double> angles)
        {
            double hookAllowance = group.WithHooks
                ? 2.0 * HookReturnInDiameters * group.DiameterMm : 0.0;

            CutLengthResult cut = BarBending.CutLength(group.BarLengthMm, angles.ToArray(),
                                                       group.DiameterMm, hookAllowance);

            var row = new BarScheduleRow
            {
                DiameterMm = group.DiameterMm,
                MandrelDiameterMm = cut.MandrelDiameterMm,
                PolylineLengthMm = cut.PolylineLengthMm,
                BendDeductionMm = cut.TotalDeductionMm,
                HookAllowanceMm = cut.HookAllowanceMm,
                CutLengthMm = cut.CutLengthMm,
                IsClosedLoop = group.IsClosedLoop,
                HasBendBeyondModel = cut.HasBendBeyondModel
            };
            row.SegmentLengthsMm.AddRange(segments);
            row.BendAnglesDegrees.AddRange(angles);
            return row;
        }

        private static List<double> SegmentLengths(RebarGroup group)
        {
            var lengths = new List<double>();
            foreach (PlanSegment segment in group.Path) lengths.Add(segment.LengthMm);
            return lengths;
        }

        /// <summary>
        /// Signature d'une forme faconnee. Deux barres la partagent si elles ont le meme
        /// diametre, les memes longueurs dans le meme ordre et les memes plis — a la
        /// tolerance de faconnage pres, parce qu'un millimetre de difference ne fait pas
        /// deux formes.
        /// </summary>
        private static string ShapeKey(double diameterMm, List<double> segments,
                                       List<double> angles, bool closedLoop, bool hooks)
        {
            var parts = new List<string>
            {
                diameterMm.ToString("0.#", CultureInfo.InvariantCulture),
                closedLoop ? "L" : "O",
                hooks ? "H" : "-"
            };
            foreach (double length in segments)
            {
                parts.Add("s" + Round(length, LengthTolerance)
                    .ToString("0", CultureInfo.InvariantCulture));
            }
            foreach (double angle in angles)
            {
                parts.Add("a" + Round(angle, AngleTolerance)
                    .ToString("0", CultureInfo.InvariantCulture));
            }
            return string.Join("|", parts);
        }

        private static double Round(double value, double step)
        {
            return Math.Round(value / step) * step;
        }
    }

    /// <summary>Le carnet de ferraillage complet.</summary>
    public sealed class BarSchedule
    {
        public List<BarScheduleRow> Rows { get; private set; }

        public BarSchedule()
        {
            Rows = new List<BarScheduleRow>();
        }

        public double TotalMassKg { get { return Rows.Sum(r => r.TotalMassKg); } }

        public double TotalLengthM { get { return Rows.Sum(r => r.TotalLengthM); } }

        public int TotalBarCount { get { return Rows.Sum(r => r.Count); } }

        /// <summary>
        /// Ce que le regroupement a economise en formes distinctes : le nombre de groupes
        /// d'origine moins le nombre de lignes du carnet.
        /// </summary>
        public int DistinctShapes { get { return Rows.Count; } }

        public int TotalUses { get { return Rows.Sum(r => r.Uses.Count); } }

        /// <summary>Masse par diametre, dans l'ordre croissant.</summary>
        public IEnumerable<KeyValuePair<double, double>> MassByDiameter()
        {
            return Rows.GroupBy(r => r.DiameterMm)
                       .OrderBy(g => g.Key)
                       .Select(g => new KeyValuePair<double, double>(g.Key, g.Sum(r => r.TotalMassKg)));
        }
    }
}
