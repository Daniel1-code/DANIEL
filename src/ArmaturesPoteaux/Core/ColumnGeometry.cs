using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace ArmaturesPoteaux.Core
{
    public enum SectionKind
    {
        Rectangular,
        Circular
    }

    /// <summary>
    /// Geometrie exploitable d'un poteau : section, hauteur et repere local.
    /// Toutes les dimensions sont en millimetres, le repere est en unites Revit.
    /// </summary>
    public class ColumnGeometry
    {
        /// <summary>Element Revit hote des armatures.</summary>
        public Element Host { get; set; }

        public ElementId HostId
        {
            get { return Host != null ? Host.Id : ElementId.InvalidElementId; }
        }

        public string HostName { get; set; }

        public SectionKind Kind { get; set; }

        /// <summary>Largeur de la section suivant l'axe local X (mm). Rectangulaire uniquement.</summary>
        public double WidthMm { get; set; }

        /// <summary>Profondeur de la section suivant l'axe local Y (mm). Rectangulaire uniquement.</summary>
        public double DepthMm { get; set; }

        /// <summary>Diametre (mm). Section circulaire uniquement.</summary>
        public double DiameterMm { get; set; }

        /// <summary>Hauteur libre du poteau (mm).</summary>
        public double HeightMm { get; set; }

        /// <summary>Origine : centre de la section, au niveau bas du poteau (unites Revit).</summary>
        public XYZ BasePoint { get; set; }

        /// <summary>Axe local X horizontal de la section (norme 1).</summary>
        public XYZ AxisX { get; set; }

        /// <summary>Axe local Y horizontal de la section (norme 1).</summary>
        public XYZ AxisY { get; set; }

        /// <summary>Axe du poteau, vertical ascendant (norme 1).</summary>
        public XYZ AxisZ { get; set; }

        /// <summary>Messages d'information ou d'alerte issus de la lecture de la geometrie.</summary>
        public List<string> Remarks { get; private set; }

        public ColumnGeometry()
        {
            Remarks = new List<string>();
        }

        /// <summary>Plus petite dimension de la section (mm) : b_min au sens de l'EC2.</summary>
        public double MinDimensionMm
        {
            get
            {
                if (Kind == SectionKind.Circular) return DiameterMm;
                return WidthMm < DepthMm ? WidthMm : DepthMm;
            }
        }

        /// <summary>Plus grande dimension de la section (mm).</summary>
        public double MaxDimensionMm
        {
            get
            {
                if (Kind == SectionKind.Circular) return DiameterMm;
                return WidthMm > DepthMm ? WidthMm : DepthMm;
            }
        }

        /// <summary>Aire brute de beton (mm2).</summary>
        public double GrossAreaMm2
        {
            get
            {
                if (Kind == SectionKind.Circular)
                {
                    return System.Math.PI * DiameterMm * DiameterMm / 4.0;
                }
                return WidthMm * DepthMm;
            }
        }

        public string SectionLabel
        {
            get
            {
                if (Kind == SectionKind.Circular)
                {
                    return string.Format("D{0:0}", DiameterMm);
                }
                return string.Format("{0:0} x {1:0}", WidthMm, DepthMm);
            }
        }

        /// <summary>Convertit un point exprime dans le repere local (mm) en point Revit.</summary>
        public XYZ ToWorld(double xMm, double yMm, double zMm)
        {
            return BasePoint
                   + AxisX.Multiply(LengthUnits.MmToFeet(xMm))
                   + AxisY.Multiply(LengthUnits.MmToFeet(yMm))
                   + AxisZ.Multiply(LengthUnits.MmToFeet(zMm));
        }
    }
}
