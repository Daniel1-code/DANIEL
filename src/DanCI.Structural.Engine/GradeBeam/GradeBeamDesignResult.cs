using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Eurocodes.EC8;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.GradeBeam
{
    /// <summary>Resultat du dimensionnement d'une longrine.</summary>
    public sealed class GradeBeamDesignResult
    {
        public GradeBeamData Beam { get; set; }

        public bool IsValid { get; set; }

        public GradeBeamReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Charge lineique de calcul retenue (kN/m).</summary>
        public double DesignLoadKnPerM { get; set; }

        /// <summary>Moment de calcul en travee (kN.m).</summary>
        public double SpanMomentKnm { get; set; }
        /// <summary>Effort tranchant de calcul aux appuis (kN).</summary>
        public double ShearKn { get; set; }

        /// <summary>Effort de liaison retenu, alterne (kN).</summary>
        public double TieForceKn { get; set; }
        /// <summary>Origine et justification de l'effort de liaison.</summary>
        public TieForceResult Tie { get; set; }

        /// <summary>Sections requises (mm2).</summary>
        public double BottomSteelRequiredMm2 { get; set; }
        public double TopSteelRequiredMm2 { get; set; }

        public string CodeLabel { get; set; }

        public GradeBeamDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new GradeBeamReinforcement();
            Plan = new ReinforcementPlan();
        }

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
    }
}
