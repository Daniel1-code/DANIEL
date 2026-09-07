using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Loads;

namespace DanCI.Structural.Eurocodes.EC0
{
    /// <summary>
    /// Categorie d'usage de l'EN 1991-1-1 tableau 6.1, dont dependent les coefficients psi
    /// de l'EN 1990 tableau A1.1.
    /// </summary>
    public enum UseCategory
    {
        /// <summary>A : habitation, zones residentielles.</summary>
        Residential,
        /// <summary>B : bureaux.</summary>
        Office,
        /// <summary>C : lieux de reunion.</summary>
        Congregation,
        /// <summary>D : commerces.</summary>
        Shopping,
        /// <summary>E : stockage.</summary>
        Storage,
        /// <summary>H : toitures accessibles pour entretien seulement.</summary>
        Roof,
        /// <summary>Neige, altitude inferieure a 1000 m.</summary>
        Snow,
        /// <summary>Vent.</summary>
        Wind
    }

    /// <summary>Coefficients psi_0, psi_1 et psi_2 d'une action variable.</summary>
    public sealed class PsiFactors
    {
        public double Psi0 { get; set; }
        public double Psi1 { get; set; }
        public double Psi2 { get; set; }
    }

    /// <summary>
    /// Combinaisons d'actions de l'EN 1990. Les coefficients sont les valeurs recommandees
    /// du tableau A1.1 ; une Annexe Nationale peut les modifier, et c'est alors elle qui
    /// fait foi.
    ///
    /// Les valeurs sont regroupees ici, jamais dispersees dans les modules de calcul.
    /// </summary>
    public static class ActionCombinations
    {
        /// <summary>Coefficient partiel des actions permanentes defavorables, eq. 6.10.</summary>
        public const double GammaGSup = 1.35;

        /// <summary>Coefficient partiel des actions permanentes favorables.</summary>
        public const double GammaGInf = 1.00;

        /// <summary>Coefficient partiel des actions variables defavorables.</summary>
        public const double GammaQ = 1.50;

        /// <summary>EN 1990 tableau A1.1, valeurs recommandees.</summary>
        public static PsiFactors Psi(UseCategory category)
        {
            switch (category)
            {
                case UseCategory.Residential:
                case UseCategory.Office:
                    return new PsiFactors { Psi0 = 0.7, Psi1 = 0.5, Psi2 = 0.3 };
                case UseCategory.Congregation:
                case UseCategory.Shopping:
                    return new PsiFactors { Psi0 = 0.7, Psi1 = 0.7, Psi2 = 0.6 };
                case UseCategory.Storage:
                    return new PsiFactors { Psi0 = 1.0, Psi1 = 0.9, Psi2 = 0.8 };
                case UseCategory.Roof:
                    return new PsiFactors { Psi0 = 0.0, Psi1 = 0.0, Psi2 = 0.0 };
                case UseCategory.Snow:
                    return new PsiFactors { Psi0 = 0.5, Psi1 = 0.2, Psi2 = 0.0 };
                case UseCategory.Wind:
                    return new PsiFactors { Psi0 = 0.6, Psi1 = 0.2, Psi2 = 0.0 };
                default:
                    return new PsiFactors { Psi0 = 0.7, Psi1 = 0.5, Psi2 = 0.3 };
            }
        }

        /// <summary>
        /// Combinaison fondamentale ELU, eq. 6.10 : 1,35 G + 1,50 Q.
        /// Charges surfaciques ou lineiques, l'unite de sortie est celle de l'entree.
        /// </summary>
        public static double Ultimate(double permanent, double variable)
        {
            return GammaGSup * permanent + GammaQ * variable;
        }

        /// <summary>Combinaison caracteristique ELS, eq. 6.14b : G + Q.</summary>
        public static double Characteristic(double permanent, double variable)
        {
            return permanent + variable;
        }

        /// <summary>Combinaison quasi-permanente ELS, eq. 6.16b : G + psi_2 Q.</summary>
        public static double QuasiPermanent(double permanent, double variable,
                                            UseCategory category)
        {
            return permanent + Psi(category).Psi2 * variable;
        }

        /// <summary>
        /// Description litterale de la combinaison, pour la note de calcul. Une valeur
        /// numerique sans son origine n'est pas une justification.
        /// </summary>
        public static string Describe(double permanent, double variable, UseCategory category)
        {
            PsiFactors psi = Psi(category);
            return string.Format(
                "EN 1990 : ELU eq. 6.10 = {0:0.00} x {1:0.00} + {2:0.00} x {3:0.00} = {4:0.00} ; " +
                "ELS caracteristique = {5:0.00} ; ELS quasi-permanente (psi_2 = {6:0.0}) = {7:0.00}",
                GammaGSup, permanent, GammaQ, variable, Ultimate(permanent, variable),
                Characteristic(permanent, variable), psi.Psi2,
                QuasiPermanent(permanent, variable, category));
        }

        /// <summary>
        /// Combinaisons a partir d'efforts deja calcules par bande, pour alimenter le
        /// pipeline. Une seule station : une bande de dalle est traitee par section.
        /// </summary>
        public static List<LoadCombination> ForStrip(InternalForces ultimate,
                                                     InternalForces characteristic,
                                                     InternalForces quasiPermanent)
        {
            return new List<LoadCombination>
            {
                LoadCombination.Single("ULS-6.10", DesignSituation.UltimateFundamental, ultimate),
                LoadCombination.Single("SLS-CAR", DesignSituation.ServiceabilityCharacteristic,
                                       characteristic),
                LoadCombination.Single("SLS-QP", DesignSituation.ServiceabilityQuasiPermanent,
                                       quasiPermanent)
            };
        }
    }
}
