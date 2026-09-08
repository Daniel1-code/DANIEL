using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.StripFooting
{
    /// <summary>Empreinte des donnees d'entree d'une semelle filante.</summary>
    public static class StripFootingDesignFingerprint
    {
        public static string Compute(StripFootingData footing,
                                     StripFootingDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("B", footing.WidthMm)
                       .Add("h", footing.ThicknessMm)
                       .Add("L", footing.LengthMm)
                       .Add("tw", footing.WallThicknessMm);

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa)
                       .Add("gamma", settings.ConcreteUnitWeightKnM3);

            fingerprint.Add("sigma", settings.AllowableBearingPressureKpa)
                       .Add("delta", settings.InterfaceFrictionAngleDeg)
                       .Add("ca", settings.InterfaceAdhesionKpa);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("soil", settings.CastDirectlyAgainstSoil)
                       .Add("cover", settings.CoverMm);

            fingerprint.Add("N", settings.AxialLoadKnPerM)
                       .Add("M", settings.MomentKnmPerM)
                       .Add("H", settings.HorizontalLoadKnPerM)
                       .Add("pp", settings.IncludeSelfWeight);

            fingerprint.Add("autoPhi", settings.AutoMeshDiameter)
                       .Add("phi", settings.ForcedMeshDiameterMm)
                       .Add("top", settings.TopMesh)
                       .Add("starters", settings.Starters)
                       .Add("sSpacing", settings.StarterSpacingMm)
                       .Add("sPhi", settings.StarterDiameterMm);

            return fingerprint.Compute();
        }
    }
}
