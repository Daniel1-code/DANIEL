using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>
    /// Donnees geometriques d'une semelle isolee rectangulaire et du poteau qu'elle porte,
    /// independantes de Revit. Dimensions en millimetres.
    /// </summary>
    public sealed class FootingData
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Dimension de la semelle suivant X (B).</summary>
        public double WidthXMm { get; set; }

        /// <summary>Dimension de la semelle suivant Y (L).</summary>
        public double WidthYMm { get; set; }

        /// <summary>Epaisseur de la semelle (h).</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Dimension du poteau porte suivant X (c1).</summary>
        public double ColumnWidthXMm { get; set; }

        /// <summary>Dimension du poteau porte suivant Y (c2).</summary>
        public double ColumnWidthYMm { get; set; }

        public List<string> Remarks { get; private set; }

        public FootingData()
        {
            Remarks = new List<string>();
            Name = "Semelle";
            Mark = "FT";
            ColumnWidthXMm = 400.0;
            ColumnWidthYMm = 400.0;
        }

        /// <summary>Aire de la semelle (mm2).</summary>
        public double AreaMm2
        {
            get { return WidthXMm * WidthYMm; }
        }

        /// <summary>Volume de beton (mm3).</summary>
        public double VolumeMm3
        {
            get { return AreaMm2 * ThicknessMm; }
        }

        /// <summary>Debord depuis le nu du poteau suivant X (mm).</summary>
        public double OverhangXMm
        {
            get { return (WidthXMm - ColumnWidthXMm) / 2.0; }
        }

        /// <summary>Debord depuis le nu du poteau suivant Y (mm).</summary>
        public double OverhangYMm
        {
            get { return (WidthYMm - ColumnWidthYMm) / 2.0; }
        }

        public double MinOverhangMm
        {
            get { return Math.Min(OverhangXMm, OverhangYMm); }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format("{0:0} x {1:0} x {2:0}", WidthXMm, WidthYMm, ThicknessMm);
            }
        }

        /// <summary>Poids propre de la semelle (N), pour un beton de 25 kN/m3.</summary>
        public double SelfWeightN(double concreteUnitWeightKnM3 = 25.0)
        {
            return VolumeMm3 * 1e-9 * concreteUnitWeightKnM3 * 1000.0;
        }
    }
}
