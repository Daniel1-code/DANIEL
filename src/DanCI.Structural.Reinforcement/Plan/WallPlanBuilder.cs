using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'un voile en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X suit la longueur du voile depuis son extremite de depart,
    /// Y traverse l'epaisseur, Z monte depuis la base. L'origine est au coin du voile,
    /// au milieu de l'epaisseur.
    /// </summary>
    public static class WallPlanBuilder
    {
        public static ReinforcementPlan Build(WallData wall, WallReinforcement r, string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.VerticalPerFace == null || r.VerticalPerFace.DiameterMm <= 0)
            {
                return plan;
            }

            int mark = 0;

            double halfThickness = wall.ThicknessMm / 2.0;
            // Les aciers verticaux sont a l'exterieur, les horizontaux a l'interieur :
            // c'est l'ordre de pose sur chantier, et il fixe les hauteurs utiles.
            double verticalOffset = halfThickness - r.CoverMm - r.VerticalPerFace.DiameterMm / 2.0;
            double horizontalOffset = halfThickness - r.CoverMm - r.VerticalPerFace.DiameterMm
                                      - r.HorizontalPerFace.DiameterMm / 2.0;
            if (verticalOffset <= 0 || horizontalOffset <= 0) return plan;

            double xStart = r.CoverMm;
            double xEnd = wall.LengthMm - r.CoverMm;
            double zStart = r.CoverMm;
            double zEnd = wall.ClearHeightMm - r.CoverMm;
            if (xEnd <= xStart || zEnd <= zStart) return plan;

            // --- Aciers verticaux, une nappe par parement ---
            foreach (double face in new[] { -1.0, 1.0 })
            {
                mark++;
                plan.Add(VerticalBars(r.VerticalPerFace, xStart, xEnd, face * verticalOffset,
                    zStart, zEnd,
                    face < 0 ? "Aciers verticaux nappe interieure" : "Aciers verticaux nappe exterieure",
                    Mark(markPrefix, mark)));
            }

            // --- Aciers horizontaux, une nappe par parement ---
            foreach (double face in new[] { -1.0, 1.0 })
            {
                mark++;
                plan.Add(HorizontalBars(r.HorizontalPerFace, xStart, xEnd,
                    face * horizontalOffset, zStart, zEnd,
                    face < 0 ? "Aciers horizontaux nappe interieure" : "Aciers horizontaux nappe exterieure",
                    Mark(markPrefix, mark)));
            }

            // --- Barres de rive ---
            if (r.HasEdgeBars)
            {
                foreach (RebarGroup edge in EdgeBars(r, xStart, xEnd, verticalOffset,
                                                     zStart, zEnd, markPrefix, ref mark))
                {
                    plan.Add(edge);
                }
            }

            // --- Epingles de liaison entre nappes ---
            if (r.HasLinks)
            {
                foreach (RebarGroup link in Links(wall, r, markPrefix, ref mark))
                {
                    plan.Add(link);
                }
            }

            return plan;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }

        /// <summary>Barres verticales d'une nappe, repetees le long du voile.</summary>
        private static RebarGroup VerticalBars(MeshSelection mesh, double xStart, double xEnd,
                                               double yMm, double zStart, double zEnd,
                                               string label, string mark)
        {
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
            group.Path.Add(PlanSegment.Line(new LocalPoint(x, yMm, zStart),
                                            new LocalPoint(x, yMm, zEnd)));
            // La barre est verticale dans un plan parallele au voile : sa normale est Y.
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>Barres horizontales d'une nappe, repetees sur la hauteur.</summary>
        private static RebarGroup HorizontalBars(MeshSelection mesh, double xStart, double xEnd,
                                                 double yMm, double zStart, double zEnd,
                                                 string label, string mark)
        {
            double available = zEnd - zStart;
            int count = mesh.CountOver(available);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double z = zStart + (available - arrayLength) / 2.0;

            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = mesh.DiameterMm,
                Label = string.Format("{0} - {1} ({2} barres)", label, mesh.Label, count),
                Mark = mark,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisZ, count, arrayLength)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(xStart, yMm, z),
                                            new LocalPoint(xEnd, yMm, z)));
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>
        /// Barres de rive : la concentration d'acier a chaque extremite du voile, qui
        /// reprend la traction due a la flexion dans le plan. Elles sont posees par paires,
        /// une barre par nappe, en s'enfoncant depuis chaque extremite.
        /// </summary>
        private static List<RebarGroup> EdgeBars(WallReinforcement r, double xStart, double xEnd,
                                                 double offsetMm, double zStart, double zEnd,
                                                 string markPrefix, ref int mark)
        {
            var groups = new List<RebarGroup>();
            int pairs = Math.Max(r.EdgeBarCount / 2, 1);
            double pitch = Math.Max(r.EdgeBarDiameterMm * 4.0, 100.0);

            foreach (double edge in new[] { xStart, xEnd })
            {
                double direction = edge <= xStart ? 1.0 : -1.0;
                for (int i = 0; i < pairs; i++)
                {
                    double x = edge + direction * i * pitch;
                    if (x <= xStart - 1e-6 || x >= xEnd + 1e-6) break;

                    mark++;
                    var group = new RebarGroup
                    {
                        Kind = RebarKind.Longitudinal,
                        DiameterMm = r.EdgeBarDiameterMm,
                        Label = string.Format("Barre de rive {0}/{1} HA{2:0} ({3})",
                                              i + 1, pairs, r.EdgeBarDiameterMm,
                                              direction > 0 ? "extremite depart" : "extremite fin"),
                        Mark = Mark(markPrefix, mark),
                        // Une barre par nappe : le reseau traverse l'epaisseur.
                        Layout = ArrayLayout.FixedNumber(LocalVector.AxisY, 2, 2.0 * offsetMm)
                    };
                    group.Path.Add(PlanSegment.Line(new LocalPoint(x, -offsetMm, zStart),
                                                    new LocalPoint(x, -offsetMm, zEnd)));
                    group.WithNormal(LocalVector.AxisX);
                    groups.Add(group);
                }
            }

            return groups;
        }

        /// <summary>
        /// Epingles de liaison entre les deux nappes, article 9.6.4. Elles ne sont pas
        /// decoratives : elles empechent les barres verticales comprimees de flamber vers
        /// l'exterieur en faisant eclater l'enrobage.
        /// </summary>
        private static List<RebarGroup> Links(WallData wall, WallReinforcement r,
                                              string markPrefix, ref int mark)
        {
            var groups = new List<RebarGroup>();

            // Maillage carre donnant le nombre d'epingles au metre carre demande.
            double pitch = 1000.0 / Math.Sqrt(Math.Max(r.LinksPerSquareMetre, 1.0));
            double halfThickness = wall.ThicknessMm / 2.0;
            double reach = halfThickness - r.CoverMm - r.VerticalPerFace.DiameterMm / 2.0;
            if (reach <= 0) return groups;

            double xStart = r.CoverMm + pitch / 2.0;
            double xEnd = wall.LengthMm - r.CoverMm;
            double zStart = r.CoverMm + pitch / 2.0;
            double zEnd = wall.ClearHeightMm - r.CoverMm;
            if (xStart >= xEnd || zStart >= zEnd) return groups;

            int columns = Math.Max((int)Math.Floor((xEnd - xStart) / pitch) + 1, 1);
            int rows = Math.Max((int)Math.Floor((zEnd - zStart) / pitch) + 1, 1);
            double arrayLength = (columns - 1) * pitch;

            // Une nappe d'epingles par niveau : le reseau lineaire de Revit ne va que dans
            // une direction, un quadrillage se construit donc rangee par rangee.
            for (int row = 0; row < rows; row++)
            {
                double z = zStart + row * pitch;
                mark++;

                var group = new RebarGroup
                {
                    Kind = RebarKind.CrossTie,
                    DiameterMm = r.LinkDiameterMm,
                    WithHooks = true,
                    Label = string.Format(
                        "Epingles de liaison HA{0:0} - rangee {1}/{2}, {3} epingles a e = {4:0} mm",
                        r.LinkDiameterMm, row + 1, rows, columns, pitch),
                    Mark = Mark(markPrefix, mark),
                    Layout = columns > 1
                        ? ArrayLayout.FixedNumber(LocalVector.AxisX, columns, arrayLength)
                        : ArrayLayout.Single()
                };
                // Une epingle traverse l'epaisseur, d'une nappe a l'autre.
                group.Path.Add(PlanSegment.Line(new LocalPoint(xStart, -reach, z),
                                                new LocalPoint(xStart, reach, z)));
                group.WithNormal(LocalVector.AxisZ);
                groups.Add(group);
            }

            return groups;
        }
    }
}
