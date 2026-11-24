using System;
using System.Collections.Generic;
using System.Linq;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Point 3D (unites du modele).</summary>
    public struct V3
    {
        public double X, Y, Z;
        public V3(double x, double y, double z) { X = x; Y = y; Z = z; }
        public static V3 operator +(V3 a, V3 b) => new V3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
        public static V3 operator -(V3 a, V3 b) => new V3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
        public static V3 operator *(V3 a, double k) => new V3(a.X * k, a.Y * k, a.Z * k);
        public double Dot(V3 b) => X * b.X + Y * b.Y + Z * b.Z;
        public V3 Cross(V3 b) => new V3(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
        public double Length => Math.Sqrt(X * X + Y * Y + Z * Z);
        public V3 Unit() { double l = Length; return l < 1e-12 ? new V3(0, 0, 1) : this * (1 / l); }
    }

    /// <summary>Maillage triangule « en vrac » : chaque groupe de 3 points est un triangle.</summary>
    public sealed class TriMesh
    {
        public List<V3> Points { get; } = new List<V3>();
        public int TriangleCount => Points.Count / 3;
        public bool IsEmpty => Points.Count < 3;

        public void Add(V3 a, V3 b, V3 c) { Points.Add(a); Points.Add(b); Points.Add(c); boundsCount = -1; }

        private int boundsCount = -1;
        private double minZ, maxZ;

        private void Bounds()
        {
            if (boundsCount == Points.Count) return;
            minZ = double.MaxValue; maxZ = double.MinValue;
            foreach (V3 p in Points) { if (p.Z < minZ) minZ = p.Z; if (p.Z > maxZ) maxZ = p.Z; }
            if (Points.Count == 0) minZ = maxZ = 0;
            boundsCount = Points.Count;
        }

        public double MinZ { get { Bounds(); return minZ; } }
        public double MaxZ { get { Bounds(); return maxZ; } }

        /// <summary>Etendue le long d'un axe (memorisee pour le dernier axe demande).</summary>
        public (double Min, double Max) Extent(V3 axis)
        {
            if (extentCount == Points.Count && extentAxis.X == axis.X && extentAxis.Y == axis.Y && extentAxis.Z == axis.Z) return extent;
            double a = double.MaxValue, b = double.MinValue;
            foreach (V3 p in Points) { double d = p.Dot(axis); if (d < a) a = d; if (d > b) b = d; }
            extent = Points.Count == 0 ? (0, 0) : (a, b);
            extentAxis = axis;
            extentCount = Points.Count;
            return extent;
        }

        private int extentCount = -1;
        private V3 extentAxis;
        private (double, double) extent;
    }

    /// <summary>
    /// Decoupe d'un maillage d'element par un plan, AVEC fermeture de la coupe (couvercle) : la
    /// partie construite d'un mur, d'une dalle, d'une poutre garde sa vraie forme (ouvertures, pentes,
    /// biais) et reste un volume plein, sans trou ni « boite » approximative. Calcul pur, rapide
    /// (lineaire en nombre de triangles), sans operation booleenne de Revit.
    /// </summary>
    public static class MeshClip
    {
        private const double Eps = 1e-9;

        /// <summary>Garde la partie ou n·p &lt;= d (n unitaire), et ferme la coupe.</summary>
        public static TriMesh Keep(TriMesh mesh, V3 n, double d, bool cap = true)
        {
            var result = new TriMesh();
            if (mesh == null || mesh.IsEmpty) return result;
            n = n.Unit();
            var segments = new List<(V3 A, V3 B)>();
            var poly = new List<V3>(5);
            for (int t = 0; t + 2 < mesh.Points.Count; t += 3)
            {
                V3 a = mesh.Points[t], b = mesh.Points[t + 1], c = mesh.Points[t + 2];
                double sa = n.Dot(a) - d, sb = n.Dot(b) - d, sc = n.Dot(c) - d;
                if (sa <= Eps && sb <= Eps && sc <= Eps) { result.Add(a, b, c); continue; }
                if (sa > Eps && sb > Eps && sc > Eps) continue;
                // Sutherland-Hodgman sur le triangle.
                poly.Clear();
                var cut = new List<V3>(2);
                Clip(a, sa, b, sb, poly, cut);
                Clip(b, sb, c, sc, poly, cut);
                Clip(c, sc, a, sa, poly, cut);
                for (int k = 1; k + 1 < poly.Count; k++) result.Add(poly[0], poly[k], poly[k + 1]);
                if (cut.Count == 2 && (cut[0] - cut[1]).Length > 1e-7) segments.Add((cut[0], cut[1]));
            }
            if (cap && segments.Count >= 3) Cap(segments, n, result);
            return result;
        }

        private static void Clip(V3 p, double sp, V3 q, double sq, List<V3> poly, List<V3> cut)
        {
            bool pin = sp <= Eps, qin = sq <= Eps;
            if (pin) poly.Add(p);
            if (pin != qin)
            {
                double k = sp / (sp - sq);
                V3 x = p + (q - p) * k;
                poly.Add(x);
                cut.Add(x);
            }
        }

        /// <summary>Couvercle : les segments de coupe chaines en boucles, triangulees (oreilles) dans le plan.</summary>
        private static void Cap(List<(V3 A, V3 B)> segments, V3 n, TriMesh result)
        {
            // Base du plan.
            V3 u = Math.Abs(n.Z) < 0.9 ? new V3(0, 0, 1).Cross(n).Unit() : new V3(1, 0, 0).Cross(n).Unit();
            V3 v = n.Cross(u).Unit();
            (long, long, long) Key(V3 p) => ((long)Math.Round(p.X * 1e5), (long)Math.Round(p.Y * 1e5), (long)Math.Round(p.Z * 1e5));
            // Graphe non oriente des extremites.
            var adj = new Dictionary<(long, long, long), List<((long, long, long) Key, V3 P)>>();
            var pts = new Dictionary<(long, long, long), V3>();
            foreach (var s in segments)
            {
                var ka = Key(s.A); var kb = Key(s.B);
                if (ka.Equals(kb)) continue;
                pts[ka] = s.A; pts[kb] = s.B;
                if (!adj.TryGetValue(ka, out var la)) adj[ka] = la = new List<((long, long, long), V3)>();
                if (!adj.TryGetValue(kb, out var lb)) adj[kb] = lb = new List<((long, long, long), V3)>();
                la.Add((kb, s.B));
                lb.Add((ka, s.A));
            }
            var used = new HashSet<((long, long, long), (long, long, long))>();
            ((long, long, long), (long, long, long)) EdgeKey((long, long, long) a, (long, long, long) b) =>
                Compare(a, b) < 0 ? (a, b) : (b, a);
            foreach (var start in adj.Keys.ToList())
            {
                foreach (var first in adj[start])
                {
                    if (used.Contains(EdgeKey(start, first.Key))) continue;
                    var loop = new List<V3> { pts[start] };
                    var cur = first.Key;
                    used.Add(EdgeKey(start, cur));
                    int guard = 0;
                    bool open = false;
                    while (!cur.Equals(start) && guard++ < 100000)
                    {
                        loop.Add(pts[cur]);
                        bool found = false;
                        foreach (var e in adj[cur])
                        {
                            if (used.Contains(EdgeKey(cur, e.Key))) continue;
                            used.Add(EdgeKey(cur, e.Key));
                            cur = e.Key;
                            found = true;
                            break;
                        }
                        if (!found) { open = true; break; }
                    }
                    if (open || !cur.Equals(start) || loop.Count < 3) continue; // boucle ouverte : pas de couvercle (jamais de faux triangle)
                    Triangulate(loop, u, v, n, result);
                }
            }
        }

        private static int Compare((long, long, long) a, (long, long, long) b) =>
            a.Item1 != b.Item1 ? a.Item1.CompareTo(b.Item1) : a.Item2 != b.Item2 ? a.Item2.CompareTo(b.Item2) : a.Item3.CompareTo(b.Item3);

        /// <summary>Au-dela, la coupe n'est pas refermee (section tres decoupee) : calcul borne, jamais de blocage.</summary>
        public const int MaxCapPoints = 3000;

        private static void Triangulate(List<V3> loop, V3 u, V3 v, V3 n, TriMesh result)
        {
            if (loop.Count > MaxCapPoints) return;
            // Points alignes (milieux de facettes, jonctions) retires : boucle plus courte, triangulation sure.
            var p2 = loop.Select(p => (X: p.Dot(u), Y: p.Dot(v))).ToList();
            for (int pass = 0; pass < 2 && loop.Count > 3; pass++)
            {
                var keepL = new List<V3>(loop.Count);
                var keepP = new List<(double X, double Y)>(loop.Count);
                for (int i = 0; i < loop.Count; i++)
                {
                    var a = p2[(i + loop.Count - 1) % loop.Count]; var b = p2[i]; var c = p2[(i + 1) % loop.Count];
                    double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    double scale = Math.Abs(c.X - a.X) + Math.Abs(c.Y - a.Y);
                    if (Math.Abs(cross) <= 1e-9 * Math.Max(1e-6, scale * scale) && loop.Count - (i - keepL.Count) > 3) continue;
                    keepL.Add(loop[i]);
                    keepP.Add(b);
                }
                if (keepL.Count < 3 || keepL.Count == loop.Count) break;
                loop = keepL;
                p2 = keepP;
            }
            double area = 0;
            for (int i = 0; i < p2.Count; i++)
            {
                var a = p2[i]; var b = p2[(i + 1) % p2.Count];
                area += a.X * b.Y - b.X * a.Y;
            }
            if (Math.Abs(area) < 1e-10) return;
            var idx = Enumerable.Range(0, loop.Count).ToList();
            if (area < 0) idx.Reverse(); // sens trigonometrique dans (u, v) : normale du couvercle = n
            int guard = 0, from = 0;
            while (idx.Count > 3 && guard++ < 10000)
            {
                bool clipped = false;
                for (int step = 0; step < idx.Count; step++)
                {
                    int i = (from + step) % idx.Count;
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    var a = p2[i0]; var b = p2[i1]; var c = p2[i2];
                    double cross = (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
                    if (cross <= 1e-12) continue; // sommet rentrant
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTriangle(p2[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(loop[i0], loop[i1], loop[i2]);
                    idx.RemoveAt(i);
                    from = idx.Count == 0 ? 0 : (i + idx.Count - 1) % idx.Count; // reprend a cote : O(n²) au lieu de O(n³)
                    clipped = true;
                    break;
                }
                if (!clipped) break; // boucle degeneree : on s'arrete proprement
            }
            if (idx.Count == 3) result.Add(loop[idx[0]], loop[idx[1]], loop[idx[2]]);
        }

        private static bool InTriangle((double X, double Y) p, (double X, double Y) a, (double X, double Y) b, (double X, double Y) c)
        {
            double d1 = (p.X - b.X) * (a.Y - b.Y) - (a.X - b.X) * (p.Y - b.Y);
            double d2 = (p.X - c.X) * (b.Y - c.Y) - (b.X - c.X) * (p.Y - c.Y);
            double d3 = (p.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (p.Y - a.Y);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }
    }

    /// <summary>Partie construite d'un element a un avancement donne, en VRAIE geometrie.</summary>
    public sealed class MeshPart
    {
        public TriMesh Mesh { get; set; }
        /// <summary>Rang en cours de pose (teinte plus claire : le palier se voit).</summary>
        public bool Fresh { get; set; }
    }

    public static class Build4DMesh
    {
        /// <summary>
        /// Partie construite d'un element (vraie geometrie) : un element haut monte par rangs de 20 cm
        /// (le rang en cours pose le long de l'element, teinte claire) ; un element plat se coule d'un bout
        /// a l'autre. Meme rythme que <see cref="Build4D.Partial"/>.
        /// </summary>
        public static List<MeshPart> Partial(Build4DItem item, TriMesh source, double progress)
        {
            var parts = new List<MeshPart>();
            double p = Math.Max(0, Math.Min(1, progress));
            if (source == null || source.IsEmpty || p <= 0) return parts;
            if (p >= 1) { parts.Add(new MeshPart { Mesh = source }); return parts; }
            double zb = source.MinZ, zt = source.MaxZ, h = zt - zb;
            V3 axis = Axis(item, source);
            var (smin, smax) = source.Extent(axis);
            if (h < Build4D.FlatFeet)
            {
                // Element plat : coule en longueur.
                parts.Add(new MeshPart { Mesh = MeshClip.Keep(source, axis, smin + p * (smax - smin)) });
                return parts;
            }
            var up = new V3(0, 0, 1);
            var down = new V3(0, 0, -1);
            int n = Math.Max(1, (int)Math.Round(h / Build4D.CourseFeet));
            double hc = h / n, total = p * n;
            int full = Math.Min(n, (int)Math.Floor(total + 1e-9));
            double frac = total - full;
            double zc = zb + full * hc;
            if (full > 0) parts.Add(new MeshPart { Mesh = MeshClip.Keep(source, up, zc) });
            if (full < n && frac > 1e-6)
            {
                TriMesh band = MeshClip.Keep(MeshClip.Keep(source, down, -zc), up, zc + hc);
                band = MeshClip.Keep(band, axis, smin + frac * (smax - smin));
                parts.Add(new MeshPart { Mesh = band, Fresh = true });
            }
            parts.RemoveAll(x => x.Mesh == null || x.Mesh.IsEmpty);
            return parts;
        }

        /// <summary>
        /// Avancement arrondi au huitieme de rang (ou au 1/64 de longueur pour un element plat) et sa cle :
        /// tant que la cle ne change pas, la decoupe precedente est reutilisee (aucun calcul).
        /// </summary>
        public static int StepKey(TriMesh source, double progress, out double rounded)
        {
            double p = Math.Max(0, Math.Min(1, progress));
            double h = source == null || source.IsEmpty ? 0 : source.MaxZ - source.MinZ;
            int steps = h < Build4D.FlatFeet ? 64 : Math.Max(1, (int)Math.Round(h / Build4D.CourseFeet)) * 8;
            int key = (int)Math.Floor(p * steps + 1e-9);
            rounded = p >= 1 ? 1 : key / (double)steps;
            return p >= 1 ? int.MaxValue : key;
        }

        /// <summary>Direction de pose : le grand cote de la forme (axe du mur), a defaut la plus grande emprise en plan.</summary>
        public static V3 Axis(Build4DItem item, TriMesh source)
        {
            var prism = item?.Shape?.FirstOrDefault();
            if (prism != null && prism.Bottom != null && prism.Bottom.Length >= 8)
            {
                V3 best = new V3(1, 0, 0);
                double len = -1;
                for (int i = 0; i < 4; i++)
                {
                    int j = (i + 1) % 4;
                    var d = new V3(prism.Bottom[2 * j] - prism.Bottom[2 * i], prism.Bottom[2 * j + 1] - prism.Bottom[2 * i + 1], 0);
                    if (d.Length > len) { len = d.Length; best = d; }
                }
                if (len > 1e-6) return best.Unit();
            }
            double dx = source.Points.Max(q => q.X) - source.Points.Min(q => q.X), dy = source.Points.Max(q => q.Y) - source.Points.Min(q => q.Y);
            return dx >= dy ? new V3(1, 0, 0) : new V3(0, 1, 0);
        }
    }
}
