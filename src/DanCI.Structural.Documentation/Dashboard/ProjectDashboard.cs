using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Quantities;

namespace DanCI.Structural.Documentation.Dashboard
{
    /// <summary>
    /// Combien d'elements sont en defaut sur un meme article, et lesquels.
    ///
    /// C'EST LA LIGNE LA PLUS UTILE DU TABLEAU DE BORD. Sur un projet de deux cents
    /// elements, « 18 elements en defaut de fleche art. 7.4.2 » dit ou est le probleme de
    /// conception ; la liste des 18 noms, non. Un defaut isole est une erreur de saisie ;
    /// un article qui tombe dix-huit fois est une hypothese de projet a revoir.
    /// </summary>
    public sealed class ClauseFailure
    {
        public string Code { get; set; }
        public string Clause { get; set; }
        public string Description { get; set; }
        public int ElementCount { get; set; }
        public double WorstUtilization { get; set; }
        public IList<string> Elements { get; set; }

        public ClauseFailure()
        {
            Elements = new List<string>();
        }

        public string Reference
        {
            get { return string.Format("{0} art. {1}", Code, Clause).Trim(); }
        }
    }

    /// <summary>Totaux d'une famille d'elements.</summary>
    public sealed class KindTotals
    {
        public ElementKind Kind { get; set; }
        public int Count { get; set; }
        public int NotCompliantCount { get; set; }
        public double SteelKg { get; set; }
        public double ConcreteM3 { get; set; }

        public string Label { get { return DesignedElement.Label(Kind); } }

        /// <summary>
        /// Ratio d'acier de la famille. Il se compare a l'experience du projeteur : un
        /// poteau courant tourne autour de 120 a 180 kg/m3, une dalle autour de 70 a 100.
        /// Un ratio hors norme ne prouve pas une erreur, mais il en signale souvent une.
        /// </summary>
        public double RatioKgPerM3
        {
            get { return ConcreteM3 > 0 ? SteelKg / ConcreteM3 : 0.0; }
        }
    }

    /// <summary>Synthese d'un lot d'elements dimensionnes.</summary>
    public sealed class DashboardSummary
    {
        public int Total { get; set; }
        public int CompliantCount { get; set; }
        public int ToVerifyCount { get; set; }
        public int NotCompliantCount { get; set; }
        public int FailedCount { get; set; }

        public double SteelKg { get; set; }
        public double ConcreteM3 { get; set; }

        public IList<DesignedElement> WorstUtilised { get; set; }
        public IList<ClauseFailure> FailuresByClause { get; set; }
        public IList<KindTotals> ByKind { get; set; }

        public DashboardSummary()
        {
            WorstUtilised = new List<DesignedElement>();
            FailuresByClause = new List<ClauseFailure>();
            ByKind = new List<KindTotals>();
        }

        public double RatioKgPerM3
        {
            get { return ConcreteM3 > 0 ? SteelKg / ConcreteM3 : 0.0; }
        }

        /// <summary>
        /// Le lot est-il livrable ? Un seul element non conforme suffit a repondre non, et
        /// c'est voulu : un projet n'est pas conforme « a 97 % ».
        /// </summary>
        public bool IsDeliverable
        {
            get { return Total > 0 && NotCompliantCount == 0 && FailedCount == 0; }
        }

        /// <summary>
        /// Le taux de travail le plus eleve du lot, tous elements confondus. Il n'y a
        /// volontairement aucune moyenne dans cette classe.
        /// </summary>
        public double WorstUtilization
        {
            get
            {
                return WorstUtilised.Count == 0 ? 0.0 : WorstUtilised[0].MaxUtilization;
            }
        }
    }

    /// <summary>
    /// TABLEAU DE BORD DE PROJET. Il ne calcule rien : il regarde ce qui a deja ete calcule
    /// et repond a trois questions, dans cet ordre.
    ///
    /// 1. QU'EST-CE QUI NE PASSE PAS ? Les elements non conformes, tries du plus charge au
    ///    moins charge.
    /// 2. POURQUOI ? Les articles en defaut, par nombre d'elements touches.
    /// 3. COMBIEN ? L'acier, le beton, et le ratio par famille.
    ///
    /// AUCUNE MOYENNE DE TAUX DE TRAVAIL n'y figure, ni globale ni par famille. Un taux
    /// moyen n'a pas de sens mecanique et il rassure exactement quand il ne faut pas.
    /// </summary>
    public static class ProjectDashboard
    {
        /// <summary>Nombre d'elements les plus charges retenus par defaut.</summary>
        public const int DefaultWorstCount = 10;

