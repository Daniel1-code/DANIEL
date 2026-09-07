using System;
using DanCI.Structural.Core.Geometry;

namespace DanCI.Structural.Eurocodes.Detailing
{
    /// <summary>
    /// Dispositions constructives des poteaux selon l'ACI 318-19, articles 10.6, 10.7.3
    /// et 25.7.2. Conserve pour les projets hors Europe ; hors du chemin critique du moteur
    /// Eurocode, il n'est pas melange aux formules EN 1992.
    /// Les diametres restent metriques : les valeurs sont les equivalents metriques des
    /// regles imperiales.
    /// </summary>
    public sealed class Aci318ColumnDetailing : IColumnDetailingCode
    {
        public string Name { get { return "ACI 318-19 art. 10.6, 10.7.3 et 25.7.2"; } }

        public string ShortName { get { return "ACI"; } }

        public double MinSteelArea(double grossAreaMm2, double axialForceN, double steelStrengthMPa,
                                   out string justification)
        {
            double result = 0.01 * grossAreaMm2;   // 10.6.1.1 : rho_min = 1 %
            justification = string.Format("ACI 318-19 10.6.1.1 : As,min = 0,01 Ag = {0:0} mm2", result);
            return result;
        }

        public double MaxSteelArea(double grossAreaMm2)
        {
            return 0.08 * grossAreaMm2;            // 10.6.1.1 : rho_max = 8 %
        }

        public double MinLongitudinalDiameterMm { get { return 10.0; } }  // barre #3

        public int MinBarCount(SectionShape shape)
        {
            return shape == SectionShape.Circular ? 6 : 4;                // 10.7.3.1
        }

        public double MinTransverseDiameterMm(double longitudinalDiameterMm)
        {
            // 25.7.2.2 : #3 jusqu'aux barres #10 (32 mm), #4 au-dela
            return longitudinalDiameterMm <= 32.0 ? 10.0 : 12.0;
        }

        public double MaxStirrupSpacingMm(double longitudinalDiameterMm, double transverseDiameterMm,
                                          double minSectionDimensionMm, out string justification)
        {
            double byLongitudinal = 16.0 * longitudinalDiameterMm;
            double byTie = 48.0 * transverseDiameterMm;
            double result = Math.Min(byLongitudinal, Math.Min(byTie, minSectionDimensionMm));
            justification = string.Format(
                "ACI 318-19 25.7.2.1 : s = min(16 db ; 48 dbt ; plus petite dimension) = " +
                "min({0:0} ; {1:0} ; {2:0}) = {3:0} mm",
                byLongitudinal, byTie, minSectionDimensionMm, result);
            return result;
        }

        public double CriticalZoneSpacingFactor { get { return 0.5; } }

        public double CriticalZoneLengthMm(double maxSectionDimensionMm, double clearHeightMm, bool seismic)
        {
            // 18.7.5.1 : lo = max(h ; ln/6 ; 450 mm)
            return Math.Max(maxSectionDimensionMm, Math.Max(clearHeightMm / 6.0, 450.0));
        }

        public double MinClearBarSpacingMm(double longitudinalDiameterMm, double aggregateSizeMm)
        {
            // 25.2.3 : max(40 mm ; 1,5 db ; 4/3 dagg)
            return Math.Max(40.0, Math.Max(1.5 * longitudinalDiameterMm, 4.0 * aggregateSizeMm / 3.0));
        }

        public double MaxDistanceToRestrainedBarMm { get { return 150.0; } }   // 25.7.2.3
    }
}
