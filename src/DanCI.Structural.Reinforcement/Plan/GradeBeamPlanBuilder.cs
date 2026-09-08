using System;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une longrine en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X suit la portee depuis le nu du premier appui, Y traverse la
    /// section, Z remonte. L'origine est au nu du premier appui, au milieu de la largeur,
    /// sur la sous-face.
    /// </summary>
    public static class GradeBeamPlanBuilder
    {
        public static ReinforcementPlan Build(GradeBeamData beam, GradeBeamReinforcement r,
                                              string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BottomBars == null || r.BottomBars.Count <= 0) return plan;

            int mark = 0;

            // Les barres sont ancrees dans les appuis : elles depassent de l'ancrage de
            // part et d'autre de la portee libre.
            double extension = Math.Max(r.AnchorageLengthMm, 0.0);
            double xStart = -extension;
            double xEnd = beam.SpanMm + extension;

            double halfWidth = beam.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm;
            if (halfWidth <= 0) return plan;

            mark++;
            plan.Add(LongitudinalRow(r.BottomBars, halfWidth, xStart, xEnd,
                r.CoverMm + r.StirrupDiameterMm + r.BottomBars.DiameterMm / 2.0,
                "Nappe inferieure filante", Mark(markPrefix, mark)));

            if (r.TopBars.Count > 0)
            {
                mark++;
                plan.Add(LongitudinalRow(r.TopBars, halfWidth, xStart, xEnd,
                    beam.HeightMm - r.CoverMm - r.StirrupDiameterMm
                        - r.TopBars.DiameterMm / 2.0,
                    "Nappe superieure filante", Mark(markPrefix, mark)));
            }

            if (r.StirrupSpacingMm > 0)
            {
                mark++;
                plan.Add(Stirrups(beam, r, Mark(markPrefix, mark)));
            }

            return plan;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }

        /// <summary>Un lit de barres filantes, reparti sur la largeur.</summary>
        private static RebarGroup LongitudinalRow(BarSelection selection, double halfWidthMm,
                                                  double xStart, double xEnd, double zMm,
                                                  string label, string mark)
        {
            int perLayer = Math.Max(selection.BarsPerLayer, 1);
            double span = 2.0 * halfWidthMm - selection.DiameterMm;
            double arrayLength = perLayer > 1 ? span : 0.0;
            double y = perLayer > 1 ? -span / 2.0 : 0.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = selection.DiameterMm,
                Label = string.Format("{0} - {1}", label, selection.Label),
                Mark = mark,
                Layout = perLayer > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, perLayer, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(xStart, y, zMm),
                                            new LocalPoint(xEnd, y, zMm)));
            // La barre file dans un plan vertical longitudinal : sa normale est Y.
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>Cadres fermes, a espacement constant sur toute la longrine.</summary>
        private static RebarGroup Stirrups(GradeBeamData beam, GradeBeamReinforcement r,
                                           string mark)
        {
            double halfY = beam.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
            double zBottom = r.CoverMm + r.StirrupDiameterMm / 2.0;
            double zTop = beam.HeightMm - r.CoverMm - r.StirrupDiameterMm / 2.0;

            int count = r.StirrupCount(beam.SpanMm);
            double arrayLength = (count - 1) * r.StirrupSpacingMm;
            double x = (beam.SpanMm - arrayLength) / 2.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Stirrup,
                DiameterMm = r.StirrupDiameterMm,
                IsClosedLoop = true,
                WithHooks = true,
                Label = string.Format("{0} ({1} cadres)", r.TransverseLabel, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisX, count, arrayLength)
                    : ArrayLayout.Single()
            };

            // Le cadre fait le tour de la section, dans un plan transversal.
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, -halfY, zBottom),
                                            new LocalPoint(x, halfY, zBottom)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, halfY, zBottom),
                                            new LocalPoint(x, halfY, zTop)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, halfY, zTop),
                                            new LocalPoint(x, -halfY, zTop)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, -halfY, zTop),
                                            new LocalPoint(x, -halfY, zBottom)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }
    }
}
