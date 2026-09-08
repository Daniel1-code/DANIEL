using System;
using System.Collections.Generic;
using System.Globalization;

namespace DanCI.Structural.Reinforcement.Plan
{
    /// <summary>D'ou vient la longueur des chapeaux.</summary>
    public enum TopBarExtentMode
    {
        /// <summary>Une fraction de la portee, plafonnee par l'ancrage. Pratique courante.</summary>
        FractionOfSpan,

        /// <summary>La seule longueur d'ancrage l_bd. Le minimum defendable.</summary>
        AnchorageOnly,

        /// <summary>Une longueur imposee par l'ingenieur.</summary>
        Fixed,

        /// <summary>Le chapeau file d'un appui a l'autre : nappe superieure continue.</summary>
        FullSpan
    }

    /// <summary>Comment le noeud volee-palier est ferraille.</summary>
    public enum KneeJointDetail
    {
        /// <summary>
        /// Les deux nappes se croisent et s'ancrent chacune dans la face opposee. C'est le
        /// detail de la pratique etablie pour un angle rentrant tendu.
        /// </summary>
        CrossedBars,

        /// <summary>
        /// Les nappes s'arretent au pli et une epingle diagonale separee reprend la
        /// resultante. Variante admise, qui demande une barre de plus.
        /// </summary>
        SeparateHairpin
    }

    /// <summary>
    /// Regles de disposition d'une volee : les DECISIONS de ferraillage, sorties du code et
    /// mises entre les mains de l'ingenieur.
    ///
    /// Un plan de ferraillage n'est pas une forme predefinie que l'on plaque sur une
    /// geometrie. C'est une suite de decisions — jusqu'ou vont les chapeaux, comment se
    /// traite le noeud, ou se coupent les barres trop longues — dont chacune a une raison.
    /// Ces raisons sont ici des PARAMETRES : le moteur en propose une valeur deduite de la
    /// geometrie et de la norme, dit d'ou elle vient, et accepte qu'on la change.
    ///
    /// Les valeurs par defaut ne sont pas neutres pour autant. Celles qui viennent d'un
    /// article de l'Eurocode sont nommees ; celles qui relevent de la pratique courante le
    /// sont aussi, et le moteur ne les fait pas passer pour normatives.
    /// </summary>
    public sealed class StairDetailingRules
    {
        // --- Chapeaux ---

        public TopBarExtentMode TopBarExtent { get; set; }

        /// <summary>Fraction de la portee couverte par le chapeau, mode FractionOfSpan.</summary>
        public double TopBarSpanFraction { get; set; }

        /// <summary>Longueur imposee du chapeau (mm), mode Fixed.</summary>
        public double TopBarFixedLengthMm { get; set; }

        // --- Noeud ---

        public KneeJointDetail KneeJoint { get; set; }

        /// <summary>
        /// Multiplicateur applique a l_bd pour l'ancrage au-dela du pli. 1,0 par defaut :
        /// l'ancrage de calcul, ni plus ni moins.
        /// </summary>
        public double KneeAnchorageFactor { get; set; }

        // --- Nappes ---

        /// <summary>
        /// La repartition est-elle posee AU-DESSUS des barres porteuses ? Oui par defaut en
        /// nappe inferieure : les porteuses doivent avoir la plus grande hauteur utile.
        /// </summary>
        public bool DistributionAboveMainBars { get; set; }

        /// <summary>Poser une nappe superieure continue meme sans moment sur appui.</summary>
        public bool ContinuousTopMesh { get; set; }

        // --- Barres longues ---

        /// <summary>
        /// Longueur de barre de stock (mm). Au-dela, la barre doit etre recouverte, et le
        /// moteur le SIGNALE sans decouper : repartir les recouvrements en quinconce est une
        /// decision de plan, pas un automatisme, et la faire silencieusement mettrait tous
        /// les recouvrements au meme endroit.
        /// </summary>
        public double StockLengthMm { get; set; }

        // --- Arrondis de chantier ---

        /// <summary>Pas d'arrondi des longueurs faconnees (mm).</summary>
        public double LengthRoundingMm { get; set; }

        public StairDetailingRules()
        {
            TopBarExtent = TopBarExtentMode.FractionOfSpan;
            TopBarSpanFraction = 0.25;
            TopBarFixedLengthMm = 1000.0;

            KneeJoint = KneeJointDetail.CrossedBars;
            KneeAnchorageFactor = 1.0;

            DistributionAboveMainBars = true;
            ContinuousTopMesh = false;

            StockLengthMm = 12000.0;

            LengthRoundingMm = 50.0;
        }

