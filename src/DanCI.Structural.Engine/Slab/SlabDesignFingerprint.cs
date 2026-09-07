using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.Slab
{
    /// <summary>Empreinte des donnees d'entree d'une bande de dalle.</summary>
    public static class SlabDesignFingerprint
    {
        public static string Compute(SlabData slab, SlabDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("h", slab.ThicknessMm)
                       .Add("L", slab.SpanMm)
                       .Add("B", slab.WidthMm)
                       .Add("kind", slab.SpanKind.ToString());

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa);

            fingerprint.Add("source", settings.MomentSource.ToString())
                       .Add("g", settings.PermanentLoadKnM2)
                       .Add("q", settings.VariableLoadKnM2)
                       .Add("pp", settings.IncludeSelfWeight)
                       .Add("gamma", settings.ConcreteUnitWeightKnM3)
                       .Add("cat", settings.Category.ToString())
                       .Add("Mspan", settings.SpanMomentKnmPerM)
                       .Add("Msup", settings.SupportMomentKnmPerM)
                       .Add("V", settings.ShearKnPerM);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("cover", settings.CoverMm);

            fingerprint.Add("partitions", settings.SupportsPartitions)
                       .Add("wmax", settings.CrackWidthLimitMm);

            fingerprint.Add("autoPhi", settings.AutoMeshDiameter)
                       .Add("phi", settings.ForcedMeshDiameterMm)
                       .Add("top", settings.TopReinforcement);

            return fingerprint.Compute();
        }
    }
}
