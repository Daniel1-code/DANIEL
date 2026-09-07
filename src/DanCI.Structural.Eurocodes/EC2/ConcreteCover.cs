using System;
using DanCI.Structural.Core.Materials;

namespace DanCI.Structural.Eurocodes.EC2
{
    /// <summary>Enrobage calcule, avec le detail de chaque terme.</summary>
    public sealed class CoverResult
    {
        /// <summary>Classe structurale retenue (S1 a S6).</summary>
        public int StructuralClass { get; set; }

        /// <summary>Enrobage minimal vis-a-vis de l'adherence c_min,b (mm).</summary>
        public double MinCoverBondMm { get; set; }

        /// <summary>Enrobage minimal vis-a-vis de la durabilite c_min,dur (mm).</summary>
        public double MinCoverDurabilityMm { get; set; }

        /// <summary>Enrobage minimal c_min (mm).</summary>
        public double MinCoverMm { get; set; }

        /// <summary>Tolerance d'execution delta c_dev (mm).</summary>
        public double AllowanceMm { get; set; }

        /// <summary>Enrobage nominal c_nom (mm).</summary>
        public double NominalCoverMm { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Enrobage selon l'EN 1992-1-1:2004, article 4.4.1 :
    /// c_nom = c_min + delta c_dev, avec
    /// c_min = max(c_min,b ; c_min,dur + dc_dur,gamma - dc_dur,st - dc_dur,add ; 10 mm).
    ///
    /// La classe structurale de reference est S4 pour une duree d'utilisation de 50 ans
    /// (tableau 4.3N), modifiee par la duree d'utilisation, la classe de resistance, la
    /// geometrie de l'element et le controle de production. L'enrobage de durabilite
    /// c_min,dur est lu dans le tableau 4.4N.
    ///
    /// Les valeurs des tableaux 4.3N et 4.4N ainsi que delta c_dev sont des parametres
    /// susceptibles d'etre modifies par une Annexe Nationale.
    /// </summary>
    public static class ConcreteCover
    {
        /// <summary>Tolerance d'execution recommandee (mm), article 4.4.1.3(1)P.</summary>
        public const double RecommendedAllowanceMm = 10.0;

        /// <summary>
        /// Tableau 4.4N : enrobage minimal de durabilite pour les armatures de beton arme.
        /// Une ligne par classe d'exposition, une colonne par classe structurale S1 a S6.
        /// </summary>
        private static readonly double[][] DurabilityTable =
        {
            /* X0  */ new[] { 10.0, 10.0, 10.0, 10.0, 15.0, 20.0 },
            /* XC1 */ new[] { 10.0, 10.0, 10.0, 15.0, 20.0, 25.0 },
            /* XC2 */ new[] { 10.0, 15.0, 20.0, 25.0, 30.0, 35.0 },
            /* XC3 */ new[] { 10.0, 15.0, 20.0, 25.0, 30.0, 35.0 },
            /* XC4 */ new[] { 15.0, 20.0, 25.0, 30.0, 35.0, 40.0 },
            /* XD1 */ new[] { 20.0, 25.0, 30.0, 35.0, 40.0, 45.0 },
            /* XD2 */ new[] { 25.0, 30.0, 35.0, 40.0, 45.0, 50.0 },
            /* XD3 */ new[] { 30.0, 35.0, 40.0, 45.0, 50.0, 55.0 },
            /* XS1 */ new[] { 20.0, 25.0, 30.0, 35.0, 40.0, 45.0 },
            /* XS2 */ new[] { 25.0, 30.0, 35.0, 40.0, 45.0, 50.0 },
            /* XS3 */ new[] { 30.0, 35.0, 40.0, 45.0, 50.0, 55.0 }
        };

        /// <summary>
        /// Tableau 4.3N : classe de resistance a partir de laquelle la classe structurale
        /// est reduite d'une unite, pour chaque classe d'exposition.
        /// </summary>
        private static double StrengthThreshold(ExposureClass exposure)
        {
            switch (exposure)
            {
                case ExposureClass.X0:
                case ExposureClass.XC1:
                    return 30.0;   // C30/37
                case ExposureClass.XC2:
                case ExposureClass.XC3:
                    return 35.0;   // C35/45
                case ExposureClass.XC4:
                case ExposureClass.XD1:
                case ExposureClass.XD2:
                case ExposureClass.XS1:
                    return 40.0;   // C40/50
                default:
                    return 45.0;   // C45/55 pour XD3, XS2 et XS3
            }
        }

        /// <summary>
        /// Classe structurale selon le tableau 4.3N. S4 de reference, puis :
        /// +2 pour 100 ans de duree d'utilisation, -1 si la resistance depasse le seuil,
        /// -1 pour un element de type dalle, -1 en cas de controle de production assure.
        /// </summary>
        public static int StructuralClass(ExposureClass exposure, double concreteStrengthMPa,
                                          DesignWorkingLife life, bool slabGeometry,
                                          bool specialQualityControl)
        {
            int structuralClass = 4;
            if (life == DesignWorkingLife.Years100) structuralClass += 2;
            if (concreteStrengthMPa >= StrengthThreshold(exposure)) structuralClass -= 1;
            if (slabGeometry) structuralClass -= 1;
            if (specialQualityControl) structuralClass -= 1;

            if (structuralClass < 1) structuralClass = 1;
            if (structuralClass > 6) structuralClass = 6;
            return structuralClass;
        }

        /// <summary>Enrobage minimal de durabilite c_min,dur (mm), tableau 4.4N.</summary>
        public static double MinimumDurabilityCover(ExposureClass exposure, int structuralClass)
        {
            int row = (int)exposure;
            int column = structuralClass - 1;
            if (column < 0) column = 0;
            if (column > 5) column = 5;
            return DurabilityTable[row][column];
        }

        /// <summary>
        /// Calcule l'enrobage nominal d'un element.
        /// </summary>
        /// <param name="barDiameterMm">Diametre de la barre la plus proche du parement.</param>
        /// <param name="exposure">Classe d'exposition.</param>
        /// <param name="concreteStrengthMPa">f_ck du beton.</param>
        /// <param name="life">Duree d'utilisation de projet.</param>
        /// <param name="slabGeometry">Element de type dalle (armatures en nappe).</param>
        /// <param name="specialQualityControl">Controle de production du beton assure.</param>
        /// <param name="allowanceMm">Tolerance d'execution delta c_dev.</param>
        public static CoverResult Compute(double barDiameterMm, ExposureClass exposure,
                                          double concreteStrengthMPa,
                                          DesignWorkingLife life = DesignWorkingLife.Years50,
                                          bool slabGeometry = false,
                                          bool specialQualityControl = false,
                                          double allowanceMm = RecommendedAllowanceMm)
        {
            int structuralClass = StructuralClass(exposure, concreteStrengthMPa, life, slabGeometry,
                                                  specialQualityControl);
            double durability = MinimumDurabilityCover(exposure, structuralClass);

            // 4.4.1.2(3) : c_min,b = diametre de la barre (barres isolees).
            double bond = barDiameterMm;

            // Les majorations dc_dur,gamma, dc_dur,st et dc_dur,add valent 0 par defaut.
            double minimum = Math.Max(bond, Math.Max(durability, 10.0));
            double nominal = minimum + allowanceMm;

            return new CoverResult
            {
                StructuralClass = structuralClass,
                MinCoverBondMm = bond,
                MinCoverDurabilityMm = durability,
                MinCoverMm = minimum,
                AllowanceMm = allowanceMm,
                NominalCoverMm = nominal,
                Justification = string.Format(
                    "EC2 4.4.1 : classe {0}, classe structurale S{1} (tableau 4.3N) => " +
                    "c_min,dur = {2:0} mm (tableau 4.4N) ; c_min,b = {3:0} mm ; " +
                    "c_min = max({3:0} ; {2:0} ; 10) = {4:0} mm ; " +
                    "c_nom = c_min + delta c_dev = {4:0} + {5:0} = {6:0} mm",
                    exposure, structuralClass, durability, bond, minimum, allowanceMm, nominal)
            };
        }
    }
}
