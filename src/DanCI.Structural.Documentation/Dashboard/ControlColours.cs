using System;
using System.Collections.Generic;

namespace DanCI.Structural.Documentation.Dashboard
{
    /// <summary>
    /// Bandes de lecture d'un projet ferraille, de la moins chargee a la plus chargee.
    /// L'ordre de l'enumeration est celui de la legende : il est signifiant.
    /// </summary>
    public enum ControlBand
    {
        /// <summary>Aucun resultat : l'element n'a pas ete dimensionne.</summary>
        NotDesigned,

        /// <summary>Taux <= 0,50. Passe largement, et c'est une information.</summary>
        LightlyUtilised,

        /// <summary>0,50 &lt; taux <= 0,85. Regime courant.</summary>
        Normal,

        /// <summary>0,85 &lt; taux <= 1,00. Passe, mais sans marge.</summary>
        Tight,

        /// <summary>Taux &gt; 1,00, ou une verification en defaut. Ne passe pas.</summary>
        Overloaded
    }

    /// <summary>Une couleur de controle, avec ce qu'elle veut dire.</summary>
    public sealed class ControlColour
    {
        public ControlBand Band { get; set; }
        public byte R { get; set; }
        public byte G { get; set; }
        public byte B { get; set; }

        /// <summary>Libelle de legende. Il doit suffire SANS la couleur.</summary>
        public string Label { get; set; }

        /// <summary>Ce que l'ingenieur doit en conclure.</summary>
        public string Meaning { get; set; }

        public string Hex
        {
            get { return string.Format("#{0:X2}{1:X2}{2:X2}", R, G, B); }
        }
    }

    /// <summary>
    /// COULEURS DE CONTROLE : de quoi lire un modele ferraille d'un coup d'oeil.
    ///
    /// UNE COULEUR N'EST PAS UNE VERIFICATION. Elle ne dit rien que la note de calcul ne
    /// dise deja ; elle dit seulement OU regarder. C'est utile sur deux cents elements,
    /// c'est trompeur si on s'y fie seul, et le module ne colore rien sans avoir produit
    /// les verifications qui justifient la couleur.
    ///
    /// LA PALETTE EST LISIBLE EN VISION DICHROMATE. Elle suit la rampe bleu - bleu pale -
    /// orange - rouge (RdYlBu a quatre classes), et non le vert-orange-rouge habituel : un
    /// deuteranope confond le vert et le rouge, c'est-a-dire exactement « ca passe » et
    /// « ca ne passe pas ». Chaque bande porte en outre un LIBELLE, pour que la legende se
    /// lise sans la couleur du tout.
    ///
    /// UNE BANDE BASSE N'EST PAS UNE BONNE NOUVELLE. Un element a 0,20 passe, mais il est
    /// probablement surdimensionne : c'est du beton et de l'acier payes pour rien. La
    /// legende le dit, au lieu de le peindre en vert rassurant.
    /// </summary>
    public static class ControlColours
    {
        public const double LightlyUtilisedLimit = 0.50;
        public const double NormalLimit = 0.85;
        public const double TightLimit = 1.00;

        /// <summary>
        /// La bande d'un element. LE STATUT PRIME SUR LE TAUX, dans les deux sens.
        ///
        /// Un element non dimensionne n'a pas de taux : il est gris, pas vert. Et un
        /// element dont une verification est en DEFAUT est surcharge meme si son taux
        /// maximal reste sous 1 — une verification peut echouer sur autre chose qu'un
        /// rapport, un espacement ou une longueur d'ancrage par exemple. Peindre celui-la
        /// en bleu serait exactement le contresens que la couleur doit eviter.
        /// </summary>
        public static ControlBand BandOf(DesignedElement element)
        {
            if (element == null || !element.IsValid) return ControlBand.NotDesigned;
            if (element.HasFailedCheck) return ControlBand.Overloaded;

            double utilization = element.MaxUtilization;
            if (utilization > TightLimit) return ControlBand.Overloaded;
            if (utilization > NormalLimit) return ControlBand.Tight;
            if (utilization > LightlyUtilisedLimit) return ControlBand.Normal;
            return ControlBand.LightlyUtilised;
        }

        public static ControlColour For(DesignedElement element)
        {
            return Of(BandOf(element));
        }

        public static ControlColour Of(ControlBand band)
        {
            switch (band)
            {
                case ControlBand.Overloaded:
                    return new ControlColour
                    {
                        Band = band, R = 215, G = 25, B = 28,
                        Label = "Ne passe pas",
                        Meaning = "Au moins une verification est en defaut, ou le taux "
                                  + "depasse 1. Le ferraillage produit n'est pas conforme."
                    };

                case ControlBand.Tight:
                    return new ControlColour
                    {
                        Band = band, R = 253, G = 174, B = 97,
                        Label = "Passe sans marge (0,85 a 1,00)",
                        Meaning = "Conforme, mais toute evolution de charge le fera "
                                  + "basculer. A surveiller si le projet n'est pas fige."
                    };

                case ControlBand.Normal:
                    return new ControlColour
                    {
                        Band = band, R = 171, G = 217, B = 233,
                        Label = "Regime courant (0,50 a 0,85)",
                        Meaning = "Conforme avec une marge normale."
                    };

                case ControlBand.LightlyUtilised:
                    return new ControlColour
                    {
                        Band = band, R = 44, G = 123, B = 182,
                        Label = "Peu sollicite (jusqu'a 0,50)",
                        Meaning = "Conforme, et probablement surdimensionne : du beton et "
                                  + "de l'acier payes pour rien. Ce n'est pas un defaut, "
                                  + "c'est une economie possible."
                    };

                default:
                    return new ControlColour
                    {
                        Band = ControlBand.NotDesigned, R = 128, G = 128, B = 128,
                        Label = "Non dimensionne",
                        Meaning = "Aucun resultat : l'element n'a pas ete calcule, ou son "
                                  + "dimensionnement n'a pas abouti. L'absence de couleur "
                                  + "n'est pas une absence de probleme."
                    };
            }
        }

        /// <summary>
        /// La legende, de la moins chargee a la plus chargee. Une vue coloree sans legende
        /// n'est pas un document : elle demande a son lecteur de deviner la convention.
        /// </summary>
        public static IEnumerable<ControlColour> Legend
        {
            get
            {
                yield return Of(ControlBand.NotDesigned);
                yield return Of(ControlBand.LightlyUtilised);
                yield return Of(ControlBand.Normal);
                yield return Of(ControlBand.Tight);
                yield return Of(ControlBand.Overloaded);
            }
        }
    }
}
