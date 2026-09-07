namespace DanCI.Structural.Core.Materials
{
    /// <summary>
    /// Classes d'exposition de l'EN 1992-1-1, tableau 4.1. Elles conditionnent l'enrobage
    /// minimal vis-a-vis de la durabilite.
    /// </summary>
    public enum ExposureClass
    {
        /// <summary>Aucun risque de corrosion ni d'attaque.</summary>
        X0,

        /// <summary>Corrosion par carbonatation - sec ou en permanence humide.</summary>
        XC1,
        /// <summary>Corrosion par carbonatation - humide, rarement sec.</summary>
        XC2,
        /// <summary>Corrosion par carbonatation - humidite moderee.</summary>
        XC3,
        /// <summary>Corrosion par carbonatation - alternance d'humidite et de sechage.</summary>
        XC4,

        /// <summary>Chlorures hors eau de mer - humidite moderee.</summary>
        XD1,
        /// <summary>Chlorures hors eau de mer - humide, rarement sec.</summary>
        XD2,
        /// <summary>Chlorures hors eau de mer - alternance d'humidite et de sechage.</summary>
        XD3,

        /// <summary>Chlorures de l'eau de mer - air vehiculant du sel marin.</summary>
        XS1,
        /// <summary>Chlorures de l'eau de mer - immersion permanente.</summary>
        XS2,
        /// <summary>Chlorures de l'eau de mer - zones de marnage.</summary>
        XS3
    }

    /// <summary>Duree d'utilisation de projet, EN 1990 tableau 2.1.</summary>
    public enum DesignWorkingLife
    {
        /// <summary>50 ans : batiments et autres structures courantes.</summary>
        Years50,
        /// <summary>100 ans : structures monumentales, ponts, ouvrages de genie civil.</summary>
        Years100
    }
}
