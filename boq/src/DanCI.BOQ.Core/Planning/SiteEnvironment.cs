using System;
using System.Collections.Generic;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Etat du chantier a une date, pour l'environnement visuel du Studio 4D.</summary>
    public sealed class SiteState
    {
        /// <summary>Avancement du gros oeuvre (fondations, structure, maconnerie) : 0..1 (hauteur des echafaudages).</summary>
        public double Structure { get; set; }
        /// <summary>Grue montee (des les fondations, demontee a la fin du gros oeuvre).</summary>
        public bool Crane { get; set; }
        /// <summary>Echafaudages (gros oeuvre et facades ; retires pendant les finitions).</summary>
        public bool Scaffold { get; set; }
        /// <summary>Orientation de la fleche de la grue (degres).</summary>
        public double SlewDeg { get; set; }

        /// <summary>Etat du chantier d'apres l'avancement affiche de chaque tache.</summary>
        public static SiteState At(IEnumerable<PlanTask> tasks, IReadOnlyDictionary<string, double> progress, double slewDeg)
        {
            double done = 0, total = 0, found = 0, finishes = 0, finishesTotal = 0;
            foreach (PlanTask t in tasks ?? new List<PlanTask>())
            {
                double p = progress != null && progress.TryGetValue(t.Id, out double v) ? v : 0;
                double w = Math.Max(1, t.DurationDays);
                if (t.Lot == "02" || t.Lot == "03" || t.Lot == "04" || t.Lot == "05")
                {
                    done += p * w;
                    total += w;
                }
                if (t.Lot == "02") found = Math.Max(found, p);
                if (t.Lot == "08") { finishes += p * w; finishesTotal += w; }
            }
            double structure = total > 0 ? done / total : 1;
            double fin = finishesTotal > 0 ? finishes / finishesTotal : 0;
            return new SiteState
            {
                Structure = structure,
                Crane = (found > 0 || total == 0 ? true : false) && structure < 0.999,
                Scaffold = structure > 0.02 && fin < 0.6,
                SlewDeg = slewDeg
            };
        }
    }

    /// <summary>
    /// Environnement de chantier autour du batiment (dessin seulement, jamais dans le modele) : terrain,
    /// plateforme, palissade et portail, grue a tour qui pivote, echafaudages qui montent avec le gros
    /// oeuvre, bungalows et stock de materiaux. Unites du modele (pieds) ; emprise = boite du batiment.
    /// </summary>
    public static class SiteEnvironment
    {
        private const double M = 3.28084; // pieds par metre

        public static List<(TriMesh Mesh, string Color)> Build(V3 min, V3 max, SiteState state)
        {
            var parts = new List<(TriMesh, string)>();
            state = state ?? new SiteState();
            double dx = max.X - min.X, dy = max.Y - min.Y, h = Math.Max(3 * M, max.Z - min.Z);
            double z0 = min.Z;
            double cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
            double size = Math.Max(dx, dy);

            // 1. Terrain (gazon) et plateforme de chantier (terre).
            double g = size * 0.9 + 18 * M; // terrain modere : le cadrage reste sur le batiment
            parts.Add((Box(cx - g, cy - g, z0 - 0.35, cx + g, cy + g, z0 - 0.25), "#6F8F4E"));
            double pad = 7 * M;
            double px0 = min.X - pad, py0 = min.Y - pad, px1 = max.X + pad, py1 = max.Y + pad;
            parts.Add((Box(px0, py0, z0 - 0.25, px1, py1, z0 - 0.18), "#9C8463"));

            // 2. Palissade (2 m), portail de 5 m au milieu du cote sud.
            double fh = 2 * M, ft = 0.12, gate = 5 * M;
            var fence = new TriMesh();
            Append(fence, Box(px0, py1 - ft, z0, px1, py1, z0 + fh));
            Append(fence, Box(px0, py0, z0, px0 + ft, py1, z0 + fh));
            Append(fence, Box(px1 - ft, py0, z0, px1, py1, z0 + fh));
            Append(fence, Box(px0, py0, z0, cx - gate / 2, py0 + ft, z0 + fh));
            Append(fence, Box(cx + gate / 2, py0, z0, px1, py0 + ft, z0 + fh));
            parts.Add((fence, "#D8DCE1"));
            var band = new TriMesh(); // bandeau bleu en haut de la palissade
            Append(band, Box(px0 - 0.02, py1 - ft - 0.02, z0 + fh - 0.9, px1 + 0.02, py1 + 0.02, z0 + fh));
            Append(band, Box(px0 - 0.02, py0 - 0.02, z0 + fh - 0.9, cx - gate / 2, py0 + ft + 0.02, z0 + fh));
            Append(band, Box(cx + gate / 2, py0 - 0.02, z0 + fh - 0.9, px1 + 0.02, py0 + ft + 0.02, z0 + fh));
            parts.Add((band, "#1F5FA8"));

            // 3. Bungalows de chantier (2 conteneurs empiles) et stock de materiaux, pres du portail.
            double bx = px1 - 14 * M, by = py0 + 1.5 * M;
            parts.Add((Box(bx, by, z0, bx + 6 * M, by + 2.4 * M, z0 + 2.6 * M), "#F2F2EE"));
            parts.Add((Box(bx, by, z0 + 2.6 * M, bx + 6 * M, by + 2.4 * M, z0 + 5.2 * M), "#EDEDE8"));
            parts.Add((Box(bx - 0.05, by - 0.05, z0 + 5.2 * M, bx + 6 * M + 0.05, by + 2.4 * M + 0.05, z0 + 5.4 * M), "#2E6FB7"));
            var stock = new TriMesh();
            for (int i = 0; i < 4; i++) Append(stock, Box(px0 + 2 * M + i * 1.6 * M, py0 + 1.5 * M, z0, px0 + 3.2 * M + i * 1.6 * M, py0 + 2.7 * M, z0 + (0.8 + 0.2 * (i % 2)) * M));
            parts.Add((stock, "#B9A27A"));

            // 4. Grue a tour : mat, cabine, fleche et contre-fleche qui pivotent, lest, cable.
            if (state.Crane)
            {
                double mx = min.X - 4.5 * M, my = cy, mast = 1.6 * M, top = z0 + h + 6 * M;
                var steel = new TriMesh();
                // Mat en treillis : 4 membrures + entretoises tous les 3 m.
                double e = 0.22;
                foreach (var (ox, oy) in new[] { (0.0, 0.0), (mast, 0.0), (mast, mast), (0.0, mast) })
                    Append(steel, Box(mx - mast / 2 + ox - e, my - mast / 2 + oy - e, z0, mx - mast / 2 + ox + e, my - mast / 2 + oy + e, top));
                for (double z = z0 + 3 * M; z < top; z += 3 * M)
                {
                    Append(steel, Box(mx - mast / 2, my - mast / 2 - e, z, mx + mast / 2, my - mast / 2 + e, z + 2 * e));
                    Append(steel, Box(mx - mast / 2, my + mast / 2 - e, z, mx + mast / 2, my + mast / 2 + e, z + 2 * e));
                    Append(steel, Box(mx - mast / 2 - e, my - mast / 2, z, mx - mast / 2 + e, my + mast / 2, z + 2 * e));
                    Append(steel, Box(mx + mast / 2 - e, my - mast / 2, z, mx + mast / 2 + e, my + mast / 2, z + 2 * e));
                }
                double a = state.SlewDeg * Math.PI / 180, ca = Math.Cos(a), sa = Math.Sin(a);
                double jib = size * 0.9 + 6 * M, counter = jib * 0.32, jz = top + 0.2 * M;
                Append(steel, Beam(mx, my, jz, ca, sa, -counter, jib, 1.1 * M, 1.3 * M));
                Append(steel, Box(mx - 0.4, my - 0.4, top, mx + 0.4, my + 0.4, top + 5 * M)); // pointe (porte-fleche)
                parts.Add((steel, "#F2B705"));
                parts.Add((Beam(mx, my, jz - 1.6 * M, ca, sa, -counter, -counter + 3.5 * M, 2.2 * M, 1.6 * M), "#7D848C")); // lest
                parts.Add((Box(mx - 1.0 * M, my - 1.0 * M, top - 2.4 * M, mx + 1.0 * M, my + 1.0 * M, top), "#E9EEF2")); // cabine
                // Cable et crochet au-dessus de la zone en construction.
                double hookAt = jib * 0.62;
                double hx = mx + ca * hookAt, hy = my + sa * hookAt, hookZ = z0 + h * Math.Max(0.15, state.Structure) + 3 * M;
                parts.Add((Box(hx - 0.05, hy - 0.05, hookZ, hx + 0.05, hy + 0.05, jz), "#30343A"));
                parts.Add((Box(hx - 0.5, hy - 0.5, hookZ - 0.8, hx + 0.5, hy + 0.5, hookZ), "#30343A"));
            }

            // 5. Echafaudages autour du batiment, jusqu'au niveau atteint par le gros oeuvre (+1 m).
            if (state.Scaffold)
            {
                double off = 0.7 * M, sx0 = min.X - off, sy0 = min.Y - off, sx1 = max.X + off, sy1 = max.Y + off;
                double topS = z0 + Math.Max(2 * M, h * Math.Min(1, state.Structure) + 1 * M), t = 0.09, step = 2.5 * M, lift = 2 * M;
                var tubes = new TriMesh();
                var decks = new TriMesh();
                void Side(double ax, double ay, double bx2, double by2)
                {
                    double len = Math.Sqrt((bx2 - ax) * (bx2 - ax) + (by2 - ay) * (by2 - ay));
                    int n = Math.Max(1, (int)Math.Round(len / step));
                    for (int i = 0; i <= n; i++)
                    {
                        double x = ax + (bx2 - ax) * i / n, y = ay + (by2 - ay) * i / n;
                        Append(tubes, Box(x - t, y - t, z0, x + t, y + t, topS));
                    }
                    for (double z = z0 + lift; z <= topS + 1e-6; z += lift)
                    {
                        Append(tubes, Box(Math.Min(ax, bx2) - t, Math.Min(ay, by2) - t, z - t, Math.Max(ax, bx2) + t, Math.Max(ay, by2) + t, z + t));
                        // Plancher de travail (planches) cote batiment.
                        double nx = ay == by2 ? 0 : (ax < (min.X + max.X) / 2 ? 1 : -1), ny = ay == by2 ? (ay < (min.Y + max.Y) / 2 ? 1 : -1) : 0;
                        double w = 0.55 * M;
                        Append(decks, Box(Math.Min(ax, bx2 + nx * w), Math.Min(ay, by2 + ny * w), z + t, Math.Max(ax, bx2 + nx * w), Math.Max(ay, by2 + ny * w), z + t + 0.12));
                    }
                }
                Side(sx0, sy0, sx1, sy0);
                Side(sx0, sy1, sx1, sy1);
                Side(sx0, sy0, sx0, sy1);
                Side(sx1, sy0, sx1, sy1);
                parts.Add((tubes, "#A9B1BA"));
                parts.Add((decks, "#C49A5A"));
            }
            return parts;
        }

        /// <summary>Boite alignee sur les axes (12 triangles).</summary>
        public static TriMesh Box(double x0, double y0, double z0, double x1, double y1, double z1)
        {
            var c = new[]
            {
                new V3(x0, y0, z0), new V3(x1, y0, z0), new V3(x1, y1, z0), new V3(x0, y1, z0),
                new V3(x0, y0, z1), new V3(x1, y0, z1), new V3(x1, y1, z1), new V3(x0, y1, z1)
            };
            return FromCorners(c);
        }

        /// <summary>Poutre horizontale orientee (fleche de grue) : de « from » a « to » le long de (ca, sa), section w x hgt.</summary>
        private static TriMesh Beam(double ox, double oy, double z, double ca, double sa, double from, double to, double w, double hgt)
        {
            double px = -sa * w / 2, py = ca * w / 2;
            V3 P(double s, double side, double up) => new V3(ox + ca * s + px * side, oy + sa * s + py * side, z + up);
            var c = new[] { P(from, -1, 0), P(to, -1, 0), P(to, 1, 0), P(from, 1, 0), P(from, -1, hgt), P(to, -1, hgt), P(to, 1, hgt), P(from, 1, hgt) };
            return FromCorners(c);
        }

        private static TriMesh FromCorners(V3[] c)
        {
            int[][] f = { new[] { 0, 3, 2, 1 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 }, new[] { 1, 2, 6, 5 }, new[] { 2, 3, 7, 6 }, new[] { 3, 0, 4, 7 } };
            var m = new TriMesh();
            foreach (var q in f) { m.Add(c[q[0]], c[q[1]], c[q[2]]); m.Add(c[q[0]], c[q[2]], c[q[3]]); }
            return m;
        }

        private static void Append(TriMesh into, TriMesh m) => into.Points.AddRange(m.Points);
    }
}
