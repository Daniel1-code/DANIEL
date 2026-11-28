using Autodesk.Revit.DB;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>Conversions depuis les unites internes Revit (pieds) — toujours par UnitUtils.</summary>
    internal static class RevitUnits
    {
        public static double ToCubicMeters(double internalVolume) =>
            UnitUtils.ConvertFromInternalUnits(internalVolume, UnitTypeId.CubicMeters);

        public static double ToSquareMeters(double internalArea) =>
            UnitUtils.ConvertFromInternalUnits(internalArea, UnitTypeId.SquareMeters);

        public static double ToMeters(double internalLength) =>
            UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Meters);

        public static double ToMillimeters(double internalLength) =>
            UnitUtils.ConvertFromInternalUnits(internalLength, UnitTypeId.Millimeters);
    }
}
