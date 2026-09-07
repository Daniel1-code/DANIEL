using System;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une dalle en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X suit la portee depuis le nu du premier appui, Y traverse le
    /// panneau, l'origine est au coin du panneau sur la sous-face, Z remonte.
    /// </summary>
    public static class SlabPlanBuilder
    {
        public static ReinforcementPlan Build(SlabData slab, SlabReinforcement r, string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BottomMain == null || r.BottomMain.DiameterMm <= 0) return plan;

            int mark = 0;

            // Les barres s'arretent a l'enrobage sur chaque rive.
            double xStart = r.CoverMm;
            double xEnd = slab.SpanMm - r.CoverMm;
            double yStart = r.CoverMm;
            double yEnd = slab.WidthMm - r.CoverMm;
            if (xEnd <= xStart || yEnd <= yStart) return plan;

            // --- Nappe inferieure ---
            double zMain = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double zTransverse = r.CoverMm + r.BottomMain.DiameterMm
                                 + r.BottomTransverse.DiameterMm / 2.0;

            mark++;
            plan.Add(AlongSpan(r.BottomMain, xStart, xEnd, yStart, yEnd, zMain,
                               "Nappe inferieure porteuse", Mark(markPrefix, mark)));
            mark++;
            plan.Add(AcrossSpan(r.BottomTransverse, xStart, xEnd, yStart, yEnd, zTransverse,
                                "Repartition inferieure", Mark(markPrefix, mark)));

            // --- Chapeaux ---
            if (r.HasTopReinforcement)
            {
                double zTop = slab.ThicknessMm - r.CoverMm - r.TopMain.DiameterMm / 2.0;
                double zTopTransverse = zTop - r.TopMain.DiameterMm / 2.0
                                        - r.TopTransverse.DiameterMm / 2.0;

                // Les chapeaux ne courent pas sur toute la travee : ils couvrent la zone
                // de moment negatif de part et d'autre de chaque appui.
                double reach = Math.Min(Math.Max(r.TopBarLengthMm, 0.0), (xEnd - xStart) / 2.0);
                if (reach > 0)
                {
                    if (slab.SpanKind == SlabSpanKind.Cantilever)
                    {
                        // Une console est tendue sur toute sa longueur : le chapeau file.
                        mark++;
                        plan.Add(AlongSpan(r.TopMain, xStart, xEnd, yStart, yEnd, zTop,
                                           "Chapeau de console", Mark(markPrefix, mark)));
                    }
                    else
                    {
                        mark++;
                        plan.Add(AlongSpan(r.TopMain, xStart, xStart + reach, yStart, yEnd, zTop,
                                           "Chapeau appui gauche", Mark(markPrefix, mark)));
                        mark++;
                        plan.Add(AlongSpan(r.TopMain, xEnd - reach, xEnd, yStart, yEnd, zTop,
                                           "Chapeau appui droit", Mark(markPrefix, mark)));
                    }

                    mark++;
                    plan.Add(AcrossSpan(r.TopTransverse, xStart, xEnd, yStart, yEnd,
                                        zTopTransverse, "Repartition superieure",
                                        Mark(markPrefix, mark)));
                }
            }

            return plan;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }

        /// <summary>Barres filant suivant la portee, repetees en travers du panneau.</summary>
        private static RebarGroup AlongSpan(MeshSelection mesh, double xStart, double xEnd,
                                            double yStart, double yEnd, double zMm,
                                            string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0 || xEnd <= xStart) return null;

            double available = yEnd - yStart;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double y = yStart + (available - arrayLength) / 2.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("{0} - {1} ({2} barres)", label, mesh.Label, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(xStart, y, zMm),
                                            new LocalPoint(xEnd, y, zMm)));
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>Barres traversant le panneau, repetees le long de la portee.</summary>
        private static RebarGroup AcrossSpan(MeshSelection mesh, double xStart, double xEnd,
                                             double yStart, double yEnd, double zMm,
                                             string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0 || yEnd <= yStart) return null;

            double available = xEnd - xStart;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double x = xStart + (available - arrayLength) / 2.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("{0} - {1} ({2} barres)", label, mesh.Label, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisX, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, yStart, zMm),
                                            new LocalPoint(x, yEnd, zMm)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }
    }
}
