using Autodesk.Revit.DB;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>
    /// Repere local d'un element Revit : origine et trois axes. C'est la seule passerelle
    /// entre les coordonnees du moteur (mm, repere local) et celles de Revit (pieds, monde).
    /// </summary>
    public sealed class RevitElementFrame
    {
        public Element Host { get; set; }

        /// <summary>Origine du repere local, en unites Revit.</summary>
        public XYZ Origin { get; set; }

        public XYZ AxisX { get; set; }
        public XYZ AxisY { get; set; }
        public XYZ AxisZ { get; set; }

        /// <summary>Convertit un point local (mm) en point Revit.</summary>
        public XYZ ToWorld(LocalPoint point)
        {
            return Origin
                   + AxisX.Multiply(UnitConverter.MmToFeet(point.X))
                   + AxisY.Multiply(UnitConverter.MmToFeet(point.Y))
                   + AxisZ.Multiply(UnitConverter.MmToFeet(point.Z));
        }

        /// <summary>Convertit un point local exprime en millimetres.</summary>
        public XYZ ToWorld(double xMm, double yMm, double zMm)
        {
            return ToWorld(new LocalPoint(xMm, yMm, zMm));
        }

        /// <summary>Convertit une direction locale en direction Revit.</summary>
        public XYZ ToWorldDirection(LocalVector direction)
        {
            return (AxisX.Multiply(direction.X)
                    + AxisY.Multiply(direction.Y)
                    + AxisZ.Multiply(direction.Z)).Normalize();
        }
    }
}
