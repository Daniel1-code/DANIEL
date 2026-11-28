using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Volume a dessiner en 3D : quadrilatere du bas (4 coins XY, sens trigonometrique) a la cote
    /// Zb, quadrilatere du haut (4 coins XY) a la cote Zt. Coordonnees du modele HOTE, pieds Revit.
    /// Un tronc de pyramide (fouille talutee) a un quadrilatere du haut plus grand.
    /// </summary>
    public sealed class Prism
    {
        public double[] Bottom { get; set; } = new double[8];
        public double Zb { get; set; }
        public double[] Top { get; set; } = new double[8];
        public double Zt { get; set; }
    }

    /// <summary>
    /// Trace 3D des fouilles (calculees, non modelisees), porte par la quantite dans le parametre
    /// « Geo3D » : prismes separes par « ; », 18 nombres chacun. Calcul pur, testable.
    /// </summary>
    public static class Excavation3D
    {
        public const string Parameter = "Geo3D";

        /// <summary>Rigole le long d'une polyligne (trace du mur), raccourcie de « cut » a chaque extremite jointe.</summary>
        public static List<Prism> AlongPolyline(IReadOnlyList<(double X, double Y)> points, double width, double zb, double zt,
            double cutStart, double cutEnd)
        {
            var result = new List<Prism>();
            if (points == null || points.Count < 2 || width <= 0 || zt <= zb) return result;
            var pts = Trim(points.ToList(), Math.Max(0, cutStart), Math.Max(0, cutEnd));
            double h = width / 2;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                (double ax, double ay) = pts[i];
                (double bx, double by) = pts[i + 1];
                double dx = bx - ax, dy = by - ay, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-9) continue;
                double nx = -dy / len * h, ny = dx / len * h;
                var quad = new[] { ax - nx, ay - ny, bx - nx, by - ny, bx + nx, by + ny, ax + nx, ay + ny };
                result.Add(new Prism { Bottom = Ccw(quad), Zb = zb, Top = Ccw(quad), Zt = zt });
            }
            return result;
        }

        /// <summary>Fouille autour d'une emprise rectangulaire : surlargeur au fond, talus en tete.</summary>
        public static Prism Pit(double minX, double minY, double maxX, double maxY, double margin, double slopeOffsetTop, double zb, double zt)
        {
            double m = Math.Max(0, margin), t = m + Math.Max(0, slopeOffsetTop);
            return new Prism
            {
                Bottom = new[] { minX - m, minY - m, maxX + m, minY - m, maxX + m, maxY + m, minX - m, maxY + m },
                Zb = zb,
                Top = new[] { minX - t, minY - t, maxX + t, minY - t, maxX + t, maxY + t, minX - t, maxY + t },
                Zt = zt
            };
        }

        public static string Encode(IEnumerable<Prism> prisms) =>
            string.Join(";", prisms.Select(p => string.Join(",", p.Bottom.Concat(new[] { p.Zb }).Concat(p.Top).Concat(new[] { p.Zt })
                .Select(v => Math.Round(v, 4).ToString(CultureInfo.InvariantCulture)))));

        public static List<Prism> Decode(string text)
        {
            var result = new List<Prism>();
            if (string.IsNullOrWhiteSpace(text)) return result;
            foreach (string part in text.Split(';'))
            {
                double[] v = part.Split(',').Select(x => double.TryParse(x, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : double.NaN).ToArray();
                if (v.Length != 18 || v.Any(double.IsNaN)) continue;
                result.Add(new Prism { Bottom = v.Take(8).ToArray(), Zb = v[8], Top = v.Skip(9).Take(8).ToArray(), Zt = v[17] });
            }
            return result;
        }

        /// <summary>Volume d'un prisme droit (haut = bas), pour les controles.</summary>
        public static double StraightVolume(Prism p) => Math.Abs(Area(p.Bottom)) * (p.Zt - p.Zb);

        private static double Area(double[] q)
        {
            double a = 0;
            for (int i = 0; i < 4; i++)
            {
                int j = (i + 1) % 4;
                a += q[2 * i] * q[2 * j + 1] - q[2 * j] * q[2 * i + 1];
            }
            return a / 2;
        }

        /// <summary>Coins dans le sens trigonometrique (normales des faces vers l'exterieur).</summary>
        private static double[] Ccw(double[] q)
        {
            if (Area(q) >= 0) return q;
            return new[] { q[6], q[7], q[4], q[5], q[2], q[3], q[0], q[1] };
        }

        /// <summary>Raccourcit une polyligne de « start » au debut et « end » a la fin (le long du trace).</summary>
        private static List<(double X, double Y)> Trim(List<(double X, double Y)> pts, double start, double end)
        {
            pts = Cut(pts, start);
            pts.Reverse();
            pts = Cut(pts, end);
            pts.Reverse();
            return pts;
        }

        private static List<(double X, double Y)> Cut(List<(double X, double Y)> pts, double d)
        {
            if (d <= 0) return pts;
            var result = new List<(double X, double Y)>();
            double remaining = d;
            for (int i = 0; i + 1 < pts.Count; i++)
            {
                double dx = pts[i + 1].X - pts[i].X, dy = pts[i + 1].Y - pts[i].Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (remaining >= len)
                {
                    remaining -= len;
                    continue;
                }
                double f = remaining / len;
                result.Add((pts[i].X + dx * f, pts[i].Y + dy * f));
                result.AddRange(pts.Skip(i + 1));
                return result;
            }
            return new List<(double X, double Y)>();
        }
    }
}
