using System;

namespace DanCI.Structural.Eurocodes.Detailing
{
    /// <summary>
    /// Dispositions constructives des voiles selon l'EN 1992-1-1 article 9.6, valeurs
    /// recommandees. Une Annexe Nationale peut modifier chacune d'elles.
    /// </summary>
    public sealed class Ec2WallDetailing : IWallDetailingCode
    {
        public string Name { get { return "EN 1992-1-1:2004 art. 9.6"; } }
        public string ShortName { get { return "EC2 9.6"; } }

        /// <summary>9.6.2(1) : A_s,vmin = 0,002 A_c.</summary>
        public double MinVerticalSteel(double grossAreaMm2, out string justification)
        {
            double area = 0.002 * grossAreaMm2;
            justification = string.Format(
                "EC2 9.6.2(1) : As,vmin = 0,002 Ac = 0,002 x {0:0} = {1:0} mm2",
                grossAreaMm2, area);
            return area;
        }

        /// <summary>9.6.2(1) : A_s,vmax = 0,04 A_c, double aux recouvrements.</summary>
        public double MaxVerticalSteel(double grossAreaMm2, bool atLaps)
        {
            return (atLaps ? 0.08 : 0.04) * grossAreaMm2;
        }

        /// <summary>9.6.2(3) : s &lt;= min(3 t ; 400 mm).</summary>
        public double MaxVerticalSpacingMm(double thicknessMm, out string justification)
        {
            double byThickness = 3.0 * thicknessMm;
            double spacing = Math.Min(byThickness, 400.0);
            justification = string.Format(
                "EC2 9.6.2(3) : s <= min(3t ; 400) = min({0:0} ; 400) = {1:0} mm",
                byThickness, spacing);
            return spacing;
        }

        /// <summary>
        /// 9.6.3(1) : A_s,hmin = max(0,25 A_s,v ; 0,001 A_c).
        ///
        /// Le premier terme est le vrai : l'acier horizontal d'un voile n'est pas un
        /// minimum absolu, il est proportionnel a ce qui est reellement pose en vertical.
        /// Plus le voile est arme verticalement, plus il lui faut d'acier horizontal.
        /// </summary>
        public double MinHorizontalSteel(double grossAreaMm2, double verticalSteelMm2,
                                         out string justification)
        {
            double byVertical = 0.25 * verticalSteelMm2;
            double byGross = 0.001 * grossAreaMm2;
            double area = Math.Max(byVertical, byGross);
            justification = string.Format(
                "EC2 9.6.3(1) : As,hmin = max(0,25 As,v ; 0,001 Ac) = max({0:0} ; {1:0}) = " +
                "{2:0} mm2{3}",
                byVertical, byGross, area,
                byVertical >= byGross ? " (proportionnel aux aciers verticaux poses)" : "");
            return area;
        }

        /// <summary>9.6.3(2) : espacement maximal de 400 mm.</summary>
        public double MaxHorizontalSpacingMm { get { return 400.0; } }

        /// <summary>
        /// 9.6.4(1) : des armatures transversales sont exigees des que
        /// A_s,v &gt; 0,02 A_c, pour tenir les barres verticales contre le flambement.
        /// </summary>
        public bool RequiresTransverseLinks(double grossAreaMm2, double verticalSteelMm2)
        {
            return verticalSteelMm2 > 0.02 * grossAreaMm2;
        }

        /// <summary>Pratique courante : quatre epingles au metre carre.</summary>
        public double LinksPerSquareMetre { get { return 4.0; } }
    }
}
