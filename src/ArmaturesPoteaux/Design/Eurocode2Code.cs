using System;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// EN 1992-1-1 (Eurocode 2), article 9.5 "Poteaux" et article 8.7 pour les recouvrements.
    /// Les dispositions sismiques optionnelles suivent l'EN 1998-1 (DCM), article 5.4.3.2.2.
    /// </summary>
    public class Eurocode2Code : IDesignCode
    {
        /// <summary>Coefficient partiel de l'acier gamma_s.</summary>
        public double GammaS { get; set; }

        /// <summary>Coefficient partiel du beton gamma_c.</summary>
        public double GammaC { get; set; }

        public Eurocode2Code()
        {
            GammaS = 1.15;
            GammaC = 1.5;
        }

        public string Name { get { return "Eurocode 2 - EN 1992-1-1 (9.5)"; } }

        public string ShortName { get { return "EC2"; } }

        public double MinSteelArea(double grossAreaMm2, double axialLoadN, double fykMPa, out string justification)
        {
            double fyd = fykMPa / GammaS;
            double byLoad = 0.10 * axialLoadN / fyd;   // 0,10 N_Ed / f_yd
            double bySection = 0.002 * grossAreaMm2;   // 0,002 A_c
            double result = Math.Max(byLoad, bySection);

            if (axialLoadN > 0)
            {
                justification = string.Format(
                    "EC2 9.5.2(2) : As,min = max(0,10 NEd/fyd ; 0,002 Ac) = max({0:0} ; {1:0}) = {2:0} mm2 (fyd = {3:0} MPa)",
                    byLoad, bySection, result, fyd);
            }
            else
            {
                justification = string.Format(
                    "EC2 9.5.2(2) : As,min = 0,002 Ac = {0:0} mm2 (NEd non renseigne, le terme 0,10 NEd/fyd est ignore)",
                    bySection);
            }
            return result;
        }

        public double MaxSteelArea(double grossAreaMm2)
        {
            return 0.04 * grossAreaMm2; // EC2 9.5.2(3) : 4 % hors zone de recouvrement
        }

        public double MinLongitudinalDiameter { get { return 8.0; } } // EC2 9.5.2(1)

        public int MinBarCount(SectionKind kind)
        {
            return kind == SectionKind.Circular ? 6 : 4; // EC2 9.5.2(4)
        }

        public double MinTransverseDiameter(double longitudinalDiameterMm)
        {
            return Math.Max(6.0, longitudinalDiameterMm / 4.0); // EC2 9.5.3(1)
        }

        public double MaxStirrupSpacing(double longitudinalDiameterMm, double transverseDiameterMm,
                                        double minSectionDimensionMm, out string justification)
        {
            double byBar = 20.0 * longitudinalDiameterMm;
            double result = Math.Min(byBar, Math.Min(minSectionDimensionMm, 400.0));
            justification = string.Format(
                "EC2 9.5.3(3) : scl,tmax = min(20 phi_l ; b_min ; 400) = min({0:0} ; {1:0} ; 400) = {2:0} mm",
                byBar, minSectionDimensionMm, result);
            return result;
        }

        public double CriticalZoneSpacingFactor { get { return 0.6; } } // EC2 9.5.3(4)

        public double CriticalZoneLength(double maxSectionDimensionMm, double clearHeightMm, bool seismic)
        {
            if (seismic)
            {
                // EN 1998-1 5.4.3.2.2(4) : lcr = max(hc ; lcl/6 ; 450 mm)
                return Math.Max(maxSectionDimensionMm, Math.Max(clearHeightMm / 6.0, 450.0));
            }
            // EC2 9.5.3(4) : sur une hauteur egale a la plus grande dimension du poteau
            return maxSectionDimensionMm;
        }

        public double MinClearBarSpacing(double longitudinalDiameterMm, double aggregateSizeMm)
        {
            // EC2 8.2(2) : max(k1 phi ; dg + k2 ; 20 mm) avec k1 = 1 et k2 = 5 mm
            return Math.Max(longitudinalDiameterMm, Math.Max(aggregateSizeMm + 5.0, 20.0));
        }

        public double MaxDistanceToRestrainedBar { get { return 150.0; } } // EC2 9.5.3(6)

        public double LapLength(double barDiameterMm, double concreteStrengthMPa, double steelStrengthMPa,
                                out string justification)
        {
            // EC2 8.4.2 : fbd = 2,25 eta1 eta2 fctd, barres HA en bonnes conditions d'adherence,
            // phi <= 32 mm donc eta1 = eta2 = 1.
            double fctm = concreteStrengthMPa <= 50.0
                ? 0.30 * Math.Pow(concreteStrengthMPa, 2.0 / 3.0)
                : 2.12 * Math.Log(1.0 + (concreteStrengthMPa + 8.0) / 10.0);
            double fctk005 = 0.7 * fctm;
            double fctd = fctk005 / GammaC;
            double eta2 = barDiameterMm <= 32.0 ? 1.0 : (132.0 - barDiameterMm) / 100.0;
            double fbd = 2.25 * 1.0 * eta2 * fctd;

            double sigmaSd = steelStrengthMPa / GammaS;         // barre supposee pleinement sollicitee
            double lbRqd = (barDiameterMm / 4.0) * (sigmaSd / fbd);   // EC2 8.4.3

            // EC2 8.7.3 : alpha6 = 1,5 quand plus de 50 % des barres sont recouvertes au meme endroit,
            // ce qui est le cas usuel en pied de poteau.
            const double alpha6 = 1.5;
            double l0 = alpha6 * lbRqd;
            double l0Min = Math.Max(0.3 * alpha6 * lbRqd, Math.Max(15.0 * barDiameterMm, 200.0));
            double result = Math.Max(l0, l0Min);

            justification = string.Format(
                "EC2 8.4/8.7 : fctm = {0:0.00} MPa, fbd = {1:0.00} MPa, lb,rqd = {2:0} mm, " +
                "l0 = alpha6 lb,rqd = 1,5 x {2:0} = {3:0} mm (arrondi a {4:0} mm, soit {5:0} phi)",
                fctm, fbd, lbRqd, l0, result, result / barDiameterMm);
            return result;
        }
    }
}
