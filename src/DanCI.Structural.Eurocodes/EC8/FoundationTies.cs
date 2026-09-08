using System;

namespace DanCI.Structural.Eurocodes.EC8
{
    /// <summary>Classe de sol au sens de l'EN 1998-1 tableau 3.1.</summary>
    public enum GroundType
    {
        /// <summary>A : rocher ou formation rocheuse.</summary>
        A,
        /// <summary>B : depots tres denses de sable ou gravier, argile tres raide.</summary>
        B,
        /// <summary>C : depots profonds de sable dense a moyennement dense.</summary>
        C,
        /// <summary>D : depots de sol sans cohesion lache a moyennement dense.</summary>
        D,
        /// <summary>E : couche superficielle d'alluvions sur un substratum plus raide.</summary>
        E
    }

    /// <summary>Effort de liaison exige d'une longrine et sa justification.</summary>
    public sealed class TieForceResult
    {
        /// <summary>Coefficient epsilon du tableau de l'EN 1998-5 5.4.1.2(7).</summary>
        public double Epsilon { get; set; }

        /// <summary>Effort axial de liaison, en traction comme en compression (N).</summary>
        public double AxialForceN { get; set; }

        /// <summary>Une longrine de liaison est-elle exigee ?</summary>
        public bool TieRequired { get; set; }

        public string Justification { get; set; }
    }

    /// <summary>
    /// Longrines de liaison entre fondations, EN 1998-5 article 5.4.1.2 et EN 1998-1
    /// article 5.8.
    ///
    /// Une longrine n'est pas une poutre ordinaire posee bas : c'est d'abord un **tirant**.
    /// Elle relie les semelles pour qu'elles se deplacent ensemble sous seisme, et l'effort
    /// qu'elle reprend est axial, alterne, proportionnel a la charge des poteaux qu'elle
    /// relie. Le dimensionner en flexion seule revient a oublier sa raison d'etre.
    ///
    /// Hors zone sismique, **aucun article de l'EN 1992 n'impose cet effort** : le moteur
    /// le dit au lieu d'appliquer silencieusement une regle sismique a un projet qui n'en
    /// releve pas.
    /// </summary>
    public static class FoundationTies
    {
        /// <summary>
        /// Coefficient epsilon, EN 1998-5 5.4.1.2(7). Le sol de classe A ne demande pas de
        /// liaison ; les classes B, C et D sont tabulees. La classe E, stratifiee, est
        /// traitee comme la plus defavorable des classes tabulees.
        /// </summary>
        public static double Epsilon(GroundType ground)
        {
            switch (ground)
            {
                case GroundType.A: return 0.0;
                case GroundType.B: return 0.3;
                case GroundType.C: return 0.4;
                case GroundType.D: return 0.6;
                default: return 0.6;   // E : traitee comme D, securitaire
            }
        }

        /// <summary>
        /// Effort de liaison N = +- epsilon alpha S N_Ed, EN 1998-5 5.4.1.2(7).
        /// </summary>
        /// <param name="meanColumnAxialForceN">
        /// N_Ed, moyenne des efforts normaux de calcul des elements verticaux relies,
        /// en situation sismique.
        /// </param>
        /// <param name="groundAccelerationRatio">alpha = a_g / g.</param>
        /// <param name="soilFactor">S, coefficient de sol du tableau 3.2 ou 3.3.</param>
        /// <param name="ground">Classe de sol.</param>
        public static TieForceResult Compute(double meanColumnAxialForceN,
                                             double groundAccelerationRatio, double soilFactor,
                                             GroundType ground)
        {
            double epsilon = Epsilon(ground);
            var result = new TieForceResult
            {
                Epsilon = epsilon,
                TieRequired = epsilon > 0,
                AxialForceN = epsilon * groundAccelerationRatio * soilFactor
                              * Math.Max(meanColumnAxialForceN, 0.0)
            };

            result.Justification = epsilon > 0
                ? string.Format(
                    "EN 1998-5 5.4.1.2(7) : sol de classe {0} -> epsilon = {1:0.0} ; " +
                    "N_liaison = +- {1:0.0} x {2:0.00} x {3:0.00} x {4:0.0} = +- {5:0.0} kN, " +
                    "alterne en traction et en compression",
                    ground, epsilon, groundAccelerationRatio, soilFactor,
                    meanColumnAxialForceN / 1000.0, result.AxialForceN / 1000.0)
                : string.Format(
                    "EN 1998-5 5.4.1.2(2) : sol de classe {0}, aucune longrine de liaison " +
                    "n'est exigee entre les fondations.", ground);
            return result;
        }

        /// <summary>
        /// Largeur minimale d'une longrine, EN 1998-1 5.8.1(4) : b_w &gt;= 0,25 m.
        /// </summary>
        public const double MinimumWidthMm = 250.0;

        /// <summary>
        /// Hauteur minimale d'une longrine, EN 1998-1 5.8.1(4) : 0,40 m jusqu'a trois
        /// niveaux, 0,50 m a partir de quatre.
        /// </summary>
        public static double MinimumHeightMm(int storeyCount)
        {
            return storeyCount >= 4 ? 500.0 : 400.0;
        }

        /// <summary>
        /// Pourcentage minimal d'armature longitudinale, EN 1998-1 5.8.2(5) :
        /// rho &gt;= 0,4 % en nappe superieure ET en nappe inferieure.
        /// </summary>
        public const double MinimumSteelRatio = 0.004;

        /// <summary>
        /// Section minimale d'armature longitudinale d'une nappe (mm2), art. 5.8.2(5).
        /// </summary>
        public static double MinimumSteelPerFace(double grossAreaMm2, out string justification)
        {
            double area = MinimumSteelRatio * grossAreaMm2;
            justification = string.Format(
                "EN 1998-1 5.8.2(5) : rho_min = 0,4 % de A_c = 0,004 x {0:0} = {1:0} mm2, " +
                "exiges EN HAUT ET EN BAS",
                grossAreaMm2, area);
            return area;
        }

        /// <summary>
        /// Section d'acier supplementaire reprenant une traction axiale, repartie a parts
        /// egales entre les deux nappes : A_s,N = N_Ed / f_yd.
        ///
        /// C'est la methode manuelle usuelle pour une traction faible combinee a de la
        /// flexion. Elle est securitaire et evite une analyse en flexion composee tendue :
        /// la fiche de validation le dit.
        /// </summary>
        public static double TensionSteel(double tensionForceN, double fydMPa)
        {
            if (tensionForceN <= 0 || fydMPa <= 0) return 0.0;
            return tensionForceN / fydMPa;
        }

        /// <summary>
        /// Resistance en compression centree d'une longrine, article 6.1 de l'EN 1992-1-1.
        /// Une longrine enterree est maintenue lateralement par le sol sur toute sa
        /// longueur : le flambement ne la concerne pas, et c'est pourquoi aucune
        /// verification d'elancement n'est produite.
        /// </summary>
        public static double CompressionResistance(double grossAreaMm2, double steelAreaMm2,
                                                   double fcdMPa, double fydMPa)
        {
            return grossAreaMm2 * fcdMPa + steelAreaMm2 * fydMPa;
        }
    }
}