        public StairDetailingRules Clone()
        {
            return (StairDetailingRules)MemberwiseClone();
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (TopBarSpanFraction <= 0 || TopBarSpanFraction > 1.0)
            {
                errors.Add("La fraction de portee des chapeaux doit etre comprise entre 0 et 1.");
            }
            if (TopBarExtent == TopBarExtentMode.Fixed && TopBarFixedLengthMm <= 0)
            {
                errors.Add("La longueur imposee des chapeaux doit etre positive.");
            }
            if (KneeAnchorageFactor < 1.0)
            {
                errors.Add("Le coefficient d'ancrage au noeud ne peut pas etre inferieur a 1 : "
                           + "reduire l'ancrage de calcul n'est pas un reglage, c'est une faute.");
            }
            if (StockLengthMm < 3000.0 || StockLengthMm > 18000.0)
            {
                errors.Add("La longueur de barre de stock doit etre comprise entre 3 et 18 m.");
            }
            if (LengthRoundingMm <= 0 || LengthRoundingMm > 200.0)
            {
                errors.Add("Le pas d'arrondi doit etre compris entre 0 et 200 mm.");
            }
            return errors;
        }

        /// <summary>
        /// Longueur de chapeau retenue, et la raison de ce choix. Les deux sortent ensemble :
        /// une longueur sans sa raison n'est pas un parametre, c'est un nombre magique.
        /// </summary>
        public DetailingDecision ResolveTopBarLength(double spanMm, double anchorageMm)
        {
            switch (TopBarExtent)
            {
                case TopBarExtentMode.AnchorageOnly:
                    return new DetailingDecision(
                        "Longueur des chapeaux", Round(anchorageMm),
                        string.Format(CultureInfo.CurrentCulture,
                            "l_bd seul, soit {0:0} mm. C'est le minimum defendable : le "
                            + "chapeau ancre la barre, il ne couvre aucune longueur de "
                            + "moment negatif.", anchorageMm));

                case TopBarExtentMode.Fixed:
                    return new DetailingDecision(
                        "Longueur des chapeaux", Round(TopBarFixedLengthMm),
                        string.Format(CultureInfo.CurrentCulture,
                            "Longueur imposee par l'ingenieur : {0:0} mm. Le moteur ne la "
                            + "justifie pas, il l'applique.", TopBarFixedLengthMm));

                case TopBarExtentMode.FullSpan:
                    return new DetailingDecision(
                        "Longueur des chapeaux", Round(spanMm),
                        "Nappe superieure continue d'un appui a l'autre. Choix courant quand "
                        + "l'encastrement reel dans les paliers n'est pas quantifie et qu'on "
                        + "prefere ne pas dependre d'une longueur d'arret.");

                default:
                    double fraction = spanMm * TopBarSpanFraction;
                    double retained = Math.Max(fraction, anchorageMm);
                    return new DetailingDecision(
                        "Longueur des chapeaux", Round(retained),
                        string.Format(CultureInfo.CurrentCulture,
                            "max({0:0.##} L ; l_bd) = max({1:0} ; {2:0}) = {3:0} mm. La "
                            + "fraction de portee releve de la PRATIQUE COURANTE, pas d'un "
                            + "article de l'EC2 : l'Eurocode demande une enveloppe de "
                            + "moments, que le moteur ne construit pas.",
                            TopBarSpanFraction, fraction, anchorageMm, retained));
            }
        }

        /// <summary>Ancrage retenu au-dela du pli, et sa raison.</summary>
        public DetailingDecision ResolveKneeAnchorage(double anchorageMm)
        {
            double retained = anchorageMm * KneeAnchorageFactor;
            string reason = Math.Abs(KneeAnchorageFactor - 1.0) < 1e-6
                ? string.Format(CultureInfo.CurrentCulture,
                    "l_bd de calcul, {0:0} mm au-dela du pli.", anchorageMm)
                : string.Format(CultureInfo.CurrentCulture,
                    "{0:0.##} x l_bd = {1:0} mm au-dela du pli, majoration demandee par "
                    + "l'ingenieur.", KneeAnchorageFactor, retained);
            return new DetailingDecision("Ancrage au noeud", Round(retained), reason);
        }

        public double Round(double value)
        {
            double step = LengthRoundingMm > 0 ? LengthRoundingMm : 1.0;
            return Math.Ceiling(value / step) * step;
        }
    }

    /// <summary>
    /// Une decision de ferraillage : ce qui a ete decide, la valeur retenue, et POURQUOI.
    /// C'est la troisieme colonne qui compte : sans elle, un plan n'est qu'une forme
    /// tombee du ciel.
    /// </summary>
    public sealed class DetailingDecision
    {
        public string Question { get; private set; }
        public double ValueMm { get; private set; }
        public string Reason { get; private set; }

        public DetailingDecision(string question, double valueMm, string reason)
        {
            Question = question;
            ValueMm = valueMm;
            Reason = reason;
        }

        public override string ToString()
        {
            return string.Format(CultureInfo.CurrentCulture, "{0} : {1:0} mm - {2}",
                                 Question, ValueMm, Reason);
        }
    }
}
