using System;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// ACI 318-19 : article 10.6 (limites d'armature), 25.7.2 (cadres) et 25.5.5 (recouvrements
    /// de barres comprimees). Les diametres restent metriques : les valeurs sont donc les
    /// equivalents metriques des regles imperiales.
    /// </summary>
    public class Aci318Code : IDesignCode
    {
        public string Name { get { return "ACI 318-19 (10.6 / 25.7.2)"; } }

        public string ShortName { get { return "ACI"; } }

        public double MinSteelArea(double grossAreaMm2, double axialLoadN, double fykMPa, out string justification)
        {
            double result = 0.01 * grossAreaMm2; // ACI 318-19 10.6.1.1 : rho_min = 1 %
            justification = string.Format(
                "ACI 318-19 10.6.1.1 : As,min = 0,01 Ag = {0:0} mm2", result);
            return result;
        }

        public double MaxSteelArea(double grossAreaMm2)
        {
            return 0.08 * grossAreaMm2; // ACI 318-19 10.6.1.1 : rho_max = 8 %
        }

        public double MinLongitudinalDiameter { get { return 10.0; } } // barre #3 ~ 9,5 mm

        public int MinBarCount(SectionKind kind)
        {
            return kind == SectionKind.Circular ? 6 : 4; // ACI 318-19 10.7.3.1
        }

        public double MinTransverseDiameter(double longitudinalDiameterMm)
        {
            // ACI 318-19 25.7.2.2 : #3 jusqu'aux barres #10 (32 mm), #4 au-dela.
            return longitudinalDiameterMm <= 32.0 ? 10.0 : 12.0;
        }

        public double MaxStirrupSpacing(double longitudinalDiameterMm, double transverseDiameterMm,
                                        double minSectionDimensionMm, out string justification)
        {
            double byLong = 16.0 * longitudinalDiameterMm;
            double byTie = 48.0 * transverseDiameterMm;
            double result = Math.Min(byLong, Math.Min(byTie, minSectionDimensionMm));
            justification = string.Format(
                "ACI 318-19 25.7.2.1 : s = min(16 db ; 48 dbt ; plus petite dimension) = " +
                "min({0:0} ; {1:0} ; {2:0}) = {3:0} mm",
                byLong, byTie, minSectionDimensionMm, result);
            return result;
        }

        public double CriticalZoneSpacingFactor { get { return 0.5; } }

        public double CriticalZoneLength(double maxSectionDimensionMm, double clearHeightMm, bool seismic)
        {
            // ACI 318-19 18.7.5.1 : lo = max(h ; ln/6 ; 450 mm)
            return Math.Max(maxSectionDimensionMm, Math.Max(clearHeightMm / 6.0, 450.0));
        }

        public double MinClearBarSpacing(double longitudinalDiameterMm, double aggregateSizeMm)
        {
            // ACI 318-19 25.2.3 : max(40 mm ; 1,5 db ; 4/3 dagg)
            return Math.Max(40.0, Math.Max(1.5 * longitudinalDiameterMm, 4.0 * aggregateSizeMm / 3.0));
        }

        public double MaxDistanceToRestrainedBar { get { return 150.0; } } // ACI 318-19 25.7.2.3

        public double LapLength(double barDiameterMm, double concreteStrengthMPa, double steelStrengthMPa,
                                out string justification)
        {
            // ACI 318-19 25.4.9.2 : ldc = max(0,24 fy db / (lambda sqrt(f'c)) ; 0,043 fy db)
            double byBond = 0.24 * steelStrengthMPa * barDiameterMm / Math.Sqrt(concreteStrengthMPa);
            double byMin = 0.043 * steelStrengthMPa * barDiameterMm;
            double ldc = Math.Max(byBond, Math.Max(byMin, 200.0));

            // ACI 318-19 25.5.5.1 : recouvrement comprime = max(ldc ; 0,071 fy db ; 300 mm)
            double lap = Math.Max(ldc, Math.Max(0.071 * steelStrengthMPa * barDiameterMm, 300.0));
            justification = string.Format(
                "ACI 318-19 25.4.9.2 / 25.5.5.1 : ldc = {0:0} mm, recouvrement = {1:0} mm (soit {2:0} db)",
                ldc, lap, lap / barDiameterMm);
            return lap;
        }
    }
}
