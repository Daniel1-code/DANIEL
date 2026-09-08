using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.GradeBeam
{
    /// <summary>Empreinte des donnees d'entree d'une longrine.</summary>
    public static class GradeBeamDesignFingerprint
    {
        public static string Compute(GradeBeamData beam, GradeBeamDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("b", beam.WidthMm)
                       .Add("h", beam.HeightMm)
                       .Add("L", beam.SpanMm)
                       .Add("kind", beam.SpanKind.ToString());

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa)
                       .Add("gamma", settings.ConcreteUnitWeightKnM3);

            fingerprint.Add("bedding", settings.Bedding.ToString())
                       .Add("w", settings.WallLoadKnPerM)
                       .Add("pp", settings.IncludeSelfWeight)
                       .Add("sigma", settings.AllowableBearingPressureKpa);

            fingerprint.Add("seismic", settings.SeismicDesign)
                       .Add("ground", settings.Ground.ToString())
                       .Add("alpha", settings.GroundAccelerationRatio)
                       .Add("S", settings.SoilFactor)
                       .Add("Ncol", settings.MeanColumnAxialLoadKn)
                       .Add("storeys", settings.StoreyCount)
                       .Add("tie", settings.ManualTieForceKn);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("soil", settings.CastDirectlyAgainstSoil)
                       .Add("cover", settings.CoverMm);

            fingerprint.Add("autoPhi", settings.AutoLongitudinalDiameter)
                       .Add("phi", settings.ForcedLongitudinalDiameterMm)
                       .Add("layers", settings.MaxLayers)
                       .Add("autoT", settings.AutoStirrupDiameter)
                       .Add("phit", settings.ForcedStirrupDiameterMm)
                       .Add("legs", settings.StirrupLegs)
                       .Add("dg", settings.AggregateSizeMm);

            return fingerprint.Compute();
        }
    }
}
