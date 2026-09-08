using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une semelle filante en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X traverse la semelle, Y suit sa longueur depuis l'extremite de
    /// depart, Z remonte. L'origine est au milieu de la largeur, a l'extremite de depart,
    /// sur la sous-face.
    /// </summary>
    public static class StripFootingPlanBuilder
    {
        public static ReinforcementPlan Build(StripFootingData footing,
                                              StripFootingReinforcement r, string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.Transverse == null || r.Transverse.DiameterMm <= 0) return plan;

            int mark = 0;

            double halfWidth = footing.WidthMm / 2.0 - r.CoverMm;
            double yStart = r.CoverMm;
            double yEnd = footing.LengthMm - r.CoverMm;
            if (halfWidth <= 0 || yEnd <= yStart) return plan;

            // Premier lit : les transversales, qui reprennent la flexion des consoles.
            double zTransverse = r.CoverMm + r.Transverse.DiameterMm / 2.0;
            // Second lit : les longitudinales de repartition, posees dessus.
            double zLongitudinal = r.CoverMm + r.Transverse.DiameterMm
                                   + r.Longitudinal.DiameterMm / 2.0;

            mark++;
            plan.Add(Transverse(r.Transverse, halfWidth, yStart, yEnd, zTransverse,
                                r.TransverseNeedsHook, "Armatures transversales inferieures",
                                Mark(markPrefix, mark)));
            mark++;
            plan.Add(Longitudinal(r.Longitudinal, halfWidth, yStart, yEnd, zLongitudinal,
                                  "Repartition longitudinale", Mark(markPrefix, mark)));

            if (r.HasTopMesh)
            {
                double zTop = footing.ThicknessMm - r.CoverMm - r.TopTransverse.DiameterMm / 2.0;
                mark++;
                plan.Add(Transverse(r.TopTransverse, halfWidth, yStart, yEnd, zTop, false,
                                    "Armatures transversales superieures",
                                    Mark(markPrefix, mark)));
            }

            if (r.HasStarters)
            {
                foreach (RebarGroup starter in Starters(footing, r, yStart, yEnd,
                                                        markPrefix, ref mark))
                {
                    plan.Add(starter);
                }
            }

            return plan;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }

        /// <summary>Barres traversant la semelle, repetees sur sa longueur.</summary>
        private static RebarGroup Transverse(MeshSelection mesh, double halfWidthMm,
                                             double yStart, double yEnd, double zMm,
                                             bool withHooks, string label, string mark)
        {
            double available = yEnd - yStart;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double y = yStart + (available - arrayLength) / 2.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                WithHooks = withHooks,
                Label = string.Format("{0} - {1} ({2} barres){3}", label, mesh.Label, count,
                                      withHooks ? " avec crochets d'extremite" : ""),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(-halfWidthMm, y, zMm),
                                            new LocalPoint(halfWidthMm, y, zMm)));
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>Barres filant sur la longueur, repetees en travers.</summary>
        private static RebarGroup Longitudinal(MeshSelection mesh, double halfWidthMm,
                                               double yStart, double yEnd, double zMm,
                                               string label, string mark)
        {
            double available = 2.0 * halfWidthMm;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double x = -arrayLength / 2.0;

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

        /// <summary>
        /// Attentes du voile : deux files en L, une par parement, avec un retour horizontal
        /// en pied qui les ancre dans la semelle.
        /// </summary>
        private static List<RebarGroup> Starters(StripFootingData footing,
                                                 StripFootingReinforcement r,
                                                 double yStart, double yEnd,
                                                 string markPrefix, ref int mark)
        {
            var groups = new List<RebarGroup>();

            // Les attentes suivent les parements du voile, a l'interieur de son epaisseur.
            double inset = footing.WallThicknessMm / 2.0 - r.CoverMm
                           - r.StarterDiameterMm / 2.0;
            if (inset <= 0) return groups;

            double zBottom = r.CoverMm + r.Transverse.DiameterMm + r.Longitudinal.DiameterMm
                             + r.StarterDiameterMm / 2.0;
            double zTop = footing.ThicknessMm + r.StarterProjectionMm;

            double available = yEnd - yStart;
            int count = Math.Max((int)Math.Floor(available / Math.Max(r.StarterSpacingMm, 1.0)), 1);
            double arrayLength = (count - 1) * r.StarterSpacingMm;
            double y = yStart + (available - arrayLength) / 2.0;

            foreach (double side in new[] { -1.0, 1.0 })
            {
                mark++;
                double x = side * inset;
                // Le retour horizontal est dirige vers l'axe de la semelle.
                var heel = new LocalPoint(x - side * r.StarterReturnMm, y, zBottom);

                var group = new RebarGroup
                {
                    Kind = RebarKind.Longitudinal,
                    DiameterMm = r.StarterDiameterMm,
                    Label = string.Format("Attentes de voile HA{0:0} e={1:0} - file {2} ({3} barres)",
                                          r.StarterDiameterMm, r.StarterSpacingMm,
                                          side < 0 ? "interieure" : "exterieure", count),
                    Mark = Mark(markPrefix, mark),
                    Layout = count > 1
                        ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, arrayLength)
                        : ArrayLayout.Single()
                };
                group.Path.Add(PlanSegment.Line(heel, new LocalPoint(x, y, zBottom)));
                group.Path.Add(PlanSegment.Line(new LocalPoint(x, y, zBottom),
                                                new LocalPoint(x, y, zTop)));
                // La barre est contenue dans un plan vertical transversal : normale Y.
                group.WithNormal(LocalVector.AxisY);
                groups.Add(group);
            }

            return groups;
        }
    }
}
