using System;

namespace DanCI.Structural.Core.Units
{
    /// <summary>
    /// Systeme d unites unique du moteur : <b>N, mm, MPa</b> (donc N.mm pour les moments).
    /// Toutes les conversions vivent ici ; aucune conversion ne doit etre dispersee ailleurs
    /// dans le projet.
    /// </summary>
    public static class UnitConverter
    {
        /// <summary>Unite interne de longueur de Revit : le pied international.</summary>
        public const double MillimetresPerFoot = 304.8;

        public static double MmToFeet(double millimetres)
        {
            return millimetres / MillimetresPerFoot;
        }

        public static double FeetToMm(double feet)
        {
            return feet * MillimetresPerFoot;
        }

        public static double RadiansToDegrees(double radians)
        {
            return radians * 180.0 / Math.PI;
        }

        public static double DegreesToRadians(double degrees)
        {
            return degrees * Math.PI / 180.0;
        }

        /// <summary>kN -> N.</summary>
        public static double KnToN(double kilonewtons)
        {
            return kilonewtons * 1000.0;
        }

        /// <summary>N -> kN.</summary>
        public static double NToKn(double newtons)
        {
            return newtons / 1000.0;
        }

        /// <summary>kN.m -> N.mm.</summary>
        public static double KnmToNmm(double kilonewtonMetres)
        {
            return kilonewtonMetres * 1e6;
        }

        /// <summary>N.mm -> kN.m.</summary>
        public static double NmmToKnm(double newtonMillimetres)
        {
            return newtonMillimetres / 1e6;
        }

        /// <summary>mm3 -> m3.</summary>
        public static double Mm3ToM3(double cubicMillimetres)
        {
            return cubicMillimetres * 1e-9;
        }

        /// <summary>Aire d'une barre de diametre nominal donne (mm2).</summary>
        public static double BarArea(double diameterMm)
        {
            return Math.PI * diameterMm * diameterMm / 4.0;
        }
    }
}
