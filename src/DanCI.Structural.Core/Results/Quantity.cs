using System.Globalization;

namespace DanCI.Structural.Core.Results
{
    /// <summary>Une valeur numerique accompagnee de son unite, pour l'affichage et les rapports.</summary>
    public readonly struct Quantity
    {
        public double Value { get; }
        public string Unit { get; }
        public int Decimals { get; }

        public Quantity(double value, string unit, int decimals = 1)
        {
            Value = value;
            Unit = unit;
            Decimals = decimals;
        }

        public static Quantity Force(double kilonewtons)
        {
            return new Quantity(kilonewtons, "kN", 1);
        }

        public static Quantity Moment(double kilonewtonMetres)
        {
            return new Quantity(kilonewtonMetres, "kN.m", 1);
        }

        public static Quantity Length(double millimetres)
        {
            return new Quantity(millimetres, "mm", 0);
        }

        public static Quantity Area(double squareMillimetres)
        {
            return new Quantity(squareMillimetres, "mm2", 0);
        }

        public static Quantity Stress(double megapascals)
        {
            return new Quantity(megapascals, "MPa", 2);
        }

        public static Quantity Ratio(double value)
        {
            return new Quantity(value, string.Empty, 2);
        }

        public override string ToString()
        {
            string number = Value.ToString("F" + Decimals, CultureInfo.CurrentCulture);
            return string.IsNullOrEmpty(Unit) ? number : number + " " + Unit;
        }
    }
}
