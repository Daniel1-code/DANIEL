using System;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>
    /// Traduit le ferraillage d'une semelle en <see cref="ReinforcementPlan"/>.
    ///
    /// Repere local : X et Y sont les axes de la semelle en plan, l'origine est au centre de
    /// la semelle sur la sous-face, Z remonte.
    /// </summary>
    public static class FootingPlanBuilder
    {
        public static ReinforcementPlan Build(FootingData footing, FootingReinforcement r,
                                              string markPrefix)
        {
            var plan = new ReinforcementPlan();
            if (r == null || r.BottomX == null || r.BottomX.DiameterMm <= 0) return plan;

            int mark = 0;

            // Les barres s'arretent a l'enrobage lateral.
            double halfX = footing.WidthXMm / 2.0 - r.CoverMm;
            double halfY = footing.WidthYMm / 2.0 - r.CoverMm;

            // --- Nappe inferieure ---
            // Premier lit : barres suivant X, a l'enrobage inferieur.
            double zBottomX = r.CoverMm + r.BottomX.DiameterMm / 2.0;
            // Second lit : barres suivant Y, posees au-dessus du premier.
            double zBottomY = r.CoverMm + r.BottomX.DiameterMm + r.BottomY.DiameterMm / 2.0;

            mark++;
            plan.Add(MeshAlongX(footing, r.BottomX, zBottomX, halfX, halfY,
                                "Nappe inferieure // X", Mark(markPrefix, mark)));
            mark++;
            plan.Add(MeshAlongY(footing, r.BottomY, zBottomY, halfX, halfY,
                                "Nappe inferieure // Y", Mark(markPrefix, mark)));

            // --- Nappe superieure ---
            if (r.HasTopMesh)
            {
                double zTopX = footing.ThicknessMm - r.CoverMm - r.TopX.DiameterMm / 2.0;
                double zTopY = zTopX - r.TopX.DiameterMm / 2.0 - r.TopY.DiameterMm / 2.0;

                mark++;
                plan.Add(MeshAlongX(footing, r.TopX, zTopX, halfX, halfY,
                                    "Nappe superieure // X", Mark(markPrefix, mark)));
                mark++;
                plan.Add(MeshAlongY(footing, r.TopY, zTopY, halfX, halfY,
                                    "Nappe superieure // Y", Mark(markPrefix, mark)));
            }

            // --- Attentes du poteau ---
            foreach (RebarGroup starter in Starters(footing, r, markPrefix, ref mark))
            {
                plan.Add(starter);
            }

            return plan;
        }

        /// <summary>
        /// Repartit <paramref name="count"/> attentes sur le pourtour du poteau : les
        /// quatre angles d'abord, puis des barres intermediaires reparties sur les faces,
        /// exactement comme les aciers longitudinaux du poteau qu'elles prolongent.
        /// </summary>
        private static System.Collections.Generic.List<LocalPoint> PerimeterPositions(
            int count, double insetXMm, double insetYMm, double zMm)
        {
            var positions = new System.Collections.Generic.List<LocalPoint>();
            if (count <= 0) return positions;

            if (count < 4)
            {
                // Moins de quatre barres : on les pose aux angles disponibles.
                var corners = new[]
                {
                    new LocalPoint(-insetXMm, -insetYMm, zMm),
                    new LocalPoint(insetXMm, -insetYMm, zMm),
                    new LocalPoint(insetXMm, insetYMm, zMm),
                    new LocalPoint(-insetXMm, insetYMm, zMm)
                };
                for (int i = 0; i < count; i++) positions.Add(corners[i]);
                return positions;
            }

            // Nombre de barres par face, angles compris : 2 nx + 2 ny - 4 = count.
            int nx = 2, ny = 2;
            while (2 * nx + 2 * ny - 4 < count)
            {
                if (nx <= ny) nx++;
                else ny++;
            }

            for (int i = 0; i < nx; i++)
            {
                double t = nx > 1 ? (double)i / (nx - 1) : 0.5;
                double x = -insetXMm + 2.0 * insetXMm * t;
                positions.Add(new LocalPoint(x, -insetYMm, zMm));
                positions.Add(new LocalPoint(x, insetYMm, zMm));
            }

            for (int j = 1; j < ny - 1; j++)
            {
                double t = (double)j / (ny - 1);
                double y = -insetYMm + 2.0 * insetYMm * t;
                positions.Add(new LocalPoint(-insetXMm, y, zMm));
                positions.Add(new LocalPoint(insetXMm, y, zMm));
            }

            return positions;
        }

        private static string Mark(string prefix, int index)
        {
            return string.Format("{0}-B{1:00}", prefix, index);
        }

        /// <summary>Nappe dont les barres filent suivant X et se repetent suivant Y.</summary>
        private static RebarGroup MeshAlongX(FootingData footing, MeshSelection mesh, double zMm,
                                             double halfXMm, double halfYMm, string label,
                                             string mark)
        {
            int count = mesh.CountOver(2.0 * halfYMm);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double start = -arrayLength / 2.0;

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
            group.Path.Add(PlanSegment.Line(new LocalPoint(-halfXMm, start, zMm),
                                            new LocalPoint(halfXMm, start, zMm)));
            group.WithNormal(LocalVector.AxisY);
            return group;
        }

        /// <summary>Nappe dont les barres filent suivant Y et se repetent suivant X.</summary>
        private static RebarGroup MeshAlongY(FootingData footing, MeshSelection mesh, double zMm,
                                             double halfXMm, double halfYMm, string label,
                                             string mark)
        {
            int count = mesh.CountOver(2.0 * halfXMm);
            double arrayLength = (count - 1) * mesh.SpacingMm;
            double start = -arrayLength / 2.0;

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
            group.Path.Add(PlanSegment.Line(new LocalPoint(start, -halfYMm, zMm),
                                            new LocalPoint(start, halfYMm, zMm)));
            group.WithNormal(LocalVector.AxisX);
            return group;
        }

        /// <summary>
        /// Attentes du poteau : barres en L, avec un retour horizontal en pied qui les ancre
        /// dans la semelle, et une partie verticale qui depasse pour le recouvrement.
        /// </summary>
        private static System.Collections.Generic.List<RebarGroup> Starters(
            FootingData footing, FootingReinforcement r, string markPrefix, ref int mark)
        {
            var groups = new System.Collections.Generic.List<RebarGroup>();
            if (r.StarterBarCount <= 0 || r.StarterBarDiameterMm <= 0) return groups;

            // Les attentes suivent le pourtour du poteau, a l'interieur de sa section.
            double insetX = footing.ColumnWidthXMm / 2.0 - 40.0;
            double insetY = footing.ColumnWidthYMm / 2.0 - 40.0;
            if (insetX <= 0 || insetY <= 0) return groups;

            double zBottom = r.CoverMm + r.BottomX.DiameterMm + r.BottomY.DiameterMm
                             + r.StarterBarDiameterMm / 2.0;
            double zTop = footing.ThicknessMm + r.StarterProjectionMm;

            System.Collections.Generic.List<LocalPoint> positions =
                PerimeterPositions(r.StarterBarCount, insetX, insetY, zBottom);

            int index = 0;
            foreach (LocalPoint position in positions)
            {
                if (index >= r.StarterBarCount) break;
                index++;
                mark++;

                // Retour horizontal dirige vers le centre de la semelle.
                double direction = position.X >= 0 ? -1.0 : 1.0;
                var heel = new LocalPoint(position.X + direction * r.StarterReturnMm,
                                          position.Y, position.Z);

                var group = new RebarGroup
                {
                    Kind = RebarKind.Longitudinal,
                    DiameterMm = r.StarterBarDiameterMm,
                    Label = string.Format("Attente {0}/{1} HA{2:0}", index, r.StarterBarCount,
                                          r.StarterBarDiameterMm),
                    Mark = Mark(markPrefix, mark),
                    Layout = ArrayLayout.Single()
                };
                group.Path.Add(PlanSegment.Line(heel, position));
                group.Path.Add(PlanSegment.Line(position,
                    new LocalPoint(position.X, position.Y, zTop)));
                // La barre est contenue dans un plan vertical parallele a X : sa normale est Y.
                group.WithNormal(LocalVector.AxisY);
                groups.Add(group);
            }

            return groups;
        }
    }
}
