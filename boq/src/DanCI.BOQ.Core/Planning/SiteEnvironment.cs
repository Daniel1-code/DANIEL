using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Extraction;

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
        /// <summary>Animation des engins (radians, augmente a chaque image).</summary>
        public double Phase { get; set; }

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

    /// <summary>Scene de terrassement : fouilles (dans l'ordre de creusement), avancement, niveau du terrain naturel.</summary>
    public sealed class DigScene
    {
        /// <summary>Niveau du terrain naturel (unites du modele).</summary>
        public double GroundZ { get; set; }
        public List<Prism> Pits { get; set; } = new List<Prism>();
        /// <summary>Nombre de fouilles deja creusees (ouvertes).</summary>
        public int Dug { get; set; }
        /// <summary>Avancement du terrassement (0..1) ; utile sans fouilles calculees.</summary>
        public double Progress { get; set; }
        /// <summary>Fouilles ouvertes (avant le remblai, fin des fondations).</summary>
        public bool Open { get; set; }
        /// <summary>Terrassement en cours : pelle et camion presents.</summary>
        public bool Excavating { get; set; }
    }

    /// <summary>
    /// Environnement de chantier autour du batiment (dessin seulement, jamais dans le modele) : terrain,
    /// plateforme, palissade et portail, grue a tour qui pivote, echafaudages qui montent avec le gros
    /// oeuvre, bungalows et stock de materiaux. Unites du modele (pieds) ; emprise = boite du batiment.
    /// </summary>
    public static class SiteEnvironment
    {
        private const double M = 3.28084; // pieds par metre

        public static List<(TriMesh Mesh, string Color)> Build(V3 min, V3 max, SiteState state) => Build(min, max, state, null);

        /// <summary>
        /// Environnement complet. <paramref name="dig"/> : scene de terrassement (fouilles creusees, pelle,
        /// camion, deblais) et niveau du terrain naturel ; null = terrain au bas du batiment, sans fouilles.
        /// </summary>
        public static List<(TriMesh Mesh, string Color)> Build(V3 min, V3 max, SiteState state, DigScene dig)
        {
            var parts = new List<(TriMesh, string)>();
            state = state ?? new SiteState();
            double z0 = dig?.GroundZ ?? min.Z;
            double dx = max.X - min.X, dy = max.Y - min.Y, h = Math.Max(3 * M, max.Z - z0);
            double cx = (min.X + max.X) / 2, cy = (min.Y + max.Y) / 2;
            double size = Math.Max(dx, dy);

            // 1. Terrain (gazon) autour de la plateforme de chantier (terre), au niveau du terrain naturel.
            //    Les fouilles deja creusees sont de VRAIS trous : la plateforme est decoupee autour d'elles.
            double g = size * 0.9 + 18 * M; // terrain modere : le cadrage reste sur le batiment
            double pad = 7 * M;
            double px0 = min.X - pad, py0 = min.Y - pad, px1 = max.X + pad, py1 = max.Y + pad;
            double gx0 = cx - g, gy0 = cy - g, gx1 = cx + g, gy1 = cy + g;
            var grass = new TriMesh();
            Quad(grass, gx0, gy0, gx1, py0, z0 - 0.06);
            Quad(grass, gx0, py1, gx1, gy1, z0 - 0.06);
            Quad(grass, gx0, py0, px0, py1, z0 - 0.06);
            Quad(grass, px1, py0, gx1, py1, z0 - 0.06);
            parts.Add((grass, "#6F8F4E"));
            var openPits = dig?.Pits == null ? new List<Prism>() : dig.Pits.Take(Math.Max(0, Math.Min(dig.Pits.Count, dig.Dug))).ToList();
            parts.Add((PlaneWithHoles(px0, py0, px1, py1, z0 - 0.04, openPits.Select(TopRect).ToList()), "#9C8463"));
            if (openPits.Count > 0)
            {
                var walls = new TriMesh();
                var floors = new TriMesh();
                foreach (Prism p in openPits) Cavity(p, walls, floors);
                parts.Add((walls, "#7A5A3C"));
                parts.Add((floors, "#5E4430"));
            }
            if (dig != null) AddEarthworks(parts, dig, min, max, z0, px0, py0, px1, py1, state.Phase);

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

        /// <summary>Rectangle horizontal (2 triangles, dessine sur ses deux faces).</summary>
        private static void Quad(TriMesh m, double x0, double y0, double x1, double y1, double z)
        {
            if (x1 - x0 < 1e-6 || y1 - y0 < 1e-6) return;
            V3 a = new V3(x0, y0, z), b = new V3(x1, y0, z), c = new V3(x1, y1, z), d = new V3(x0, y1, z);
            m.Add(a, b, c);
            m.Add(a, c, d);
        }

        /// <summary>Emprise en plan (rectangle englobant de la tete de fouille, talus compris).</summary>
        private static (double X0, double Y0, double X1, double Y1) TopRect(Prism p)
        {
            double[] q = p.Top != null && p.Top.Length >= 8 ? p.Top : p.Bottom;
            double x0 = double.MaxValue, y0 = double.MaxValue, x1 = double.MinValue, y1 = double.MinValue;
            for (int i = 0; i < 4; i++)
            {
                x0 = Math.Min(x0, q[2 * i]); x1 = Math.Max(x1, q[2 * i]);
                y0 = Math.Min(y0, q[2 * i + 1]); y1 = Math.Max(y1, q[2 * i + 1]);
            }
            return (x0, y0, x1, y1);
        }

        /// <summary>
        /// Plan horizontal troue : rectangle moins des rectangles (ouvertures des fouilles), par bandes
        /// (compression des coordonnees) ; les morceaux d'une bande sont fusionnes.
        /// </summary>
        public static TriMesh PlaneWithHoles(double x0, double y0, double x1, double y1, double z, List<(double X0, double Y0, double X1, double Y1)> holes)
        {
            var m = new TriMesh();
            var hs = holes.Where(r => r.X1 > x0 && r.X0 < x1 && r.Y1 > y0 && r.Y0 < y1)
                          .Select(r => (X0: Math.Max(x0, r.X0), Y0: Math.Max(y0, r.Y0), X1: Math.Min(x1, r.X1), Y1: Math.Min(y1, r.Y1))).ToList();
            if (hs.Count == 0) { Quad(m, x0, y0, x1, y1, z); return m; }
            var ys = hs.SelectMany(r => new[] { r.Y0, r.Y1 }).Concat(new[] { y0, y1 }).Distinct().OrderBy(v => v).ToList();
            for (int j = 0; j + 1 < ys.Count; j++)
            {
                double ya = ys[j], yb = ys[j + 1], ym = (ya + yb) / 2;
                if (yb - ya < 1e-6) continue;
                var cut = hs.Where(r => r.Y0 <= ym && r.Y1 >= ym).Select(r => (r.X0, r.X1)).OrderBy(r => r.X0).ToList();
                double x = x0;
                foreach (var (a, b) in cut)
                {
                    if (a > x) Quad(m, x, ya, a, yb, z);
                    x = Math.Max(x, b);
                }
                if (x < x1) Quad(m, x, ya, x1, yb, z);
            }
            return m;
        }

        /// <summary>Fouille ouverte : fond et parois (talus) en terre, sans dessus (on voit dedans).</summary>
        private static void Cavity(Prism p, TriMesh walls, TriMesh floors)
        {
            double[] b = p.Bottom, t = p.Top != null && p.Top.Length >= 8 ? p.Top : p.Bottom;
            V3 B(int i) => new V3(b[2 * i], b[2 * i + 1], p.Zb);
            V3 T(int i) => new V3(t[2 * i], t[2 * i + 1], p.Zt - 0.04);
            floors.Add(B(0), B(1), B(2));
            floors.Add(B(0), B(2), B(3));
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                walls.Add(B(i), B(j), T(j));
                walls.Add(B(i), T(j), T(i));
            }
        }

        /// <summary>
        /// Scene de terrassement : tas de deblais qui grossit, pelle hydraulique animee au bord de la fouille
        /// en cours (tourelle qui pivote, fleche, bras, godet) et camion-benne qui recoit la terre.
        /// </summary>
        private static void AddEarthworks(List<(TriMesh, string)> parts, DigScene dig, V3 min, V3 max, double z0,
            double px0, double py0, double px1, double py1, double phase)
        {
            int total = Math.Max(1, dig.Pits?.Count ?? 0);
            double share = dig.Pits == null || dig.Pits.Count == 0 ? dig.Progress : Math.Min(1.0, dig.Dug / (double)total);
            // Deblais : tant que les fouilles sont ouvertes (avant le remblai).
            if (dig.Open && share > 0.01)
            {
                // Tas compact dans le coin de la plateforme (hors de l'emprise du batiment : bande de 7 m).
                double r = (1.4 + 1.8 * Math.Sqrt(share)) * M, hh = (1.0 + 2.2 * Math.Sqrt(share)) * M;
                double hx = px0 + 3.6 * M, hy = py1 - 3.6 * M;
                parts.Add((Mound(hx, hy, z0 - 0.04, r, hh), "#8B6B47"));
            }
            if (!dig.Excavating) return;
            // Fouille en cours (la derniere ouverte), a defaut le coin du batiment.
            double tx, ty, depth;
            if (dig.Pits != null && dig.Pits.Count > 0)
            {
                Prism cur = dig.Pits[Math.Max(0, Math.Min(dig.Pits.Count - 1, dig.Dug - 1))];
                var rr = TopRect(cur);
                tx = (rr.X0 + rr.X1) / 2; ty = (rr.Y0 + rr.Y1) / 2; depth = cur.Zb;
            }
            else
            {
                tx = min.X; ty = min.Y; depth = z0 - 1.5 * M;
            }
            // La pelle se place du cote exterieur de la fouille (vers le bord de la plateforme le plus proche).
            double bcx = (min.X + max.X) / 2, bcy = (min.Y + max.Y) / 2;
            double ox = tx - bcx, oy = ty - bcy, ol = Math.Sqrt(ox * ox + oy * oy);
            if (ol < 1e-6) { ox = -1; oy = 0; ol = 1; }
            ox /= ol; oy /= ol;
            double ex = tx + ox * 5.5 * M, ey = ty + oy * 5.5 * M;
            double toPit = Math.Atan2(ty - ey, tx - ex);
            // Cycle : creuse (vers la fouille) puis pivote vers le camion pour deverser.
            double cycle = (Math.Sin(phase) + 1) / 2;                       // 0 = fouille, 1 = camion
            double swing = toPit + cycle * Math.PI * 0.55;
            double dip = Math.Max(0, Math.Cos(phase));                          // 1 = godet au fond
            Excavator(parts, ex, ey, z0, toPit + Math.PI / 2, swing, dip, depth);
            // Camion-benne a cote, cote deversement.
            double ta = toPit + Math.PI * 0.55;
            double kx = ex + Math.Cos(ta) * 6.0 * M, ky = ey + Math.Sin(ta) * 6.0 * M;
            DumpTruck(parts, kx, ky, z0, ta + Math.PI / 2, share);
        }

        /// <summary>Tas conique (tronc de pyramide a 8 cotes).</summary>
        private static TriMesh Mound(double cx, double cy, double z, double r, double h)
        {
            var m = new TriMesh();
            int n = 10;
            V3 top = new V3(cx, cy, z + h);
            for (int i = 0; i < n; i++)
            {
                double a = 2 * Math.PI * i / n, b = 2 * Math.PI * (i + 1) / n;
                double ra = r * (0.85 + 0.15 * Math.Sin(3 * a)), rb = r * (0.85 + 0.15 * Math.Sin(3 * b));
                V3 p = new V3(cx + ra * Math.Cos(a), cy + ra * Math.Sin(a), z), q = new V3(cx + rb * Math.Cos(b), cy + rb * Math.Sin(b), z);
                m.Add(p, q, top);
            }
            return m;
        }

        /// <summary>Boite orientee : centre, demi-dimensions le long de (u, v, w).</summary>
        private static TriMesh OBox(V3 c, V3 u, V3 v, V3 w, double hu, double hv, double hw)
        {
            V3 P(int su, int sv, int sw) => c + u * (su * hu) + v * (sv * hv) + w * (sw * hw);
            var k = new[] { P(-1, -1, -1), P(1, -1, -1), P(1, 1, -1), P(-1, 1, -1), P(-1, -1, 1), P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1) };
            return FromCorners(k);
        }

        /// <summary>Barre de section s x s entre deux points (fleche, bras).</summary>
        private static TriMesh Bar(V3 a, V3 b, double s)
        {
            V3 d = (b - a);
            double len = d.Length;
            if (len < 1e-6) return new TriMesh();
            V3 u = d * (1 / len);
            V3 side = Math.Abs(u.Z) < 0.95 ? u.Cross(new V3(0, 0, 1)).Unit() : u.Cross(new V3(1, 0, 0)).Unit();
            V3 up = side.Cross(u).Unit();
            return OBox((a + b) * 0.5, u, side, up, len / 2, s / 2, s / 2);
        }

        /// <summary>Pelle hydraulique : chenilles (cap), tourelle et cabine (pivot), fleche, bras, godet.</summary>
        private static void Excavator(List<(TriMesh, string)> parts, double x, double y, double z, double heading, double swing, double dip, double digZ)
        {
            V3 Dir(double a) => new V3(Math.Cos(a), Math.Sin(a), 0);
            V3 up = new V3(0, 0, 1);
            V3 f = Dir(heading), s = Dir(heading + Math.PI / 2);
            var tracks = new TriMesh();
            foreach (int k in new[] { -1, 1 })
                Append(tracks, OBox(new V3(x, y, z + 0.45 * M) + s * (k * 1.15 * M), f, s, up, 2.1 * M, 0.32 * M, 0.45 * M));
            parts.Add((tracks, "#2B2B2B"));
            V3 sf = Dir(swing), ss = Dir(swing + Math.PI / 2);
            V3 body = new V3(x, y, z + 1.55 * M);
            parts.Add((OBox(body - sf * (0.3 * M), sf, ss, up, 1.6 * M, 1.25 * M, 0.6 * M), "#F4B400"));
            parts.Add((OBox(body - sf * (1.75 * M) + up * (0.15 * M), sf, ss, up, 0.25 * M, 1.2 * M, 0.55 * M), "#3A3A3A")); // contrepoids
            parts.Add((OBox(body + sf * (0.6 * M) + ss * (0.65 * M) + up * (1.15 * M), sf, ss, up, 0.55 * M, 0.5 * M, 0.6 * M), "#8FBCE0")); // cabine vitree
            // Fleche et bras : le godet descend dans la fouille puis remonte.
            V3 root = body + sf * (1.0 * M) + up * (0.5 * M);
            V3 knee = root + sf * (3.4 * M) + up * ((1.8 + 1.2 * (1 - dip)) * M);
            double bottom = Math.Min(z - 0.3 * M, digZ + 0.4 * M);
            V3 tip = new V3(knee.X, knee.Y, 0) + sf * (1.6 * M);
            tip = new V3(tip.X, tip.Y, z + (bottom - z) * dip + (1 - dip) * 1.2 * M);
            var arm = new TriMesh();
            Append(arm, Bar(root, knee, 0.45 * M));
            Append(arm, Bar(knee, tip, 0.35 * M));
            parts.Add((arm, "#F4B400"));
            parts.Add((OBox(tip + sf * (0.2 * M) - up * (0.25 * M), sf, ss, up, 0.45 * M, 0.55 * M, 0.35 * M), "#4A4A4A")); // godet
        }

        /// <summary>Camion-benne : chassis, cabine, benne remplie de terre, roues.</summary>
        private static void DumpTruck(List<(TriMesh, string)> parts, double x, double y, double z, double heading, double load)
        {
            V3 Dir(double a) => new V3(Math.Cos(a), Math.Sin(a), 0);
            V3 up = new V3(0, 0, 1), f = Dir(heading), s = Dir(heading + Math.PI / 2);
            V3 c = new V3(x, y, z);
            var wheels = new TriMesh();
            foreach (double a in new[] { -2.2, -1.0, 2.0 })
                foreach (int k in new[] { -1, 1 })
                    Append(wheels, OBox(c + f * (a * M) + s * (k * 1.05 * M) + up * (0.5 * M), f, s, up, 0.5 * M, 0.25 * M, 0.5 * M));
            parts.Add((wheels, "#222222"));
            parts.Add((OBox(c + up * (1.1 * M), f, s, up, 3.2 * M, 1.15 * M, 0.2 * M), "#3C3F44")); // chassis
            parts.Add((OBox(c + f * (2.3 * M) + up * (2.0 * M), f, s, up, 0.8 * M, 1.2 * M, 0.8 * M), "#E8E8E8")); // cabine
            parts.Add((OBox(c - f * (0.8 * M) + up * (2.0 * M), f, s, up, 2.2 * M, 1.2 * M, 0.7 * M), "#C8462E")); // benne
            double fill = Math.Max(0.15, Math.Min(1, load * 3 % 1 + 0.2));
            parts.Add((OBox(c - f * (0.8 * M) + up * ((2.7 + 0.2 * fill) * M), f, s, up, 2.0 * M, 1.05 * M, 0.2 * fill * M), "#8B6B47")); // terre
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
