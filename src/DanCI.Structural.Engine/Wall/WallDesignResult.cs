using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Wall
{
    /// <summary>Resultat du dimensionnement d'un voile.</summary>
    public sealed class WallDesignResult
    {
        public WallData Wall { get; set; }

        public bool IsValid { get; set; }

        public WallReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Longueur de flambement retenue.</summary>
        public WallBucklingResult Buckling { get; set; }

        /// <summary>Second ordre hors plan.</summary>
        public SecondOrderResult SecondOrder { get; set; }

        /// <summary>Elancement mecanique lambda de la bande verticale.</summary>
        public double SlendernessRatio { get; set; }

        /// <summary>Moment hors plan de calcul, second ordre inclus (kN.m/m).</summary>
        public double DesignOutOfPlaneMomentKnmPerM { get; set; }

        /// <summary>Section verticale requise par metre, deux nappes (mm2/m).</summary>
        public double VerticalSteelRequiredMm2PerM { get; set; }

        /// <summary>Section de rive requise pour la flexion dans le plan (mm2).</summary>
        public double EdgeSteelRequiredMm2 { get; set; }

        public string CodeLabel { get; set; }

        public WallDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new WallReinforcement();
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
