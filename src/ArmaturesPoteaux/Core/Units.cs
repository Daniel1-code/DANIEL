using Autodesk.Revit.DB;

namespace ArmaturesPoteaux.Core
{
    /// <summary>
    /// Conversions entre les unites internes de Revit (pieds) et les millimetres
    /// utilises par toute la partie calcul du plugin.
    /// </summary>
    public static class Units
    {
        /// <summary>Millimetres -> unites internes Revit.</summary>
        public static double MmToFeet(double mm)
        {
            return UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
        }

        /// <summary>Unites internes Revit -> millimetres.</summary>
        public static double FeetToMm(double feet)
        {
            return UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
        }

        /// <summary>Radians -> degres (pour la lecture des parametres d'angle Revit).</summary>
        public static double RadiansToDegrees(double radians)
        {
            return UnitUtils.ConvertFromInternalUnits(radians, UnitTypeId.Degrees);
        }
    }
}
