using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage decide pour un poteau en un <see cref="ReinforcementPlan"/>
    /// exprime en coordonnees locales. Aucune dependance a Revit : le meme plan sert au
    /// modeleur, a l'apercu graphique et au quantitatif.
    /// </summary>
    public static class ColumnPlanBuilder
    {
        public static ReinforcementPlan Build(ColumnData column, ColumnReinforcement r, string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BarDiameterMm <= 0) return plan;

            int barMark = 0;
            int tieMark = 0;

            double zBottom = r.BottomOffsetMm;
            double zTop = column.HeightMm + r.TopExtensionMm;

            if (column.Shape == SectionShape.Circular)
            {
                foreach (BarPosition bar in ColumnLayoutGeometry.Bars(column, r))
                {
                    barMark++;
                    plan.Add(new RebarGroup
                    {
                        Kind = RebarKind.Longitudinal,
                        DiameterMm = r.BarDiameterMm,
                        Label = string.Format("Barre {0}", barMark),
                        Mark = string.Format("{0}-B{1:00}", markPrefix, barMark),
                        Layout = ArrayLayout.Single(),
                        Path =
                        {
                            PlanSegment.Line(new LocalPoint(bar.XMm, bar.YMm, zBottom),
                                             new LocalPoint(bar.XMm, bar.YMm, zTop))
                        }
                    }.WithNormal(LocalVector.AxisX));
                }

                double radius = ColumnLayoutGeometry.StirrupRadius(column, r);
                foreach (StirrupZone zone in StirrupZones.Compute(column, r))
                {
                    tieMark++;
                    plan.Add(CircularTie(column, r, zone, radius,
                        string.Format("{0}-T{1:00}", markPrefix, tieMark)));
                }
                return plan;
            }

            // --- Section rectangulaire : lits longitudinaux ---
            double x0 = ColumnLayoutGeometry.BarHalfSpanX(column, r);
            double y0 = ColumnLayoutGeometry.BarHalfSpanY(column, r);

            barMark++;
            plan.Add(BarRow(r, -x0, -y0, zBottom, zTop, LocalVector.AxisX, r.BarsAlongX, 2.0 * x0,
                            "Lit inferieur", string.Format("{0}-B{1:00}", markPrefix, barMark)));
            barMark++;
            plan.Add(BarRow(r, -x0, y0, zBottom, zTop, LocalVector.AxisX, r.BarsAlongX, 2.0 * x0,
                            "Lit superieur", string.Format("{0}-B{1:00}", markPrefix, barMark)));

            int intermediate = r.BarsAlongY - 2;
            if (intermediate > 0)
            {
                double pitch = ColumnLayoutGeometry.PitchY(column, r);
                double start = -y0 + pitch;
                double arrayLength = (intermediate - 1) * pitch;
                barMark++;
                plan.Add(BarRow(r, -x0, start, zBottom, zTop, LocalVector.AxisY, intermediate,
                                arrayLength, "Face gauche",
                                string.Format("{0}-B{1:00}", markPrefix, barMark)));
                barMark++;
                plan.Add(BarRow(r, x0, start, zBottom, zTop, LocalVector.AxisY, intermediate,
                                arrayLength, "Face droite",
                                string.Format("{0}-B{1:00}", markPrefix, barMark)));
            }

            // --- Cadres ---
            double sx = ColumnLayoutGeometry.StirrupHalfX(column, r);
            double sy = ColumnLayoutGeometry.StirrupHalfY(column, r);
            List<StirrupZone> zones = StirrupZones.Compute(column, r);
            if (sx > 0 && sy > 0)
            {
                foreach (StirrupZone zone in zones)
                {
                    tieMark++;
                    plan.Add(RectangularTie(r, zone, sx, sy,
                        string.Format("{0}-T{1:00}", markPrefix, tieMark)));
                }
            }

            // --- Epingles ---
            foreach (double x in ColumnLayoutGeometry.CrossTieXPositions(column, r))
            {
                foreach (StirrupZone zone in zones)
                {
                    tieMark++;
                    plan.Add(StraightTie(r, zone,
                        new LocalPoint(x, -y0, zone.StartMm), new LocalPoint(x, y0, zone.StartMm),
                        "Epingle //Y", string.Format("{0}-T{1:00}", markPrefix, tieMark)));
                }
            }
            foreach (double y in ColumnLayoutGeometry.CrossTieYPositions(column, r))
            {
                foreach (StirrupZone zone in zones)
                {
                    tieMark++;
                    plan.Add(StraightTie(r, zone,
                        new LocalPoint(-x0, y, zone.StartMm), new LocalPoint(x0, y, zone.StartMm),
                        "Epingle //X", string.Format("{0}-T{1:00}", markPrefix, tieMark)));
                }
            }

            return plan;
        }

        private static RebarGroup BarRow(ColumnReinforcement r, double xMm, double yMm,
                                         double zBottomMm, double zTopMm, LocalVector direction,
                                         int count, double arrayLengthMm, string label, string mark)
        {
            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = r.BarDiameterMm,
                Label = string.Format("{0} - {1} HA{2:0}", label, count, r.BarDiameterMm),
                Mark = mark,
                Layout = count > 1 && arrayLengthMm > 1.0
                    ? ArrayLayout.FixedNumber(direction, count, arrayLengthMm)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(xMm, yMm, zBottomMm),
                                            new LocalPoint(xMm, yMm, zTopMm)));
            // Barre verticale : la normale est horizontale, celle de la direction de repetition.
            group.WithNormal(direction);
            return group;
        }

        private static RebarGroup RectangularTie(ColumnReinforcement r, StirrupZone zone,
                                                 double halfX, double halfY, string mark)
        {
            double z = zone.StartMm;
            var p1 = new LocalPoint(-halfX, -halfY, z);
            var p2 = new LocalPoint(halfX, -halfY, z);
            var p3 = new LocalPoint(halfX, halfY, z);
            var p4 = new LocalPoint(-halfX, halfY, z);

            var group = new RebarGroup
            {
                Kind = RebarKind.Stirrup,
                DiameterMm = r.StirrupDiameterMm,
                IsClosedLoop = true,
                WithHooks = true,
                Label = string.Format("Cadre {0} - HA{1:0} e={2:0}",
                                      zone.Label, r.StirrupDiameterMm, zone.SpacingMm),
                Mark = mark,
                Layout = ArrayLayout.MaximumSpacing(LocalVector.AxisZ, zone.SpacingMm, zone.LengthMm,
                                                    zone.IncludeFirst, zone.IncludeLast)
            };
            group.Path.Add(PlanSegment.Line(p1, p2));
            group.Path.Add(PlanSegment.Line(p2, p3));
            group.Path.Add(PlanSegment.Line(p3, p4));
            group.Path.Add(PlanSegment.Line(p4, p1));
            group.WithNormal(LocalVector.AxisZ);
            return group;
        }

        private static RebarGroup CircularTie(ColumnData column, ColumnReinforcement r,
                                              StirrupZone zone, double radiusMm, string mark)
        {
            var center = new LocalPoint(0, 0, zone.StartMm);
            var group = new RebarGroup
            {
                Kind = RebarKind.Stirrup,
                DiameterMm = r.StirrupDiameterMm,
                IsClosedLoop = true,
                WithHooks = true,
                Label = string.Format("Cerce {0} - HA{1:0} e={2:0}",
                                      zone.Label, r.StirrupDiameterMm, zone.SpacingMm),
                Mark = mark,
                Layout = ArrayLayout.MaximumSpacing(LocalVector.AxisZ, zone.SpacingMm, zone.LengthMm,
                                                    zone.IncludeFirst, zone.IncludeLast)
            };
            // Deux demi-cercles : Revit n'accepte pas un arc de 2 pi en une seule courbe.
            group.Path.Add(PlanSegment.Arc(center, radiusMm, 0.0, Math.PI));
            group.Path.Add(PlanSegment.Arc(center, radiusMm, Math.PI, 2.0 * Math.PI));
            group.WithNormal(LocalVector.AxisZ);
            return group;
        }

        private static RebarGroup StraightTie(ColumnReinforcement r, StirrupZone zone,
                                              LocalPoint start, LocalPoint end, string label, string mark)
        {
            var group = new RebarGroup
            {
                Kind = RebarKind.CrossTie,
                DiameterMm = r.StirrupDiameterMm,
                IsClosedLoop = false,
                WithHooks = true,
                Label = string.Format("{0} {1} - HA{2:0} e={3:0}",
                                      label, zone.Label, r.StirrupDiameterMm, zone.SpacingMm),
                Mark = mark,
                Layout = ArrayLayout.MaximumSpacing(LocalVector.AxisZ, zone.SpacingMm, zone.LengthMm,
                                                    zone.IncludeFirst, zone.IncludeLast)
            };
            group.Path.Add(PlanSegment.Line(start, end));
            group.WithNormal(LocalVector.AxisZ);
            return group;
        }
    }
}
