using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>
    /// Donnees d'un voile en beton arme, independantes de Revit. Dimensions en millimetres.
    ///
    /// Le calcul du voile porteur se mene sur une **bande verticale de 1 metre**, comme un
    /// poteau de section 1 000 x t. La verification de contreventement, elle, porte sur le
    /// voile entier : les deux echelles cohabitent et ne doivent pas etre confondues.
    /// </summary>
    public sealed class WallData
    {
        /// <summary>Largeur de la bande de calcul verticale (mm).</summary>
        public const double StripWidthMm = 1000.0;

        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Epaisseur du voile t (mm).</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Longueur du voile en plan (mm).</summary>
        public double LengthMm { get; set; }

        /// <summary>Hauteur libre entre planchers (mm).</summary>
        public double ClearHeightMm { get; set; }

        public List<string> Remarks { get; private set; }

        public WallData()
        {
            Remarks = new List<string>();
            Name = "Voile";
            Mark = "V";
            ThicknessMm = 200.0;
        }

        /// <summary>Aire de beton de la bande de calcul (mm2).</summary>
        public double StripAreaMm2 { get { return StripWidthMm * ThicknessMm; } }

        /// <summary>Aire de la section horizontale complete du voile (mm2).</summary>
        public double GrossAreaMm2 { get { return LengthMm * ThicknessMm; } }

        /// <summary>Volume de beton (mm3).</summary>
        public double VolumeMm3 { get { return GrossAreaMm2 * ClearHeightMm; } }

        /// <summary>
        /// Elancement geometrique de la bande, hauteur libre sur epaisseur. Au-dela de 30
        /// environ, le second ordre hors plan devient dimensionnant.
        /// </summary>
        public double HeightToThickness
        {
            get { return ThicknessMm > 0 ? ClearHeightMm / ThicknessMm : 0.0; }
        }

        /// <summary>
        /// L'element est-il vraiment un voile ? L'article 9.6.1 le definit par
        /// longueur &gt;= 4 x epaisseur. En deca, c'est un poteau, et ce sont les
        /// dispositions de l'article 9.5 qui s'appliquent, pas celles de 9.6.
        /// </summary>
        public bool IsWallByCode
        {
            get { return ThicknessMm > 0 && LengthMm >= 4.0 * ThicknessMm; }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format("{0:0} x {1:0} mm - hauteur {2:0} mm",
                                     LengthMm, ThicknessMm, ClearHeightMm);
            }
        }
    }
}
