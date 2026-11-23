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

        /// <summary>Marge autour du batiment (1,06 = 6 %) : il occupe presque tout l'ecran.</summary>
        public const double Margin = 1.06;

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

        /// <summary>
        /// Angle d'orbite de la face « Avant » du ViewCube : camera au sud, regard vers le nord (+Y),
        /// a l'horizontale (elevation 0).
        /// </summary>
        public const double FrontAngleDeg = 90;

        /// <summary>Nombre d'images de l'atterrissage final sur la vue de face.</summary>
        public const int LandingFrames = 24;

        /// <summary>Courbe douce (depart et arrivee lents) : 0 -> 0, 1 -> 1.</summary>
        public static double Smooth(double t)
        {
            t = Math.Max(0, Math.Min(1, t));
            return t * t * (3 - 2 * t);
        }

        /// <summary>
        /// Atterrissage sur la vue de face : de l'angle de depart (elevation 25°) vers la face Avant
        /// du ViewCube (angle 90°, elevation 0°) par le chemin le plus court, avec une courbe douce.
        /// </summary>
        public static (double AngleDeg, double ElevationDeg) Landing(double startAngleDeg, double t)
        {
            double delta = ((FrontAngleDeg - startAngleDeg) % 360 + 540) % 360 - 180; // -180..180 : chemin le plus court
            double k = Smooth(t);
            return (startAngleDeg + delta * k, ElevationDeg * (1 - k));
        }

        /// <summary>Direction de visee et vecteur haut pour un angle d'orbite (degres).</summary>
        public static (Vec Forward, Vec Up) Orientation(double angleDeg) => Orientation(angleDeg, ElevationDeg);

        /// <summary>Direction de visee et vecteur haut pour un angle d'orbite et une elevation (degres).</summary>
        public static (Vec Forward, Vec Up) Orientation(double angleDeg, double elevationDeg)
        {
            double th = angleDeg * Math.PI / 180, ph = elevationDeg * Math.PI / 180;
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
        /// Les deux coins du rectangle de zoom, dans le plan de l'ecran passant par le centre du
        /// batiment. Largeur = diagonale en plan (la plus grande largeur vue de n'importe quel cote),
        /// hauteur = plus grande hauteur apparente sur un tour (hauteur x cos(elevation) + diagonale
        /// en plan x sin(elevation)). Le batiment REMPLIT l'ecran a l'angle le plus large et ne
        /// deborde jamais ; le cadre ne depend pas de l'angle : le zoom ne bouge pas pendant la rotation.
        /// </summary>
        public static (Vec A, Vec B) ZoomCorners(Vec min, Vec max, Vec forward, Vec up) => ZoomCorners(min, max, forward, up, ElevationDeg);

        public static (Vec A, Vec B) ZoomCorners(Vec min, Vec max, Vec forward, Vec up, double elevationDeg)
        {
            Vec c = (min + max) * 0.5;
            Vec d = max - min;
            double plan = Math.Sqrt(d.X * d.X + d.Y * d.Y);
            double ph = Math.Max(0, elevationDeg) * Math.PI / 180;
            double halfW = Math.Max(1.5, 0.5 * plan) * Margin;
            double halfH = Math.Max(1.5, 0.5 * Math.Abs(d.Z) * Math.Cos(ph) + 0.5 * plan * Math.Sin(ph)) * Margin;
            Vec right = forward.Cross(up);
            double n = right.Length;
            right = n > 1e-9 ? right * (1 / n) : new Vec(1, 0, 0);
            double u = up.Length;
            Vec upN = u > 1e-9 ? up * (1 / u) : new Vec(0, 0, 1);
            return (c - right * halfW - upN * halfH, c + right * halfW + upN * halfH);
        }
    }
}
