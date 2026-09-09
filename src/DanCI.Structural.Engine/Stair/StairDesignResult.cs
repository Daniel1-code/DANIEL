using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Eurocodes.EC1;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Stair
{
    /// <summary>Resultat du dimensionnement d'une bande de 1 m de volee.</summary>
    public sealed class StairDesignResult
    {
        public StairData Stair { get; set; }

        public bool IsValid { get; set; }

        public StairReinforcement Reinforcement { get; set; }

        public ReinforcementPlan Plan { get; set; }

        public List<CheckResult> Checks { get; private set; }
        public List<string> Notes { get; private set; }
        public List<string> Warnings { get; private set; }

        /// <summary>
        /// Decisions de disposition retenues, chacune avec sa raison. Elles font partie du
        /// calcul : un plan produit sans dire quelles decisions l'ont forme n'est pas
        /// verifiable.
        /// </summary>
        public List<DetailingDecision> Decisions { get; private set; }

        /// <summary>Descente de charge de la paillasse.</summary>
        public StairLoadBreakdown FlightLoad { get; set; }

        /// <summary>Descente de charge du palier.</summary>
        public StairLoadBreakdown LandingLoad { get; set; }

        /// <summary>Charge ELU sur la volee (kN/m2 de projection horizontale).</summary>
        public double FlightUltimateLoadKnM2 { get; set; }

        /// <summary>Charge ELU sur le palier (kN/m2).</summary>
        public double LandingUltimateLoadKnM2 { get; set; }

        /// <summary>Statique de la travee.</summary>
        public StairStaticsResult Statics { get; set; }

        public double SpanMomentKnmPerM { get; set; }
        public double SupportMomentKnmPerM { get; set; }
        public double ShearKnPerM { get; set; }

        public double SpanSteelRequiredMm2PerM { get; set; }
        public double SupportSteelRequiredMm2PerM { get; set; }

        /// <summary>
        /// Section superieure exigee par l'art. 9.3.1.2(2), soit celle capable de reprendre
        /// 25 % du moment de travee. Elle existe meme quand aucun moment sur appui n'a ete
        /// declare : c'est justement le cas que l'article vise.
        /// </summary>
        public double PartialFixitySteelMm2PerM { get; set; }

        public DeflectionResult Deflection { get; set; }

        /// <summary>Maitrise de la fissuration.</summary>
        public CrackControlResult Cracking { get; set; }

        /// <summary>Charge quasi-permanente sur la volee (kN/m2).</summary>
        public double QuasiPermanentLoadKnM2 { get; set; }

        /// <summary>
        /// Moment de la situation alternative a charge concentree (kN.m/m). Zero si Q_k
        /// n'est pas declaree.
        /// </summary>
        public double ConcentratedLoadMomentKnmPerM { get; set; }

        /// <summary>La charge concentree gouverne-t-elle le dimensionnement ?</summary>
        public bool ConcentratedLoadGoverns { get; set; }

        public string CodeLabel { get; set; }

        public StairDesignResult()
        {
            Checks = new List<CheckResult>();
            Notes = new List<string>();
            Warnings = new List<string>();
            Decisions = new List<DetailingDecision>();
            Reinforcement = new StairReinforcement();
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
