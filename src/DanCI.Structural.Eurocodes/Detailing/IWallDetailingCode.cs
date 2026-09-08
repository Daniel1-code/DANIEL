namespace DanCI.Structural.Eurocodes.Detailing
{
    /// <summary>
    /// Dispositions constructives applicables aux voiles en beton arme.
    /// Une implementation par reglement. Longueurs en millimetres, sections en mm2.
    /// </summary>
    public interface IWallDetailingCode
    {
        string Name { get; }
        string ShortName { get; }

        /// <summary>Section verticale minimale, pour une aire de beton donnee.</summary>
        double MinVerticalSteel(double grossAreaMm2, out string justification);

        /// <summary>Section verticale maximale.</summary>
        double MaxVerticalSteel(double grossAreaMm2, bool atLaps);

        /// <summary>Espacement maximal des aciers verticaux.</summary>
        double MaxVerticalSpacingMm(double thicknessMm, out string justification);

        /// <summary>Section horizontale minimale, fonction de la section verticale posee.</summary>
        double MinHorizontalSteel(double grossAreaMm2, double verticalSteelMm2,
                                  out string justification);

        /// <summary>Espacement maximal des aciers horizontaux.</summary>
        double MaxHorizontalSpacingMm { get; }

        /// <summary>
        /// Des epingles de liaison entre nappes sont-elles exigees ?
        /// </summary>
        bool RequiresTransverseLinks(double grossAreaMm2, double verticalSteelMm2);

        /// <summary>Nombre d'epingles au metre carre, lorsqu'elles sont exigees.</summary>
        double LinksPerSquareMetre { get; }
    }
}
