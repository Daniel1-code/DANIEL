using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Stair
{
    /// <summary>Empreinte des donnees d'entree d'une volee d'escalier.</summary>
    public static class StairDesignFingerprint
    {
        public static string Compute(StairData stair, StairDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("R", stair.RiserHeightMm)
                       .Add("G", stair.TreadDepthMm)
                       .Add("n", stair.RiserCount)
                       .Add("t", stair.WaistThicknessMm)
                       .Add("b", stair.WidthMm)
                       .Add("tp", stair.LandingThicknessMm)
                       .Add("Lp", stair.LandingSpanMm)
                       .Add("kind", stair.SpanKind.ToString())
                       .Add("shape", stair.Shape.ToString());

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa)
                       .Add("gamma", settings.ConcreteUnitWeightKnM3);

            fingerprint.Add("src", settings.MomentSource.ToString())
                       .Add("gt", settings.TreadFinishKnM2)
                       .Add("gs", settings.SoffitFinishKnM2)
                       .Add("q", settings.VariableLoadKnM2)
                       .Add("Qk", settings.ConcentratedLoadKn)
                       .Add("pp", settings.IncludeSelfWeight)
                       .Add("cat", settings.Category.ToString())
                       .Add("Md", settings.SpanMomentKnmPerM)
                       .Add("Ma", settings.SupportMomentKnmPerM)
                       .Add("V", settings.ShearKnPerM);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("cover", settings.CoverMm);

            fingerprint.Add("partitions", settings.SupportsPartitions)
                       .Add("wk", settings.CrackWidthLimitMm)
                       .Add("autoPhi", settings.AutoMeshDiameter)
                       .Add("phi", settings.ForcedMeshDiameterMm)
                       .Add("top", settings.TopReinforcement);

            // Les dispositions font partie de l'identite du calcul : deux plans issus de
            // regles differentes ne sont pas le meme ferraillage.
            StairDetailingRules rules = settings.Detailing ?? new StairDetailingRules();
            fingerprint.Add("topMode", rules.TopBarExtent.ToString())
                       .Add("topFrac", rules.TopBarSpanFraction)
                       .Add("topFixed", rules.TopBarFixedLengthMm)
                       .Add("knee", rules.KneeJoint.ToString())
                       .Add("kneeF", rules.KneeAnchorageFactor)
                       .Add("distAbove", rules.DistributionAboveMainBars)
                       .Add("contTop", rules.ContinuousTopMesh)
                       .Add("stock", rules.StockLengthMm)
                       .Add("round", rules.LengthRoundingMm);

            return fingerprint.Compute();
        }
    }
}
