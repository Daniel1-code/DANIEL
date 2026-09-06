using System;
using System.Collections.Generic;

namespace ArmaturesPoteaux.Core
{
    /// <summary>Position d'une barre longitudinale dans le repere local de la section (mm).</summary>
    public struct BarPoint
    {
        public double XMm;
        public double YMm;
        public double DiameterMm;

        public BarPoint(double xMm, double yMm, double diameterMm)
        {
            XMm = xMm;
            YMm = yMm;
            DiameterMm = diameterMm;
        }

        public double AreaMm2
        {
            get { return Math.PI * DiameterMm * DiameterMm / 4.0; }
        }
    }

    /// <summary>
    /// Source unique des cotes du ferraillage : le modeleur Revit, l'apercu graphique,
    /// le quantitatif et la verification de resistance partent tous d'ici, ce qui garantit
    /// que ce qui est dessine, chiffre et verifie est exactement ce qui est modelise.
    /// Origine au centre de la section, axes X et Y locaux, dimensions en millimetres.
    /// </summary>
    public static class RebarLayout
    {
        /// <summary>Demi-distance entre axes des barres d'angle suivant X.</summary>
        public static double BarHalfSpanX(ColumnGeometry g, DesignResult d)
        {
            return g.WidthMm / 2.0 - d.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-distance entre axes des barres d'angle suivant Y.</summary>
        public static double BarHalfSpanY(ColumnGeometry g, DesignResult d)
        {
            return g.DepthMm / 2.0 - d.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant X.</summary>
        public static double StirrupHalfX(ColumnGeometry g, DesignResult d)
        {
            return g.WidthMm / 2.0 - d.CoverMm - d.StirrupDiameterMm / 2.0;
        }

        /// <summary>Demi-largeur de l'axe du cadre suivant Y.</summary>
        public static double StirrupHalfY(ColumnGeometry g, DesignResult d)
        {
            return g.DepthMm / 2.0 - d.CoverMm - d.StirrupDiameterMm / 2.0;
        }

        /// <summary>Rayon de l'axe de la cerce (section circulaire).</summary>
        public static double StirrupRadius(ColumnGeometry g, DesignResult d)
        {
            return g.DiameterMm / 2.0 - d.CoverMm - d.StirrupDiameterMm / 2.0;
        }

        /// <summary>Rayon du lit de barres longitudinales (section circulaire).</summary>
        public static double BarRadius(ColumnGeometry g, DesignResult d)
        {
            return g.DiameterMm / 2.0 - d.CoverMm - d.StirrupDiameterMm - d.BarDiameterMm / 2.0;
        }

        /// <summary>Entraxe des barres le long d'une face parallele a X.</summary>
        public static double PitchX(ColumnGeometry g, DesignResult d)
        {
            return d.BarsAlongX > 1 ? 2.0 * BarHalfSpanX(g, d) / (d.BarsAlongX - 1) : 0.0;
        }

        /// <summary>Entraxe des barres le long d'une face parallele a Y.</summary>
        public static double PitchY(ColumnGeometry g, DesignResult d)
        {
            return d.BarsAlongY > 1 ? 2.0 * BarHalfSpanY(g, d) / (d.BarsAlongY - 1) : 0.0;
        }

        /// <summary>
        /// Toutes les barres longitudinales, dans l'ordre ou elles sont posees :
        /// lit inferieur, lit superieur, puis les barres intermediaires des faces laterales.
        /// </summary>
        public static List<BarPoint> Bars(ColumnGeometry g, DesignResult d)
        {
            var bars = new List<BarPoint>();
            if (d.BarDiameterMm <= 0) return bars;

            if (g.Kind == SectionKind.Circular)
            {
                double radius = BarRadius(g, d);
                for (int i = 0; i < d.TotalBars; i++)
                {
                    double angle = 2.0 * Math.PI * i / Math.Max(d.TotalBars, 1);
                    bars.Add(new BarPoint(radius * Math.Cos(angle), radius * Math.Sin(angle),
                                          d.BarDiameterMm));
                }
                return bars;
            }

            double x0 = BarHalfSpanX(g, d);
            double y0 = BarHalfSpanY(g, d);
            double pitchX = PitchX(g, d);
            double pitchY = PitchY(g, d);

            for (int i = 0; i < d.BarsAlongX; i++)
            {
                bars.Add(new BarPoint(-x0 + i * pitchX, -y0, d.BarDiameterMm));
            }
            for (int i = 0; i < d.BarsAlongX; i++)
            {
                bars.Add(new BarPoint(-x0 + i * pitchX, y0, d.BarDiameterMm));
            }
            for (int i = 1; i <= d.BarsAlongY - 2; i++)
            {
                bars.Add(new BarPoint(-x0, -y0 + i * pitchY, d.BarDiameterMm));
                bars.Add(new BarPoint(x0, -y0 + i * pitchY, d.BarDiameterMm));
            }
            return bars;
        }

        /// <summary>Abscisses des epingles orientees suivant Y (section rectangulaire).</summary>
        public static List<double> CrossTieXPositions(ColumnGeometry g, DesignResult d)
        {
            var positions = new List<double>();
            if (d.CrossTiesAlongY <= 0 || d.BarsAlongX <= 2) return positions;
            double x0 = BarHalfSpanX(g, d);
            double pitch = PitchX(g, d);
            for (int i = 1; i <= d.BarsAlongX - 2; i++) positions.Add(-x0 + i * pitch);
            return positions;
        }

        /// <summary>Ordonnees des epingles orientees suivant X (section rectangulaire).</summary>
        public static List<double> CrossTieYPositions(ColumnGeometry g, DesignResult d)
        {
            var positions = new List<double>();
            if (d.CrossTiesAlongX <= 0 || d.BarsAlongY <= 2) return positions;
            double y0 = BarHalfSpanY(g, d);
            double pitch = PitchY(g, d);
            for (int i = 1; i <= d.BarsAlongY - 2; i++) positions.Add(-y0 + i * pitch);
            return positions;
        }
    }
}
