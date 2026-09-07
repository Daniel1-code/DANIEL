using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>Position d'une barre longitudinale dans la section (mm).</summary>
    public readonly struct BarPosition
    {
        public double XMm { get; }
        public double YMm { get; }
        public double DiameterMm { get; }

        public BarPosition(double xMm, double yMm, double diameterMm)
        {
            XMm = xMm;
            YMm = yMm;
            DiameterMm = diameterMm;
        }

        public double AreaMm2 { get { return UnitConverter.BarArea(DiameterMm); } }
    }

    /// <summary>
    /// Source unique des cotes du ferraillage d'un poteau. Le plan de ferraillage, l'apercu
    /// graphique, le quantitatif et la verification de resistance partent tous d'ici : ce qui
    /// est dessine, chiffre, verifie et modelise est ainsi necessairement identique.
    /// </summary>
    public static class ColumnLayoutGeometry
    {
        /// <summary>Demi-distance entre axes des barres d'angle suivant X.</summary>
        public static double BarHalfSpanX(ColumnData column, ColumnReinforcement r)
        {
            return column.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm - r.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-distance entre axes des barres d'angle suivant Y.</summary>
        public static double BarHalfSpanY(ColumnData column, ColumnReinforcement r)
        {
            return column.DepthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm - r.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant X.</summary>
        public static double StirrupHalfX(ColumnData column, ColumnReinforcement r)
        {
            return column.WidthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant Y.</summary>
        public static double StirrupHalfY(ColumnData column, ColumnReinforcement r)
        {
            return column.DepthMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
        }

        /// <summary>Rayon de l'axe de la cerce (section circulaire).</summary>
        public static double StirrupRadius(ColumnData column, ColumnReinforcement r)
        {
            return column.DiameterMm / 2.0 - r.CoverMm - r.StirrupDiameterMm / 2.0;
        }

        /// <summary>Rayon du lit de barres longitudinales (section circulaire).</summary>
        public static double BarRadius(ColumnData column, ColumnReinforcement r)
        {
            return column.DiameterMm / 2.0 - r.CoverMm - r.StirrupDiameterMm - r.BarDiameterMm / 2.0;
        }

        /// <summary>Entraxe des barres le long d'une face parallele a X.</summary>
        public static double PitchX(ColumnData column, ColumnReinforcement r)
        {
            return r.BarsAlongX > 1 ? 2.0 * BarHalfSpanX(column, r) / (r.BarsAlongX - 1) : 0.0;
        }

        /// <summary>Entraxe des barres le long d'une face parallele a Y.</summary>
        public static double PitchY(ColumnData column, ColumnReinforcement r)
        {
            return r.BarsAlongY > 1 ? 2.0 * BarHalfSpanY(column, r) / (r.BarsAlongY - 1) : 0.0;
        }

        /// <summary>
        /// Toutes les barres longitudinales, dans l'ordre de pose : lit inferieur, lit
        /// superieur, puis les barres intermediaires des faces laterales.
        /// </summary>
        public static List<BarPosition> Bars(ColumnData column, ColumnReinforcement r)
        {
            var bars = new List<BarPosition>();
            if (r.BarDiameterMm <= 0) return bars;

            if (column.Shape == SectionShape.Circular)
            {
                double radius = BarRadius(column, r);
                for (int i = 0; i < r.TotalBars; i++)
                {
                    double angle = 2.0 * Math.PI * i / Math.Max(r.TotalBars, 1);
                    bars.Add(new BarPosition(radius * Math.Cos(angle), radius * Math.Sin(angle),
                                             r.BarDiameterMm));
                }
                return bars;
            }

            double x0 = BarHalfSpanX(column, r);
            double y0 = BarHalfSpanY(column, r);
            double pitchX = PitchX(column, r);
            double pitchY = PitchY(column, r);

            for (int i = 0; i < r.BarsAlongX; i++)
            {
                bars.Add(new BarPosition(-x0 + i * pitchX, -y0, r.BarDiameterMm));
            }
            for (int i = 0; i < r.BarsAlongX; i++)
            {
                bars.Add(new BarPosition(-x0 + i * pitchX, y0, r.BarDiameterMm));
            }
            for (int i = 1; i <= r.BarsAlongY - 2; i++)
            {
                bars.Add(new BarPosition(-x0, -y0 + i * pitchY, r.BarDiameterMm));
                bars.Add(new BarPosition(x0, -y0 + i * pitchY, r.BarDiameterMm));
            }
            return bars;
        }

        /// <summary>Abscisses des epingles orientees suivant Y (section rectangulaire).</summary>
        public static List<double> CrossTieXPositions(ColumnData column, ColumnReinforcement r)
        {
            var positions = new List<double>();
            if (r.CrossTiesAlongY <= 0 || r.BarsAlongX <= 2) return positions;
            double x0 = BarHalfSpanX(column, r);
            double pitch = PitchX(column, r);
            for (int i = 1; i <= r.BarsAlongX - 2; i++) positions.Add(-x0 + i * pitch);
            return positions;
        }

        /// <summary>Ordonnees des epingles orientees suivant X (section rectangulaire).</summary>
        public static List<double> CrossTieYPositions(ColumnData column, ColumnReinforcement r)
        {
            var positions = new List<double>();
            if (r.CrossTiesAlongX <= 0 || r.BarsAlongY <= 2) return positions;
            double y0 = BarHalfSpanY(column, r);
            double pitch = PitchY(column, r);
            for (int i = 1; i <= r.BarsAlongY - 2; i++) positions.Add(-y0 + i * pitch);
            return positions;
        }
    }
}
