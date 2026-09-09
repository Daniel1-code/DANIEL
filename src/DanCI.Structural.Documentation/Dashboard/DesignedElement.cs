using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Documentation.Dashboard
{
    /// <summary>Famille d'element, pour regrouper et comparer ce qui est comparable.</summary>
    public enum ElementKind
    {
        Column,
        Beam,
        Slab,
        Wall,
        IsolatedFooting,
        StripFooting,
        GradeBeam,
        Stair
    }

    /// <summary>
    /// Etat d'un element dimensionne, dans l'ordre de gravite croissante. C'est cet ordre
    /// qui permet de trier un projet : ce qu'on veut voir en premier, c'est ce qui ne passe
    /// pas.
    /// </summary>
    public enum DesignStatus
    {
        /// <summary>Toutes les verifications passent, sans reserve.</summary>
        Compliant,

        /// <summary>Les verifications passent, mais le calcul porte des reserves.</summary>
        ToVerify,

        /// <summary>Au moins une verification est en defaut.</summary>
        NotCompliant,

        /// <summary>Le dimensionnement n'a pas abouti : aucun ferraillage n'est produit.</summary>
        Failed
    }

    /// <summary>
    /// UN ELEMENT DIMENSIONNE, VU DE FACON UNIFORME.
    ///
    /// Les huit modules produisent chacun leur propre type de resultat, et c'est normal :
    /// un poteau et une semelle n'ont pas les memes grandeurs. Mais ils produisent tous la
    /// meme chose au fond — une liste de verifications, un plan de ferraillage, un statut —
    /// et c'est cela, et cela seul, dont un tableau de bord, un mode batch ou une couleur de
    /// controle ont besoin.
    ///
    /// Ce type est cette vue commune. Il ne remplace aucun resultat de module : il en est
    /// l'adaptation, construite par l'appelant qui, lui, sait de quel module il vient.
    ///
    /// LE STATUT EST RECALCULE ICI, a partir des memes regles que les modules. Un test
    /// verifie que les deux coincident : deux definitions du mot « conforme » qui divergent
    /// seraient pires qu'une seule imparfaite.
    /// </summary>
    public sealed class DesignedElement
    {
        public string Name { get; set; }
        public string Mark { get; set; }
        public ElementKind Kind { get; set; }

        /// <summary>Le dimensionnement a-t-il abouti a un ferraillage ?</summary>
        public bool IsValid { get; set; }

        public IList<CheckResult> Checks { get; set; }
        public IList<string> Warnings { get; set; }
        public ReinforcementPlan Plan { get; set; }
        public SteelQuantities Quantities { get; set; }

        public DesignedElement()
        {
            Checks = new List<CheckResult>();
            Warnings = new List<string>();
        }

        /// <summary>
        /// Taux de travail le plus eleve parmi les verifications APPLICABLES.
        ///
        /// C'est un maximum, jamais une moyenne. Un element dont neuf verifications passent
        /// a 0,3 et la dixieme echoue a 2,9 n'est pas « a 0,56 en moyenne » : il est en
        /// defaut. Moyenner des taux de travail est la facon la plus simple de rendre un
        /// projet dangereux ET rassurant.
        /// </summary>
        public double MaxUtilization
        {
            get
            {
                if (Checks == null) return 0.0;
                var applicable = Checks
                    .Where(c => c != null && c.Status != CheckStatus.NotApplicable)
                    .ToList();
                return applicable.Count == 0 ? 0.0 : applicable.Max(c => c.Utilization);
            }
        }

        public IEnumerable<CheckResult> FailedChecks
        {
            get
            {
                return Checks == null
                    ? Enumerable.Empty<CheckResult>()
                    : Checks.Where(c => c != null && c.Status == CheckStatus.Fail);
            }
        }

        public bool HasFailedCheck { get { return FailedChecks.Any(); } }

        public DesignStatus Status
        {
            get
            {
                if (!IsValid) return DesignStatus.Failed;
                if (HasFailedCheck) return DesignStatus.NotCompliant;
                return Warnings != null && Warnings.Count > 0
                    ? DesignStatus.ToVerify : DesignStatus.Compliant;
            }
        }

        /// <summary>Le libelle employe par les modules et les fenetres.</summary>
        public string StatusLabel { get { return Label(Status); } }

        public static string Label(DesignStatus status)
        {
            switch (status)
            {
                case DesignStatus.Failed: return "Echec";
                case DesignStatus.NotCompliant: return "Non conforme";
                case DesignStatus.ToVerify: return "A verifier";
                default: return "OK";
            }
        }

        public static string Label(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Column: return "Poteaux";
                case ElementKind.Beam: return "Poutres";
                case ElementKind.Slab: return "Dalles";
                case ElementKind.Wall: return "Voiles";
                case ElementKind.IsolatedFooting: return "Semelles isolees";
                case ElementKind.StripFooting: return "Semelles filantes";
                case ElementKind.GradeBeam: return "Longrines";
                default: return "Escaliers";
            }
        }
    }
}
