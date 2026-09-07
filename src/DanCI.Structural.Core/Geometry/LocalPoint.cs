namespace DanCI.Structural.Core.Geometry
{
    /// <summary>
    /// Point exprime dans le repere local d'un element structurel, en millimetres.
    /// X et Y sont les axes de la section, Z l'axe de l'element.
    /// </summary>
    public readonly struct LocalPoint
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public LocalPoint(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public LocalPoint WithZ(double z)
        {
            return new LocalPoint(X, Y, z);
        }

        public override string ToString()
        {
            return string.Format("({0:0.#}, {1:0.#}, {2:0.#})", X, Y, Z);
        }
    }

    /// <summary>Direction unitaire dans le repere local d'un element.</summary>
    public readonly struct LocalVector
    {
        public double X { get; }
        public double Y { get; }
        public double Z { get; }

        public LocalVector(double x, double y, double z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static LocalVector AxisX { get { return new LocalVector(1, 0, 0); } }
        public static LocalVector AxisY { get { return new LocalVector(0, 1, 0); } }
        public static LocalVector AxisZ { get { return new LocalVector(0, 0, 1); } }
    }
}
