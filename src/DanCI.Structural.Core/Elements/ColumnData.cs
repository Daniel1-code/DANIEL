using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Geometry;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>
    /// Donnees geometriques d'un poteau, independantes de Revit. Toutes les dimensions sont
    /// en millimetres.
    /// </summary>
    public sealed class ColumnData
    {
        /// <summary>Identifiant stable de l'element dans le modele source.</summary>
        public string Id { get; set; }

        /// <summary>Libelle lisible, utilise dans les tableaux et les rapports.</summary>
        public string Name { get; set; }

        public SectionShape Shape { get; set; }

        /// <summary>Largeur suivant l'axe local X (section rectangulaire).</summary>
        public double WidthMm { get; set; }

        /// <summary>Profondeur suivant l'axe local Y (section rectangulaire).</summary>
        public double DepthMm { get; set; }

        /// <summary>Diametre (section circulaire).</summary>
        public double DiameterMm { get; set; }

        /// <summary>Hauteur libre du poteau.</summary>
        public double HeightMm { get; set; }

        public List<string> Remarks { get; private set; }

        public ColumnData()
        {
            Remarks = new List<string>();
            Name = "Poteau";
        }

        public double MinDimensionMm
        {
            get
            {
                if (Shape == SectionShape.Circular) return DiameterMm;
                return Math.Min(WidthMm, DepthMm);
            }
        }

        public double MaxDimensionMm
        {
            get
            {
                if (Shape == SectionShape.Circular) return DiameterMm;
                return Math.Max(WidthMm, DepthMm);
            }
        }

        /// <summary>Aire brute de beton (mm2).</summary>
        public double GrossAreaMm2
        {
            get
            {
                if (Shape == SectionShape.Circular)
                {
                    return Math.PI * DiameterMm * DiameterMm / 4.0;
                }
                return WidthMm * DepthMm;
            }
        }

        /// <summary>Hauteur de section vue par une flexion autour de l'axe X (mm).</summary>
        public double HeightForBendingAboutX
        {
            get { return Shape == SectionShape.Circular ? DiameterMm : DepthMm; }
        }

        /// <summary>Hauteur de section vue par une flexion autour de l'axe Y (mm).</summary>
        public double HeightForBendingAboutY
        {
            get { return Shape == SectionShape.Circular ? DiameterMm : WidthMm; }
        }

        public string SectionLabel
        {
            get
            {
                if (Shape == SectionShape.Circular)
                {
                    return string.Format("D{0:0}", DiameterMm);
                }
                return string.Format("{0:0} x {1:0}", WidthMm, DepthMm);
            }
        }
    }
}
