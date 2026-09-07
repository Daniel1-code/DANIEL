using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Slab
{
    /// <summary>Resultat du dimensionnement d'une bande de dalle.</summary>
    public sealed class SlabDesignResult
    {
        public SlabData Slab { get; set; }

        public bool IsValid { get; set; }

        public SlabReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Charge ELU retenue (kN/m2).</summary>
        public double UltimateLoadKnM2 { get; set; }
        /// <summary>Charge quasi-permanente retenue (kN/m2).</summary>
        public double QuasiPermanentLoadKnM2 { get; set; }

        /// <summary>Moment de calcul en travee, par metre (kN.m/m).</summary>
        public double SpanMomentKnmPerM { get; set; }
        /// <summary>Moment de calcul sur appui, par metre (kN.m/m).</summary>
        public double SupportMomentKnmPerM { get; set; }
        /// <summary>Effort tranchant de calcul, par metre (kN/m).</summary>
        public double ShearKnPerM { get; set; }

        /// <summary>Sections requises par metre (mm2/m).</summary>
        public double SpanSteelRequiredMm2PerM { get; set; }
        public double SupportSteelRequiredMm2PerM { get; set; }

        /// <summary>Verification de fleche par l'elancement limite.</summary>
        public DeflectionResult Deflection { get; set; }

        /// <summary>Maitrise de la fissuration.</summary>
        public CrackControlResult Cracking { get; set; }

        public string CodeLabel { get; set; }

        public SlabDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new SlabReinforcement();
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
