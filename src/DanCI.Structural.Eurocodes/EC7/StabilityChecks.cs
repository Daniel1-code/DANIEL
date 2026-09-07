using System;

namespace DanCI.Structural.Eurocodes.EC7
{
    /// <summary>Resultat d'une verification de stabilite d'ensemble.</summary>
    public sealed class StabilityResult
    {
        /// <summary>Action deviatorique (N ou N.mm selon la verification).</summary>
        public double Destabilising { get; set; }
        /// <summary>Resistance ou action stabilisatrice.</summary>
        public double Stabilising { get; set; }

        public double Utilization
        {
            get { return Stabilising > 0 ? Destabilising / Stabilising : double.PositiveInfinity; }
        }

        public bool Passes { get { return Utilization <= 1.0; } }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Verifications de stabilite d'une fondation superficielle selon l'EN 1997-1 :
    /// glissement (art. 6.5.3) et renversement (etat limite EQU, art. 2.4.7.2).
    /// </summary>
    public static class StabilityChecks
    {
        /// <summary>
        /// Glissement a la base, EN 1997-1 6.5.3(10) pour des conditions drainees :
        /// H_Ed &lt;= R_d = V'_Ed tan(delta_d) + A' c_a,d.
        /// </summary>
        /// <param name="horizontalForceN">Resultante horizontale H_Ed (N).</param>
        /// <param name="verticalForceN">Charge verticale de calcul V'_Ed (N).</param>
        /// <param name="frictionAngleDeg">Angle de frottement a l'interface delta (degres).</param>
        /// <param name="adhesionKpa">Adherence a l'interface (kPa).</param>
        /// <param name="effectiveAreaMm2">Aire effective de contact A' (mm2).</param>
        /// <param name="partialFactor">Coefficient partiel applique a tan(delta).</param>
        public static StabilityResult Sliding(double horizontalForceN, double verticalForceN,
                                              double frictionAngleDeg, double adhesionKpa,
                                              double effectiveAreaMm2, double partialFactor = 1.25)
        {
            double tanDelta = Math.Tan(frictionAngleDeg * Math.PI / 180.0) / partialFactor;
            double friction = Math.Max(verticalForceN, 0.0) * tanDelta;
            double adhesion = adhesionKpa / 1000.0 * effectiveAreaMm2;   // kPa -> N/mm2
            double resistance = friction + adhesion;

            return new StabilityResult
            {
                Destabilising = Math.Abs(horizontalForceN),
                Stabilising = resistance,
                Justification = string.Format(
                    "EN 1997-1 6.5.3 : R_d = V' tan(delta_d) + A' c_a,d = {0:0.0} + {1:0.0} = " +
                    "{2:0.0} kN, avec tan(delta_d) = tan({3:0}) / {4:0.00} = {5:0.000}",
                    friction / 1000.0, adhesion / 1000.0, resistance / 1000.0,
                    frictionAngleDeg, partialFactor, tanDelta)
            };
        }

        /// <summary>
        /// Renversement autour de l'arete de la semelle, etat limite EQU.
        /// Le moment stabilisateur est celui de la charge verticale par rapport a l'arete.
        /// </summary>
        /// <param name="overturningMomentNmm">Moment deviateur a la base (N.mm).</param>
        /// <param name="verticalForceN">Charge verticale stabilisatrice (N).</param>
        /// <param name="widthMm">Dimension de la semelle dans la direction consideree (mm).</param>
        /// <param name="stabilisingFactor">Coefficient sur l'action stabilisatrice (0,9 en EQU).</param>
        public static StabilityResult Overturning(double overturningMomentNmm, double verticalForceN,
                                                  double widthMm, double stabilisingFactor = 0.9)
        {
            double stabilising = stabilisingFactor * Math.Max(verticalForceN, 0.0) * widthMm / 2.0;
            return new StabilityResult
            {
                Destabilising = Math.Abs(overturningMomentNmm),
                Stabilising = stabilising,
                Justification = string.Format(
                    "EN 1997-1 2.4.7.2 (EQU) : M_stb = {0:0.00} N B/2 = {1:0.0} kN.m, " +
                    "M_dst = {2:0.0} kN.m",
                    stabilisingFactor, stabilising / 1e6, Math.Abs(overturningMomentNmm) / 1e6)
            };
        }
    }
}
