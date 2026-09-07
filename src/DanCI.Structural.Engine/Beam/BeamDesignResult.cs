using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Beam
{
    /// <summary>Resultat du dimensionnement d'une poutre.</summary>
    public sealed class BeamDesignResult
    {
        public BeamData Beam { get; set; }

        public bool IsValid { get; set; }

        public BeamReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>Sections d'acier requises, pour la note de calcul (mm2).</summary>
        public double SpanSteelRequiredMm2 { get; set; }
        public double LeftSteelRequiredMm2 { get; set; }
        public double RightSteelRequiredMm2 { get; set; }
        public double MinSteelAreaMm2 { get; set; }
        public double MaxSteelAreaMm2 { get; set; }

        /// <summary>Largeur participante de la table retenue (mm), section en T.</summary>
        public double EffectiveFlangeWidthMm { get; set; }

        public string CodeLabel { get; set; }

        public BeamDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Reinforcement = new BeamReinforcement();
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

        /// <summary>Section d'acier longitudinal totale effectivement posee (mm2).</summary>
        public double ProvidedSteelMm2
        {
            get
            {
                return Reinforcement.BottomSpan.AreaMm2
                       + Reinforcement.TopContinuous.AreaMm2
                       + Reinforcement.TopLeft.AreaMm2
                       + Reinforcement.TopRight.AreaMm2;
            }
        }
    }
}
