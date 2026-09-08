using System;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une volee en <see cref="ReinforcementPlan"/>.
    ///
    /// REPERE LOCAL, ET IL EST HORIZONTAL. L'origine est au pied de la volee, sur la
    /// sous-face, au bord gauche. X est horizontal et suit le sens de la MONTEE, Y est
    /// horizontal et traverse la volee, Z est vertical.
    ///
    /// La pente n'est donc PAS dans le repere : elle est dans les coordonnees Z des barres.
    /// C'est un choix, et il doit rester tenu de bout en bout — incliner le repere tout en
    /// gardant ces Z appliquerait la pente deux fois.
    ///
    ///   sous-face      z = m x            pour 0 &lt;= x &lt;= projection de la volee
    ///                  z = m x_pli        au-dela, sur le palier
    ///   face superieure z = m x + t / cos alpha        (volee)
    ///                   z = m x_pli + t_palier         (palier)
    ///
    /// L'ENROBAGE D'UNE FACE INCLINEE N'EST PAS UN DECALAGE VERTICAL. Pour une distance
    /// normale c a un plan de pente m, le decalage vertical vaut c / cos alpha. Prendre c
    /// tout court donne un enrobage reel de c cos alpha, soit 15 % de moins a 31 degres.
    ///
    /// LA REPETITION D'UN GROUPE EST UNE TRANSLATION RECTILIGNE. Revit repartit les copies
    /// le long de la NORMALE de la barre, en ligne droite. Un groupe unique ne peut donc
    /// pas suivre une volee inclinee puis un palier horizontal : les nappes transversales
    /// sont separees en un groupe de volee, reparti suivant la pente, et un groupe de
    /// palier, reparti horizontalement.
    /// </summary>
    public static class StairPlanBuilder
    {
        public static ReinforcementPlan Build(StairData stair, StairReinforcement r,
                                              string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BottomMain == null || r.BottomMain.DiameterMm <= 0) return plan;

            double yStart = r.CoverMm;
            double yEnd = stair.WidthMm - r.CoverMm;
            if (stair.SpanMm <= 0 || yEnd <= yStart) return plan;

            var g = new Geometry(stair, r);
            StairDetailingRules rules = r.Rules ?? new StairDetailingRules();
            int mark = 0;

            if (stair.SpanKind == StairSpanKind.TransverseBetweenWalls)
            {
                BuildTransverseSpanning(plan, stair, r, g, yStart, yEnd, markPrefix, ref mark);
                return plan;
            }

            // --- Nappe inferieure porteuse ---
            // Le NOEUD decide de la forme des barres porteuses, et le detail retenu est un
            // parametre : croisement des nappes, ou epingle diagonale separee.
            if (g.HasLanding && r.HasKneeJoint
                && rules.KneeJoint == KneeJointDetail.CrossedBars)
            {
                mark++;
                plan.Add(FlightBarCrossingTheJoint(stair, r, g, yStart, yEnd,
                                                   Mark(markPrefix, mark)));
                mark++;
                plan.Add(LandingBarCrossingTheJoint(stair, r, g, yStart, yEnd,
                                                    Mark(markPrefix, mark)));
            }
            else if (g.HasLanding && r.HasKneeJoint)
            {
                mark++;
                plan.Add(BottomBarStoppingAtTheJoint(stair, r, g, yStart, yEnd, true,
                                                     Mark(markPrefix, mark)));
                mark++;
                plan.Add(BottomBarStoppingAtTheJoint(stair, r, g, yStart, yEnd, false,
                                                     Mark(markPrefix, mark)));
                mark++;
                plan.Add(CornerHairpin(stair, r, g, yStart, yEnd, Mark(markPrefix, mark)));
            }
            else
            {
                mark++;
                plan.Add(BottomMainAlongSpan(stair, r, g, yStart, yEnd, Mark(markPrefix, mark)));
            }

            // --- Repartition inferieure : un groupe par tronçon ---
            // La repartition se pose AU-DESSUS des porteuses par defaut : ce sont elles qui
            // doivent avoir la plus grande hauteur utile. Le choix inverse existe et se
            // regle, il coute simplement de la hauteur utile.
            double mainOffset = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double distributionOffset = rules.DistributionAboveMainBars
                ? r.CoverMm + r.BottomMain.DiameterMm + r.BottomTransverse.DiameterMm / 2.0
                : r.CoverMm + r.BottomTransverse.DiameterMm / 2.0;

            mark++;
            plan.Add(FlightTransverse(r.BottomTransverse, stair, r, g, yStart, yEnd,
                                      distributionOffset,
                                      "Repartition inferieure, volee", Mark(markPrefix, mark)));
            if (g.HasLanding)
            {
                mark++;
                plan.Add(LandingTransverse(r.BottomTransverse, stair, r, g, yStart, yEnd,
                                           distributionOffset,
                                           "Repartition inferieure, palier",
                                           Mark(markPrefix, mark)));
            }

            AddTopBars(plan, stair, r, g, yStart, yEnd, markPrefix, ref mark);
            return plan;
        }

        // ------------------------------------------------------------------
        // Reperes verticaux, tous derives des faces reelles du beton
        // ------------------------------------------------------------------

        /// <summary>
        /// Les altitudes de reference de la volee. Toutes passent par cos alpha : c'est ce
        /// facteur, et lui seul, qui distingue le ferraillage d'une volee de celui d'une
        /// dalle.
        /// </summary>
        private sealed class Geometry
        {
            private readonly StairData _stair;

            public double Slope { get; private set; }
            public double Cos { get; private set; }
            public double GoingMm { get; private set; }
            public double SpanMm { get; private set; }
            public bool HasLanding { get; private set; }

            /// <summary>Decalage VERTICAL correspondant a l'enrobage normal a la pente.</summary>
            public double BottomLevelOffset { get; private set; }

            /// <summary>Epaisseur de paillasse vue verticalement, t / cos alpha.</summary>
            public double VerticalWaistMm { get; private set; }

            public Geometry(StairData stair, StairReinforcement r)
            {
                _stair = stair;
                Slope = stair.SlopeTangent;
                Cos = Math.Max(stair.SlopeCosine, 0.05);
                GoingMm = Math.Min(stair.TotalGoingMm, stair.SpanMm);
                SpanMm = stair.SpanMm;
                HasLanding = SpanMm - GoingMm > 1.0;
                VerticalWaistMm = stair.WaistThicknessMm / Cos;
                BottomLevelOffset = (r.CoverMm + r.BottomMain.DiameterMm / 2.0) / Cos;
            }

            /// <summary>Sous-face du beton a l'abscisse x.</summary>
            public double SoffitZ(double x)
            {
                return Math.Min(x, GoingMm) * Slope;
            }

            /// <summary>Face superieure du beton a l'abscisse x.</summary>
            public double TopFaceZ(double x)
            {
                return x <= GoingMm
                    ? x * Slope + VerticalWaistMm
                    : GoingMm * Slope + _stair.LandingThicknessMm;
            }

            /// <summary>Altitude d'une nappe inferieure a l'abscisse x, enrobage normal compris.</summary>
            public double BottomBarZ(double x, double extraOffsetMm)
            {
                double offset = x <= GoingMm ? extraOffsetMm / Cos : extraOffsetMm;
                return SoffitZ(x) + offset;
            }

            /// <summary>Altitude d'une nappe superieure a l'abscisse x.</summary>
            public double TopBarZ(double x, double coverAndHalfBarMm)
            {
                double offset = x <= GoingMm ? coverAndHalfBarMm / Cos : coverAndHalfBarMm;
                return TopFaceZ(x) - offset;
            }

            /// <summary>Longueur developpee suivant la pente, entre deux abscisses.</summary>
            public double SlopeLength(double fromX, double toX)
            {
                return Math.Max(toX - fromX, 0.0) / Cos;
            }

            /// <summary>Direction de repetition suivant la pente, dans le repere local.</summary>
            public LocalVector SlopeDirection { get { return new LocalVector(1.0, 0.0, Slope); } }
        }

        // ------------------------------------------------------------------
        // Nappe inferieure porteuse
        // ------------------------------------------------------------------

        private static RebarGroup BottomMainAlongSpan(StairData stair, StairReinforcement r,
                                                      Geometry g, double yStart, double yEnd,
                                                      string mark)
        {
            double x0 = r.CoverMm;
            double x1 = g.SpanMm - r.CoverMm;
            if (x1 <= x0) return null;

            var group = NewAcrossWidthGroup(r.BottomMain, "Nappe inferieure porteuse", mark,
                                            yStart, yEnd, out double y);

            double half = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double xJoint = Math.Min(g.GoingMm, x1);

            if (x1 - xJoint > 1.0)
            {
                SetPath(group,
                    new LocalPoint(x0, y, g.BottomBarZ(x0, half)),
                    new LocalPoint(xJoint, y, g.BottomBarZ(xJoint, half)),
                    new LocalPoint(xJoint, y, g.SoffitZ(xJoint) + half),
                    new LocalPoint(x1, y, g.SoffitZ(x1) + half));
            }
            else
            {
                SetPath(group,
                    new LocalPoint(x0, y, g.BottomBarZ(x0, half)),
                    new LocalPoint(xJoint, y, g.BottomBarZ(xJoint, half)));
            }
            return group;
        }

        /// <summary>
        /// Barre de volee au noeud : elle monte le long de la sous-face jusqu'au pli, puis
        /// traverse l'epaisseur et s'ancre dans la FACE SUPERIEURE du palier. Elle ne suit
        /// jamais le pli — voir la note de tete de classe.
        /// </summary>
        private static RebarGroup FlightBarCrossingTheJoint(StairData stair, StairReinforcement r,
                                                            Geometry g, double yStart, double yEnd,
                                                            string mark)
        {
            double half = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double x0 = r.CoverMm;
            double xJoint = g.GoingMm;
            double zStart = g.BottomBarZ(x0, half);
            double zJoint = g.BottomBarZ(xJoint, half);
            double zLandingTop = g.TopFaceZ(g.SpanMm) - half;
            double anchorEnd = Math.Min(xJoint + r.KneeAnchorageMm, g.SpanMm - r.CoverMm);

            var group = NewAcrossWidthGroup(r.BottomMain,
                "Nappe inferieure de volee, croisee au noeud", mark, yStart, yEnd, out double y);

            SetPath(group,
                new LocalPoint(x0, y, zStart),
                new LocalPoint(xJoint, y, zJoint),
                new LocalPoint(xJoint, y, zLandingTop),
                new LocalPoint(anchorEnd, y, zLandingTop));
            return group;
        }

        /// <summary>Symetrique : la barre de palier s'ancre dans la face superieure de la volee.</summary>
        private static RebarGroup LandingBarCrossingTheJoint(StairData stair, StairReinforcement r,
                                                             Geometry g, double yStart,
                                                             double yEnd, string mark)
        {
            double half = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double xJoint = g.GoingMm;
            double xEnd = g.SpanMm - r.CoverMm;
            double zLanding = g.SoffitZ(xJoint) + half;

            // L'ancrage remonte dans la face superieure de la paillasse : sa projection
            // horizontale vaut l_bd cos alpha.
            double anchorRun = r.KneeAnchorageMm * g.Cos;
            double xAnchor = Math.Max(xJoint - anchorRun, r.CoverMm);

            var group = NewAcrossWidthGroup(r.BottomMain,
                "Nappe inferieure de palier, croisee au noeud", mark, yStart, yEnd, out double y);

            SetPath(group,
                new LocalPoint(xEnd, y, zLanding),
                new LocalPoint(xJoint, y, zLanding),
                new LocalPoint(xJoint, y, g.TopBarZ(xJoint, half)),
                new LocalPoint(xAnchor, y, g.TopBarZ(xAnchor, half)));
            return group;
        }

        /// <summary>
        /// Variante a EPINGLE DIAGONALE. Les deux nappes inferieures s'arretent au pli au
        /// lieu de se croiser, et une epingle separee franchit l'angle rentrant.
        ///
        /// C'est l'autre detail admis pour un angle rentrant tendu. Il coute une barre de
        /// plus et demande un placement soigne, mais il evite de faire remonter les nappes
        /// dans la face opposee, ce qui encombre le noeud quand les diametres sont gros.
        /// Comme le croisement, son rendement n'est pas calcule.
        /// </summary>
        private static RebarGroup BottomBarStoppingAtTheJoint(StairData stair,
                                                              StairReinforcement r, Geometry g,
                                                              double yStart, double yEnd,
                                                              bool onFlight, string mark)
        {
            double half = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double xJoint = g.GoingMm;

            var group = NewAcrossWidthGroup(r.BottomMain,
                onFlight ? "Nappe inferieure de volee, arretee au noeud"
                         : "Nappe inferieure de palier, arretee au noeud",
                mark, yStart, yEnd, out double y);

            if (onFlight)
            {
                SetPath(group,
                    new LocalPoint(r.CoverMm, y, g.BottomBarZ(r.CoverMm, half)),
                    new LocalPoint(xJoint, y, g.BottomBarZ(xJoint, half)));
            }
            else
            {
                double zLanding = g.SoffitZ(xJoint) + half;
                SetPath(group,
                    new LocalPoint(g.SpanMm - r.CoverMm, y, zLanding),
                    new LocalPoint(xJoint, y, zLanding));
            }
            return group;
        }

        /// <summary>
        /// L'epingle diagonale du noeud : deux branches ancrees l_bd de part et d'autre du
        /// pli, chacune suivant la sous-face de son propre element.
        /// </summary>
        private static RebarGroup CornerHairpin(StairData stair, StairReinforcement r,
                                                Geometry g, double yStart, double yEnd,
                                                string mark)
        {
            double half = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            double xJoint = g.GoingMm;
            double reach = r.KneeAnchorageMm;

            // Cote volee, l'ancrage se mesure suivant la pente : sa projection vaut
            // l_bd cos alpha.
            double xFlight = Math.Max(xJoint - reach * g.Cos, r.CoverMm);
            double xLanding = Math.Min(xJoint + reach, g.SpanMm - r.CoverMm);

            var group = NewAcrossWidthGroup(r.BottomMain, "Epingle diagonale du noeud",
                                            mark, yStart, yEnd, out double y);

            // L'epingle passe SOUS les nappes arretees, au plus pres de la sous-face.
            double hairpinOffset = r.CoverMm + r.BottomMain.DiameterMm / 2.0;
            SetPath(group,
                new LocalPoint(xFlight, y, g.BottomBarZ(xFlight, hairpinOffset)),
                new LocalPoint(xJoint, y, g.BottomBarZ(xJoint, hairpinOffset)),
                new LocalPoint(xLanding, y, g.SoffitZ(xJoint) + hairpinOffset));
            return group;
        }

        // ------------------------------------------------------------------
        // Nappes transversales : un groupe par tronçon
        // ------------------------------------------------------------------

        /// <summary>
        /// Barres en travers de la volee, repetees SUIVANT LA PENTE. La direction de
        /// repetition est la tangente inclinee, et la longueur du reseau est mesuree le long
        /// de cette tangente : c'est ainsi que les barres se posent sur le coffrage.
        /// </summary>
        private static RebarGroup FlightTransverse(MeshSelection mesh, StairData stair,
                                                   StairReinforcement r, Geometry g,
                                                   double yStart, double yEnd,
                                                   double verticalOffsetMm,
                                                   string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double x0 = r.CoverMm;
            double x1 = g.GoingMm - r.CoverMm;
            if (x1 <= x0) return null;

            double availableAlongSlope = g.SlopeLength(x0, x1);
            int count = mesh.CountOver(availableAlongSlope);
            if (count <= 0) return null;
            double arrayLength = (count - 1) * mesh.SpacingMm;

            // Le reseau est centre sur le troncon, en longueur developpee.
            double startAlongSlope = (availableAlongSlope - arrayLength) / 2.0;
            double xFirst = x0 + startAlongSlope * g.Cos;
            double z = g.SoffitZ(xFirst) + verticalOffsetMm / g.Cos;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("{0} - {1} ({2} barres)", label, mesh.Label, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(g.SlopeDirection, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(xFirst, yStart, z),
                                            new LocalPoint(xFirst, yEnd, z)));
            // La normale est aussi la direction de repetition : Revit distribue le long
            // de la normale. Les deux doivent donc etre le meme vecteur.
            group.WithNormal(g.SlopeDirection);
            return group;
        }

        /// <summary>Barres en travers du palier, repetees horizontalement.</summary>
        private static RebarGroup LandingTransverse(MeshSelection mesh, StairData stair,
                                                    StairReinforcement r, Geometry g,
                                                    double yStart, double yEnd,
                                                    double verticalOffsetMm,
                                                    string label, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double x0 = g.GoingMm;
            double x1 = g.SpanMm - r.CoverMm;
            double available = x1 - x0;
            if (available <= 0) return null;

            int count = mesh.CountOver(available);
            if (count <= 0) return null;
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double xFirst = x0 + (available - arrayLength) / 2.0;
            double z = g.SoffitZ(g.GoingMm) + verticalOffsetMm;

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
            group.Path.Add(PlanSegment.Line(new LocalPoint(xFirst, yStart, z),
                                            new LocalPoint(xFirst, yEnd, z)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }

        // ------------------------------------------------------------------
        // Chapeaux
        // ------------------------------------------------------------------

        private static void AddTopBars(ReinforcementPlan plan, StairData stair,
                                       StairReinforcement r, Geometry g,
                                       double yStart, double yEnd,
                                       string markPrefix, ref int mark)
        {
            if (!r.HasTopReinforcement || r.TopBarLengthMm <= 0) return;

            StairDetailingRules rules = r.Rules ?? new StairDetailingRules();
            double half = r.CoverMm + r.TopMain.DiameterMm / 2.0;

            // Nappe superieure CONTINUE : une seule barre d'appui a appui, au lieu de deux
            // chapeaux qui s'arretent. C'est le choix quand l'encastrement reel dans les
            // paliers n'est pas quantifie et qu'on prefere ne pas dependre d'une longueur
            // d'arret.
            if (rules.TopBarExtent == TopBarExtentMode.FullSpan || rules.ContinuousTopMesh)
            {
                mark++;
                AddContinuousTopMesh(plan, stair, r, g, yStart, yEnd, half,
                                     Mark(markPrefix, mark));
                mark++;
                plan.Add(TopTransverse(r.TopTransverse, stair, r, g, yStart, yEnd,
                                       half + r.TopMain.DiameterMm / 2.0
                                       + r.TopTransverse.DiameterMm / 2.0,
                                       Mark(markPrefix, mark)));
                return;
            }

            double reach = Math.Min(r.TopBarLengthMm, g.SpanMm / 2.0);

            // Appui bas : le chapeau suit la pente, il est dans la volee.
            mark++;
            var lower = NewAcrossWidthGroup(r.TopMain, "Chapeau appui bas",
                                            Mark(markPrefix, mark), yStart, yEnd, out double y1);
            double xLowEnd = Math.Min(reach, g.GoingMm);
            SetPath(lower,
                new LocalPoint(r.CoverMm, y1, g.TopBarZ(r.CoverMm, half)),
                new LocalPoint(xLowEnd, y1, g.TopBarZ(xLowEnd, half)));
            plan.Add(lower);

            // Appui haut : horizontal s'il tombe dans le palier, incline sinon. Un segment
            // a Z constant pose sur une volee passerait au-dessus du beton.
            mark++;
            double xEnd = g.SpanMm - r.CoverMm;
            double xStart = Math.Max(xEnd - reach, r.CoverMm);
            var upper = NewAcrossWidthGroup(r.TopMain, "Chapeau appui haut",
                                            Mark(markPrefix, mark), yStart, yEnd, out double y2);
            if (g.HasLanding && xStart >= g.GoingMm)
            {
                upper.Path.Add(PlanSegment.Line(
                    new LocalPoint(xStart, y2, g.TopBarZ(xStart, half)),
                    new LocalPoint(xEnd, y2, g.TopBarZ(xEnd, half))));
            }
            else if (g.HasLanding)
            {
                // Il empiete sur la volee : incline, puis le decrochement du raccord, puis
                // horizontal sur le palier.
                SetPath(upper,
                    new LocalPoint(xStart, y2, g.TopBarZ(xStart, half)),
                    new LocalPoint(g.GoingMm, y2, g.TopBarZ(g.GoingMm, half)),
                    new LocalPoint(g.GoingMm, y2, g.TopBarZ(g.SpanMm, half)),
                    new LocalPoint(xEnd, y2, g.TopBarZ(xEnd, half)));
            }
            else
            {
                SetPath(upper,
                    new LocalPoint(xStart, y2, g.TopBarZ(xStart, half)),
                    new LocalPoint(xEnd, y2, g.TopBarZ(xEnd, half)));
            }
            plan.Add(upper);

            // Repartition superieure : referencee a la FACE SUPERIEURE, pas a la sous-face.
            mark++;
            plan.Add(TopTransverse(r.TopTransverse, stair, r, g, yStart, yEnd,
                                   half + r.TopMain.DiameterMm / 2.0
                                   + r.TopTransverse.DiameterMm / 2.0,
                                   Mark(markPrefix, mark)));
        }

        /// <summary>Nappe superieure filant d'un appui a l'autre, en suivant les faces.</summary>
        private static void AddContinuousTopMesh(ReinforcementPlan plan, StairData stair,
                                                 StairReinforcement r, Geometry g,
                                                 double yStart, double yEnd, double half,
                                                 string mark)
        {
            double x0 = r.CoverMm;
            double x1 = g.SpanMm - r.CoverMm;
            if (x1 <= x0) return;

            var group = NewAcrossWidthGroup(r.TopMain, "Nappe superieure continue", mark,
                                            yStart, yEnd, out double y);

            if (g.HasLanding)
            {
                SetPath(group,
                    new LocalPoint(x0, y, g.TopBarZ(x0, half)),
                    new LocalPoint(g.GoingMm, y, g.TopBarZ(g.GoingMm, half)),
                    new LocalPoint(g.GoingMm, y, g.TopBarZ(g.SpanMm, half)),
                    new LocalPoint(x1, y, g.TopBarZ(x1, half)));
            }
            else
            {
                SetPath(group,
                    new LocalPoint(x0, y, g.TopBarZ(x0, half)),
                    new LocalPoint(x1, y, g.TopBarZ(x1, half)));
            }
            plan.Add(group);
        }

        private static RebarGroup TopTransverse(MeshSelection mesh, StairData stair,
                                                StairReinforcement r, Geometry g,
                                                double yStart, double yEnd,
                                                double depthBelowTopMm, string mark)
        {
            if (mesh == null || mesh.DiameterMm <= 0) return null;

            double x0 = r.CoverMm;
            double x1 = g.GoingMm - r.CoverMm;
            if (x1 <= x0) return null;

            double availableAlongSlope = g.SlopeLength(x0, x1);
            int count = mesh.CountOver(availableAlongSlope);
            if (count <= 0) return null;
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double xFirst = x0 + (availableAlongSlope - arrayLength) / 2.0 * g.Cos;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("Repartition superieure - {0} ({1} barres)",
                                      mesh.Label, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(g.SlopeDirection, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(
                PlanSegment.Line(new LocalPoint(xFirst, yStart, g.TopBarZ(xFirst, depthBelowTopMm)),
                                 new LocalPoint(xFirst, yEnd, g.TopBarZ(xFirst, depthBelowTopMm))));
            group.WithNormal(g.SlopeDirection);
            return group;
        }

        // ------------------------------------------------------------------
        // Portee transversale
        // ------------------------------------------------------------------

        /// <summary>
        /// La volee franchit sa LARGEUR : les barres porteuses traversent la volee et sont
        /// repetees suivant la pente ; la repartition file suivant la pente et est repetee
        /// en travers.
        /// </summary>
        private static void BuildTransverseSpanning(ReinforcementPlan plan, StairData stair,
                                                    StairReinforcement r, Geometry g,
                                                    double yStart, double yEnd,
                                                    string markPrefix, ref int mark)
        {
            mark++;
            plan.Add(FlightTransverse(r.BottomMain, stair, r, g, yStart, yEnd,
                                      r.CoverMm + r.BottomMain.DiameterMm / 2.0,
                                      "Nappe inferieure porteuse (sens transversal)",
                                      Mark(markPrefix, mark)));

            // La repartition file suivant la pente, sur la projection de la VOLEE et non
            // sur la portee, qui vaut ici la largeur.
            MeshSelection mesh = r.BottomTransverse;
            if (mesh == null || mesh.DiameterMm <= 0) return;

            double x0 = r.CoverMm;
            double x1 = Math.Max(g.GoingMm - r.CoverMm, x0 + 1.0);
            double offset = r.CoverMm + r.BottomMain.DiameterMm + mesh.DiameterMm / 2.0;

            double availableWidth = yEnd - yStart;
            int count = mesh.CountOver(availableWidth);
            if (count <= 0) return;
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double y = yStart + (availableWidth - arrayLength) / 2.0;

            mark++;
            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("Repartition longitudinale - {0} ({1} barres)",
                                      mesh.Label, count),
                Mark = Mark(markPrefix, mark),
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(x0, y, g.BottomBarZ(x0, offset)),
                                            new LocalPoint(x1, y, g.BottomBarZ(x1, offset))));
            group.WithNormal(LocalVector.AxisY);
            plan.Add(group);
        }

        // ------------------------------------------------------------------
        // Utilitaires
        // ------------------------------------------------------------------

        /// <summary>
        /// Groupe de barres filant suivant la portee, repetees EN TRAVERS de la volee. La
        /// repetition est horizontale : c'est le seul cas ou elle l'est legitimement, parce
        /// que la largeur d'une volee est horizontale.
        /// </summary>
        private static RebarGroup NewAcrossWidthGroup(MeshSelection mesh, string label,
                                                      string mark, double yStart, double yEnd,
                                                      out double y)
        {
            double available = yEnd - yStart;
            int count = mesh.CountOver(available);
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

        /// <summary>
        /// Construit le trajet a partir d'une SUITE DE POINTS, et non de segments poses un
        /// a un. Revit exige une chaine de courbes continue : deux segments dont l'un ne
        /// commence pas ou l'autre finit font echouer la creation de la barre. Passer par
        /// les points rend la continuite structurelle plutot que surveillee.
        ///
        /// Les points confondus sont ignores : au raccord volee-palier, l'enrobage normal
        /// a la pente et l'enrobage vertical du palier ne donnent pas la meme altitude, et
        /// le court segment vertical qui les relie est reel.
        /// </summary>
        private static void SetPath(RebarGroup group, params LocalPoint[] points)
        {
            const double tolerance = 0.05;
            LocalPoint previous = points[0];
            for (int i = 1; i < points.Length; i++)
            {
                LocalPoint current = points[i];
                double dx = current.X - previous.X;
                double dy = current.Y - previous.Y;
                double dz = current.Z - previous.Z;
                if (Math.Sqrt(dx * dx + dy * dy + dz * dz) < tolerance) continue;

                group.Path.Add(PlanSegment.Line(previous, current));
                previous = current;
            }
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }
    }
}
