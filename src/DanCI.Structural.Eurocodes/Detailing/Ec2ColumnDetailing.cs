using System;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Eurocodes.Detailing
{
    /// <summary>
    /// Dispositions constructives des poteaux selon l'EN 1992-1-1:2004, article 9.5, et
    /// espacements libres selon l'article 8.2. Les zones critiques sismiques suivent
    /// l'EN 1998-1 article 5.4.3.2.2 lorsque l'option est activee.
    /// Toutes les valeurs modifiables par une Annexe Nationale proviennent de
    /// <see cref="INationalAnnex"/>.
    /// </summary>
    public sealed class Ec2ColumnDetailing : IColumnDetailingCode
    {
        private readonly INationalAnnex _annex;

        public Ec2ColumnDetailing(INationalAnnex annex)
        {
            _annex = annex;
        }

        public string Name
        {
            get { return "EN 1992-1-1:2004 art. 9.5 - " + _annex.Name; }
        }

        public string ShortName { get { return "EC2"; } }

        public double MinSteelArea(double grossAreaMm2, double axialForceN, double steelStrengthMPa,
                                   out string justification)
        {
            double fyd = steelStrengthMPa / _annex.GammaS;
            double byLoad = _annex.ColumnMinSteelLoadFactor * axialForceN / fyd;
            double bySection = _annex.ColumnMinSteelAreaRatio * grossAreaMm2;
            double result = Math.Max(byLoad, bySection);

            if (axialForceN > 0)
            {
                justification = string.Format(
                    "EC2 9.5.2(2) : As,min = max({0:0.00} NEd/fyd ; {1:0.000} Ac) = " +
                    "max({2:0} ; {3:0}) = {4:0} mm2 (fyd = {5:0} MPa)",
                    _annex.ColumnMinSteelLoadFactor, _annex.ColumnMinSteelAreaRatio,
                    byLoad, bySection, result, fyd);
            }
            else
            {
                justification = string.Format(
                    "EC2 9.5.2(2) : As,min = {0:0.000} Ac = {1:0} mm2 " +
                    "(NEd non renseigne, le terme lie a l'effort normal est ignore)",
                    _annex.ColumnMinSteelAreaRatio, bySection);
            }
            return result;
        }

        public double MaxSteelArea(double grossAreaMm2)
        {
            return _annex.ColumnMaxSteelAreaRatio * grossAreaMm2;   // 9.5.2(3)
        }

        public double MinLongitudinalDiameterMm
        {
            get { return _annex.ColumnMinLongitudinalDiameterMm; }  // 9.5.2(1)
        }

        public int MinBarCount(SectionShape shape)
        {
            return shape == SectionShape.Circular ? 6 : 4;          // 9.5.2(4)
        }

        public double MinTransverseDiameterMm(double longitudinalDiameterMm)
        {
            return Math.Max(_annex.MinTransverseDiameterMm, longitudinalDiameterMm / 4.0); // 9.5.3(1)
        }

        public double MaxStirrupSpacingMm(double longitudinalDiameterMm, double transverseDiameterMm,
                                          double minSectionDimensionMm, out string justification)
        {
            double byBar = _annex.TieSpacingBarDiameterFactor * longitudinalDiameterMm;
            double result = Math.Min(byBar, Math.Min(minSectionDimensionMm, _annex.TieSpacingMaximumMm));
            justification = string.Format(
                "EC2 9.5.3(3) : scl,tmax = min({0:0} phi_l ; b_min ; {1:0}) = min({2:0} ; {3:0} ; {1:0}) = {4:0} mm",
                _annex.TieSpacingBarDiameterFactor, _annex.TieSpacingMaximumMm,
                byBar, minSectionDimensionMm, result);
            return result;
        }

        public double CriticalZoneSpacingFactor
        {
            get { return _annex.CriticalZoneSpacingFactor; }        // 9.5.3(4)
        }

        public double CriticalZoneLengthMm(double maxSectionDimensionMm, double clearHeightMm, bool seismic)
        {
            if (seismic)
            {
                // EN 1998-1 5.4.3.2.2(4) : lcr = max(hc ; lcl/6 ; 450 mm)
                return Math.Max(maxSectionDimensionMm, Math.Max(clearHeightMm / 6.0, 450.0));
            }
            // EC2 9.5.3(4) : sur une hauteur egale a la plus grande dimension du poteau
            return maxSectionDimensionMm;
        }

        public double MinClearBarSpacingMm(double longitudinalDiameterMm, double aggregateSizeMm)
        {
            // EC2 8.2(2) : max(k1 phi ; dg + k2 ; 20 mm)
            return Math.Max(_annex.ClearSpacingBarFactor * longitudinalDiameterMm,
                   Math.Max(aggregateSizeMm + _annex.ClearSpacingAggregateAdditionMm,
                            _annex.ClearSpacingMinimumMm));
        }

        public double MaxDistanceToRestrainedBarMm
        {
            get { return _annex.MaxDistanceToRestrainedBarMm; }     // 9.5.3(6)
        }
    }
}