        public static DashboardSummary Build(IEnumerable<DesignedElement> elements)
        {
            return Build(elements, DefaultWorstCount);
        }

        public static DashboardSummary Build(IEnumerable<DesignedElement> elements,
                                             int worstCount)
        {
            var summary = new DashboardSummary();
            if (elements == null) return summary;

            var list = elements.Where(e => e != null).ToList();
            summary.Total = list.Count;
            if (list.Count == 0) return summary;

            foreach (DesignedElement element in list)
            {
                switch (element.Status)
                {
                    case DesignStatus.Failed: summary.FailedCount++; break;
                    case DesignStatus.NotCompliant: summary.NotCompliantCount++; break;
                    case DesignStatus.ToVerify: summary.ToVerifyCount++; break;
                    default: summary.CompliantCount++; break;
                }

                if (element.Quantities == null) continue;
                summary.SteelKg += element.Quantities.TotalMassKg;
                summary.ConcreteM3 += element.Quantities.ConcreteVolumeM3;
            }

            // Le tri met le plus charge en tete. A taux egal, l'element en defaut passe
            // devant : c'est lui qu'on veut lire.
            summary.WorstUtilised = list
                .OrderByDescending(e => e.MaxUtilization)
                .ThenByDescending(e => e.HasFailedCheck)
                .Take(Math.Max(worstCount, 0))
                .ToList();

            summary.FailuresByClause = GroupFailures(list);
            summary.ByKind = GroupByKind(list);
            return summary;
        }

        /// <summary>
        /// Regroupe les verifications en defaut PAR ARTICLE. La cle est le couple norme +
        /// article : deux articles differents d'une meme norme sont deux problemes
        /// differents, et le meme article de deux normes aussi.
        /// </summary>
        private static IList<ClauseFailure> GroupFailures(IEnumerable<DesignedElement> elements)
        {
            var byClause = new Dictionary<string, ClauseFailure>();

            foreach (DesignedElement element in elements)
            {
                // Un element peut echouer plusieurs fois sur le meme article — par exemple
                // dans les deux directions. Il ne compte qu'une fois dans le nombre
                // d'elements touches, sinon le classement ne mesure plus rien.
                var seen = new HashSet<string>();

                foreach (CheckResult check in element.FailedChecks)
                {
                    string key = (check.Code ?? string.Empty) + "|" + (check.Clause ?? string.Empty);

                    ClauseFailure failure;
                    if (!byClause.TryGetValue(key, out failure))
                    {
                        failure = new ClauseFailure
                        {
                            Code = check.Code,
                            Clause = check.Clause,
                            Description = check.Description
                        };
                        byClause[key] = failure;
                    }

                    if (check.Utilization > failure.WorstUtilization)
                    {
                        failure.WorstUtilization = check.Utilization;
                    }

                    if (seen.Add(key))
                    {
                        failure.ElementCount++;
                        failure.Elements.Add(element.Name ?? element.Mark ?? "?");
                    }
                }
            }

            return byClause.Values
                .OrderByDescending(f => f.ElementCount)
                .ThenByDescending(f => f.WorstUtilization)
                .ToList();
        }

        private static IList<KindTotals> GroupByKind(IEnumerable<DesignedElement> elements)
        {
            var byKind = new Dictionary<ElementKind, KindTotals>();

            foreach (DesignedElement element in elements)
            {
                KindTotals totals;
                if (!byKind.TryGetValue(element.Kind, out totals))
                {
                    totals = new KindTotals { Kind = element.Kind };
                    byKind[element.Kind] = totals;
                }

                totals.Count++;
                if (element.Status == DesignStatus.NotCompliant
                    || element.Status == DesignStatus.Failed)
                {
                    totals.NotCompliantCount++;
                }

                if (element.Quantities == null) continue;
                totals.SteelKg += element.Quantities.TotalMassKg;
                totals.ConcreteM3 += element.Quantities.ConcreteVolumeM3;
            }

            return byKind.Values.OrderBy(t => t.Kind).ToList();
        }
    }
}
