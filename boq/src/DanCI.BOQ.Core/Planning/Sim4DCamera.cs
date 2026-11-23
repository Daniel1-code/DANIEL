using System;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>
    /// Camera du Studio 4D (calcul pur, sans Revit) : orbite autour du batiment et cadrage
    /// CONSTANT. Le cadrage est un carre pose dans le plan de l'ecran, centre sur le batiment, de
    /// cote = diametre de la sphere qui l'englobe : quel que soit l'angle de rotation, le batiment
    /// entier tient a l'ecran et sa taille ne change pas (pas de zoom qui saute).
    /// </summary>
    public static class Sim4DCamera
    {
        /// <summary>Hauteur de la camera au-dessus de l'horizon (degres).</summary>
        public const double ElevationDeg = 25;

        /// <summary>Marge autour du batiment (1,15 = 15 %).</summary>
        public const double Margin = 1.15;

        public struct Vec
        {
            public double X, Y, Z;
            public Vec(double x, double y, double z) { X = x; Y = y; Z = z; }
            public static Vec operator +(Vec a, Vec b) => new Vec(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
            public static Vec operator -(Vec a, Vec b) => new Vec(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
            public static Vec operator *(Vec a, double k) => new Vec(a.X * k, a.Y * k, a.Z * k);
            public double Dot(Vec b) => X * b.X + Y * b.Y + Z * b.Z;
            public Vec Cross(Vec b) => new Vec(Y * b.Z - Z * b.Y, Z * b.X - X * b.Z, X * b.Y - Y * b.X);
            public double Length => Math.Sqrt(Dot(this));
        }

        /// <summary>Direction de visee et vecteur haut pour un angle d'orbite (degres).</summary>
        public static (Vec Forward, Vec Up) Orientation(double angleDeg)
        {
            double th = angleDeg * Math.PI / 180, ph = ElevationDeg * Math.PI / 180;
            var forward = new Vec(Math.Cos(th) * Math.Cos(ph), Math.Sin(th) * Math.Cos(ph), -Math.Sin(ph));
            var up = new Vec(Math.Cos(th) * Math.Sin(ph), Math.Sin(th) * Math.Sin(ph), Math.Cos(ph));
            return (forward, up);
        }

        /// <summary>Oeil de la camera : recule de la diagonale du batiment, face au centre.</summary>
        public static Vec Eye(Vec min, Vec max, Vec forward)
        {
            Vec c = (min + max) * 0.5;
            double r = Math.Max(10, (max - min).Length);
            return c - forward * r;
        }

        /// <summary>
        /// Les deux coins du rectangle de zoom : carre dans le plan de l'ecran passant par le centre
        /// du batiment, de demi-cote = rayon de la sphere englobante x marge. Independant de l'angle.
        /// </summary>
        public static (Vec A, Vec B) ZoomCorners(Vec min, Vec max, Vec forward, Vec up)
        {
            Vec c = (min + max) * 0.5;
            double half = Math.Max(3, (max - min).Length * 0.5) * Margin;
            Vec right = forward.Cross(up);
            double n = right.Length;
            right = n > 1e-9 ? right * (1 / n) : new Vec(1, 0, 0);
            double u = up.Length;
            Vec upN = u > 1e-9 ? up * (1 / u) : new Vec(0, 0, 1);
            return (c - right * half - upN * half, c + right * half + upN * half);
        }
    }
}
