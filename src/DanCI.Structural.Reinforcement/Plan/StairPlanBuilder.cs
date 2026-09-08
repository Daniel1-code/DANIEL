using System;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une volee en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X suit la projection horizontale de la portee depuis l'appui bas,
    /// Y traverse la volee, Z remonte. La sous-face part de z = 0 a l'appui bas, monte
    /// avec la pente jusqu'au pli, puis reste horizontale sur le palier.
    ///
    /// LE NOEUD VOLEE-PALIER EST LE POINT DUR DE CE PLAN. La sous-face y forme un angle
    /// rentrant, et la nappe inferieure y est tendue. Une barre qui suivrait le pli
    /// developperait, a l'interieur du coude, une resultante dirigee vers l'exterieur du
    /// beton : elle ferait sauter l'enrobage et le noeud perdrait sa resistance avant la
    /// section courante. Le detail correct est de faire SE CROISER les barres et de les
    /// ancrer chacune dans la face opposee. C'est ce que ce constructeur dessine, et il ne
    /// propose aucune variante qui suivrait le pli.
    /// </summary>
    public static class StairPlanBuilder
    {
        public static ReinforcementPlan Build(StairData stair, StairReinforcement r,
                                              string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BottomMain == null || r.BottomMain.DiameterMm <= 0) return plan;

            double span = stair.SpanMm;
            double yStart = r.CoverMm;
            double yEnd = stair.WidthMm - r.CoverMm;
            if (span <= 0 || yEnd <= yStart) return plan;

            int mark = 0;

            if (stair.SpanKind == StairSpanKind.TransverseBetweenWalls)
            {
                // La volee porte en travers : les barres principales traversent la volee,
                // il n'y a ni pli ni noeud dans le sens porteur.
                mark++;
                plan.Add(StraightRun(r.BottomMain, stair, r, yStart, yEnd,
                                     "Nappe inferieure porteuse (sens transversal)",
                                     Mark(markPrefix, mark)));
                mark++;
                plan.Add(AlongSpanBars(r.BottomTransverse, stair, r, yStart, yEnd,
                                       r.BottomMain.DiameterMm,
                                       "Repartition longitudinale", Mark(markPrefix, mark)));
                return plan;
            }

            double going = Math.Min(stair.TotalGoingMm, span);
            bool hasLanding = r.HasKneeJoint && span - going > 1.0;

            if (!hasLanding)
            {
                mark++;
                plan.Add(AlongSpanBars(r.BottomMain, stair, r, yStart, yEnd, 0.0,
                                       "Nappe inferieure porteuse", Mark(markPrefix, mark)));
                mark++;
                plan.Add(TransverseBars(r.BottomTransverse, stair, r, yStart, yEnd,
                                        r.BottomMain.DiameterMm,
                                        "Repartition inferieure", Mark(markPrefix, mark)));
                AddTopBars(plan, stair, r, yStart, yEnd, markPrefix, ref mark);
                return plan;
            }

            // --- Noeud volee-palier : les deux nappes inferieures se croisent ---
            mark++;
            plan.Add(FlightBarCrossingTheJoint(r, stair, yStart, yEnd,
                                               Mark(markPrefix, mark)));
            mark++;
            plan.Add(LandingBarCrossingTheJoint(r, stair, yStart, yEnd,
                                                Mark(markPrefix, mark)));
            mark++;
            plan.Add(TransverseBars(r.BottomTransverse, stair, r, yStart, yEnd,
                                    r.BottomMain.DiameterMm,
                                    "Repartition inferieure", Mark(markPrefix, mark)));
            AddTopBars(plan, stair, r, yStart, yEnd, markPrefix, ref mark);
            return plan;
        }

        // ------------------------------------------------------------------
        // Le noeud
        // ------------------------------------------------------------------

        /// <summary>
        /// Barre de la volee : elle monte le long de la sous-face jusqu'au pli, puis
        /// traverse l'epaisseur et s'ancre dans la FACE SUPERIEURE du palier. Elle ne suit
        /// jamais le pli.
        /// </summary>
        private static RebarGroup FlightBarCrossingTheJoint(StairReinforcement r, StairData stair,
                                                            double yStart, double yEnd, string mark)
        {
            double going = stair.TotalGoingMm;
            double slope = stair.SlopeTangent;
            double zBottom = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double zJoint = zBottom + going * slope;
            double zTopOfLanding = zJoint + stair.LandingThicknessMm - 2.0 * r.CoverMm
                                   - r.BottomMain.DiameterMm;
            double anchorEnd = Math.Min(going + r.KneeAnchorageMm, stair.SpanMm - r.CoverMm);

            var group = NewGroup(r.BottomMain, "Nappe inferieure de volee, croisee au noeud",
                                 mark, yStart, yEnd, out double y, out int count);

            group.Path.Add(PlanSegment.Line(new LocalPoint(r.CoverMm, y, zBottom),
                                            new LocalPoint(going, y, zJoint)));
            // Traversee de l'epaisseur au droit du pli, puis ancrage en face superieure.
            group.Path.Add(PlanSegment.Line(new LocalPoint(going, y, zJoint),
                                            new LocalPoint(going, y, zTopOfLanding)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(going, y, zTopOfLanding),
                                            new LocalPoint(anchorEnd, y, zTopOfLanding)));
            return group;
        }

        /// <summary>
        /// Barre du palier : symetrique de la precedente. Elle vient de l'appui haut,
        /// atteint le pli, puis s'ancre dans la face SUPERIEURE de la volee.
        /// </summary>
        private static RebarGroup LandingBarCrossingTheJoint(StairReinforcement r, StairData stair,
                                                             double yStart, double yEnd, string mark)
        {
            double going = stair.TotalGoingMm;
            double slope = stair.SlopeTangent;
            double zBottom = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double zJoint = zBottom + going * slope;

            // Ancrage remonte dans la face superieure de la paillasse, mesure le long de
            // la pente : sa projection horizontale vaut l_bd x cos alpha.
            double anchorRun = r.KneeAnchorageMm * stair.SlopeCosine;
            double xAnchor = Math.Max(going - anchorRun, r.CoverMm);
            double waistNormal = stair.WaistThicknessMm - 2.0 * r.CoverMm - r.BottomMain.DiameterMm;
            double zRise = waistNormal / Math.Max(stair.SlopeCosine, 0.05);

            var group = NewGroup(r.BottomMain, "Nappe inferieure de palier, croisee au noeud",
                                 mark, yStart, yEnd, out double y, out int count);

            group.Path.Add(PlanSegment.Line(new LocalPoint(stair.SpanMm - r.CoverMm, y, zJoint),
                                            new LocalPoint(going, y, zJoint)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(going, y, zJoint),
                                            new LocalPoint(going, y, zJoint + zRise)));
            group.Path.Add(PlanSegment.Line(
                new LocalPoint(going, y, zJoint + zRise),
                new LocalPoint(xAnchor, y, zJoint + zRise - (going - xAnchor) * slope)));
            return group;
        }

        // ------------------------------------------------------------------
        // Nappes courantes
        // ------------------------------------------------------------------

        private static void AddTopBars(ReinforcementPlan plan, StairData stair,
                                       StairReinforcement r, double yStart, double yEnd,
                                       string markPrefix, ref int mark)
        {
            if (!r.HasTopReinforcement || r.TopBarLengthMm <= 0) return;

            double slope = stair.SlopeTangent;
            double zTop = r.CoverMm + r.TopMain.DiameterMm / 2.0
                          + stair.WaistThicknessMm / Math.Max(stair.SlopeCosine, 0.05)
                          - 2.0 * r.CoverMm;
            double reach = Math.Min(r.TopBarLengthMm, stair.SpanMm / 2.0);

            mark++;
            var lower = NewGroup(r.TopMain, "Chapeau appui bas", Mark(markPrefix, mark),
                                 yStart, yEnd, out double y1, out int c1);
            lower.Path.Add(PlanSegment.Line(new LocalPoint(r.CoverMm, y1, zTop),
                                            new LocalPoint(reach, y1, zTop + reach * slope)));
            plan.Add(lower);

            mark++;
            double xEnd = stair.SpanMm - r.CoverMm;
            double zAtEnd = zTop + stair.TotalGoingMm * slope;
            var upper = NewGroup(r.TopMain, "Chapeau appui haut", Mark(markPrefix, mark),
                                 yStart, yEnd, out double y2, out int c2);
            upper.Path.Add(PlanSegment.Line(new LocalPoint(xEnd - reach, y2, zAtEnd),
                                            new LocalPoint(xEnd, y2, zAtEnd)));
            plan.Add(upper);

            mark++;
            plan.Add(TransverseBars(r.TopTransverse, stair, r, yStart, yEnd,
                                    -r.TopMain.DiameterMm,
                                    "Repartition superieure", Mark(markPrefix, mark)));
        }

        /// <summary>Barres suivant la portee, sur la sous-face, avec le pli du palier.</summary>
        private static RebarGroup AlongSpanBars(MeshSelection mesh, StairData stair,
                                                StairReinforcement r, double yStart, double yEnd,
                                                double zOffsetMm, string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double zBottom = r.CoverMm + mesh.DiameterMm / 2.0 + zOffsetMm;
            double going = Math.Min(stair.TotalGoingMm, stair.SpanMm);
            double zJoint = zBottom + going * stair.SlopeTangent;

            var group = NewGroup(mesh, label, mark, yStart, yEnd, out double y, out int count);
            group.Path.Add(PlanSegment.Line(new LocalPoint(r.CoverMm, y, zBottom),
                                            new LocalPoint(going, y, zJoint)));
            if (stair.SpanMm - going > 1.0)
            {
                group.Path.Add(PlanSegment.Line(
                    new LocalPoint(going, y, zJoint),
                    new LocalPoint(stair.SpanMm - r.CoverMm, y, zJoint)));
            }
            return group;
        }

        /// <summary>Barres droites traversant la volee, pour la portee transversale.</summary>
        private static RebarGroup StraightRun(MeshSelection mesh, StairData stair,
                                              StairReinforcement r, double yStart, double yEnd,
                                              string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double z = r.CoverMm + mesh.DiameterMm / 2.0;
            double available = stair.TotalGoingMm - 2.0 * r.CoverMm;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double x = r.CoverMm + (available - arrayLength) / 2.0;
            double zAtX = z + x * stair.SlopeTangent;

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
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, yStart, zAtX),
                                            new LocalPoint(x, yEnd, zAtX)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }

        /// <summary>Barres de repartition, en travers de la volee, repetees le long de la portee.</summary>
        private static RebarGroup TransverseBars(MeshSelection mesh, StairData stair,
                                                 StairReinforcement r, double yStart, double yEnd,
                                                 double stackOffsetMm, string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double available = stair.SpanMm - 2.0 * r.CoverMm;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double x = r.CoverMm + (available - arrayLength) / 2.0;

            double z = r.CoverMm + mesh.DiameterMm / 2.0 + stackOffsetMm
                       + Math.Min(x, stair.TotalGoingMm) * stair.SlopeTangent;

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
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, yStart, z),
                                            new LocalPoint(x, yEnd, z)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        private static RebarGroup NewGroup(MeshSelection mesh, string label, string mark,
                                           double yStart, double yEnd,
                                           out double y, out int count)
        {
            double available = yEnd - yStart;
            count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            y = yStart + (available - arrayLength) / 2.0;

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
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }
    }
}
