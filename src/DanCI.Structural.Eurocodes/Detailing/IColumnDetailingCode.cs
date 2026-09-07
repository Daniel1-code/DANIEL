using DanCI.Structural.Core.Geometry;

namespace DanCI.Structural.Eurocodes.Detailing
{
    /// <summary>
    /// Dispositions constructives applicables aux poteaux. Une implementation par reglement.
    /// Les valeurs renvoyees sont en millimetres et millimetres carres.
    /// </summary>
    public interface IColumnDetailingCode
    {
        string Name { get; }
        string ShortName { get; }

        double MinSteelArea(double grossAreaMm2, double axialForceN, double steelStrengthMPa,
                            out string justification);

        double MaxSteelArea(double grossAreaMm2);

        double MinLongitudinalDiameterMm { get; }

        int MinBarCount(SectionShape shape);

        double MinTransverseDiameterMm(double longitudinalDiameterMm);

        double MaxStirrupSpacingMm(double longitudinalDiameterMm, double transverseDiameterMm,
                                   double minSectionDimensionMm, out string justification);

        double CriticalZoneSpacingFactor { get; }

        double CriticalZoneLengthMm(double maxSectionDimensionMm, double clearHeightMm, bool seismic);

        double MinClearBarSpacingMm(double longitudinalDiameterMm, double aggregateSizeMm);

        double MaxDistanceToRestrainedBarMm { get; }
    }
}
