using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Column
{
    /// <summary>
    /// Resultat du dimensionnement d'un poteau : le ferraillage retenu, son plan geometrique,
    /// et la liste tracable des verifications qui l'ont valide.
    /// </summary>
    public sealed class ColumnDesignResult
    {
        public ColumnData Column { get; set; }

        public bool IsValid { get; set; }

        public ColumnReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        /// <summary>Verifications reglementaires, dans l'ordre ou elles ont ete menees.</summary>
        public List<CheckResult> Checks { get; private set; }

        /// <summary>Hypotheses et choix commentes, pour la note de calcul.</summary>
        public List<string> Notes { get; private set; }

        public List<string> Warnings { get; private set; }

        public double MinSteelAreaMm2 { get; set; }
        public double MaxSteelAreaMm2 { get; set; }

        /// <summary>Norme et Annexe Nationale effectivement appliquees.</summary>
        public string CodeLabel { get; set; }

        public ColumnDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new ColumnReinforcement();
            Plan = new ReinforcementPlan();
        }

        /// <summary>Taux de travail le plus defavorable de toutes les verifications menees.</summary>
        public double MaxUtilization
        {
            get
            {
                var applicable = Checks.Where(c => c.Status != CheckStatus.NotApplicable).ToList();
                return applicable.Count == 0 ? 0.0 : applicable.Max(c => c.Utilization);
            }
        }

        public bool HasFailedCheck
        {
            get { return Checks.Any(c => c.Status == CheckStatus.Fail); }
        }

        public string Status
        {
            get
            {
                if (!IsValid) return "Echec";
                if (HasFailedCheck) return "Non conforme";
                return Warnings.Count > 0 ? "A verifier" : "OK";
            }
        }

        public double SteelRatioPercent
        {
            get
            {
                if (Column == null || Column.GrossAreaMm2 <= 0) return 0.0;
                return 100.0 * Reinforcement.SteelAreaMm2 / Column.GrossAreaMm2;
            }
        }
    }
}
