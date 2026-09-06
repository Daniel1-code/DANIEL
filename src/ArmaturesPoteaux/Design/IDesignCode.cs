using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// Regles de ferraillage minimales d'un reglement, exprimees en mm / mm2 / MPa / N.
    /// Une implementation par norme supportee.
    /// </summary>
    public interface IDesignCode
    {
        string Name { get; }
        string ShortName { get; }

        /// <summary>Section d'acier longitudinal minimale (mm2).</summary>
        double MinSteelArea(double grossAreaMm2, double axialLoadN, double fykMPa, out string justification);

        /// <summary>Section d'acier longitudinal maximale hors recouvrement (mm2).</summary>
        double MaxSteelArea(double grossAreaMm2);

        /// <summary>Diametre minimal des barres longitudinales (mm).</summary>
        double MinLongitudinalDiameter { get; }

        /// <summary>Nombre minimal de barres longitudinales.</summary>
        int MinBarCount(SectionKind kind);

        /// <summary>Diametre minimal des armatures transversales (mm).</summary>
        double MinTransverseDiameter(double longitudinalDiameterMm);

        /// <summary>Espacement maximal des cadres en zone courante (mm).</summary>
        double MaxStirrupSpacing(double longitudinalDiameterMm, double transverseDiameterMm,
                                double minSectionDimensionMm, out string justification);

        /// <summary>Coefficient de reduction de l'espacement en zone critique.</summary>
        double CriticalZoneSpacingFactor { get; }

        /// <summary>Longueur des zones critiques en pied et en tete (mm).</summary>
        double CriticalZoneLength(double maxSectionDimensionMm, double clearHeightMm, bool seismic);

        /// <summary>Espacement libre minimal entre barres longitudinales (mm).</summary>
        double MinClearBarSpacing(double longitudinalDiameterMm, double aggregateSizeMm);

        /// <summary>Distance maximale entre une barre comprimee et la barre tenue la plus proche (mm).</summary>
        double MaxDistanceToRestrainedBar { get; }

        /// <summary>Longueur de recouvrement des barres longitudinales (mm).</summary>
        double LapLength(double barDiameterMm, double concreteStrengthMPa, double steelStrengthMPa,
                         out string justification);
    }
}
