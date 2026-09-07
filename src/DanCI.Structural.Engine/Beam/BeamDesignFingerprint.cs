using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.Beam
{
    /// <summary>
    /// Empreinte des donnees d'une poutre : geometrie, materiaux, sollicitations et
    /// preferences de ferraillage.
    /// </summary>
    public static class BeamDesignFingerprint
    {
        public static string Compute(BeamData beam, BeamDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("shape", beam.Shape.ToString())
                       .Add("bw", beam.WebWidthMm)
                       .Add("h", beam.HeightMm)
                       .Add("L", beam.SpanMm);

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa)
                       .Add("delta", settings.RedistributionRatio);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("qc", settings.SpecialQualityControl)
                       .Add("cover", settings.CoverMm)
                       .Add("dg", settings.AggregateSizeMm);

            fingerprint.Add("Mspan", settings.SpanMomentKnm)
                       .Add("Mleft", settings.LeftSupportMomentKnm)
                       .Add("Mright", settings.RightSupportMomentKnm)
                       .Add("Vleft", settings.LeftShearKn)
                       .Add("Vright", settings.RightShearKn)
                       .Add("N", settings.AxialForceKn)
                       .Add("spanKind", settings.SpanKind.ToString());

            fingerprint.Add("tee", settings.TreatAsTSection)
                       .Add("beff", settings.FlangeWidthMm)
                       .Add("hf", settings.FlangeThicknessMm);

            fingerprint.Add("autoPhi", settings.AutoLongitudinalDiameter)
                       .Add("phi", settings.ForcedLongitudinalDiameterMm)
                       .Add("layers", settings.MaxLayers)
                       .Add("autoT", settings.AutoStirrupDiameter)
                       .Add("phit", settings.ForcedStirrupDiameterMm)
                       .Add("legs", settings.StirrupLegs);

            return fingerprint.Compute();
        }
    }
}
