using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.EC7;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.IsolatedFooting
{
    /// <summary>Resultat du dimensionnement d'une semelle isolee.</summary>
    public sealed class FootingDesignResult
    {
        public FootingData Footing { get; set; }

        public bool IsValid { get; set; }

        public FootingReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Distribution des contraintes sous la semelle.</summary>
        public SoilPressureResult Pressure { get; set; }

        /// <summary>Verification au poinconnement.</summary>
        public PunchingResult Punching { get; set; }

        /// <summary>Sections d'acier requises par metre (mm2/m).</summary>
        public double RequiredSteelXMm2PerM { get; set; }
        public double RequiredSteelYMm2PerM { get; set; }

        public string CodeLabel { get; set; }

        public FootingDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new FootingReinforcement();
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
