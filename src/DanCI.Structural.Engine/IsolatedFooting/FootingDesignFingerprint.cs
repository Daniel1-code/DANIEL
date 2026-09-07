using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.IsolatedFooting
{
    /// <summary>
    /// Empreinte des donnees d'une semelle isolee : geometrie, materiaux, sol,
    /// sollicitations et preferences de ferraillage. Deux empreintes identiques
    /// designent un dimensionnement identique.
    /// </summary>
    public static class FootingDesignFingerprint
    {
        public static string Compute(FootingData footing, FootingDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("B", footing.WidthXMm)
                       .Add("L", footing.WidthYMm)
                       .Add("h", footing.ThicknessMm)
                       .Add("c1", footing.ColumnWidthXMm)
                       .Add("c2", footing.ColumnWidthYMm);

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

            fingerprint.Add("N", settings.AxialLoadKn)
                       .Add("Mx", settings.MomentAboutXKnm)
                       .Add("My", settings.MomentAboutYKnm)
                       .Add("Vx", settings.ShearXKn)
                       .Add("Vy", settings.ShearYKn)
                       .Add("pp", settings.IncludeSelfWeight);

            fingerprint.Add("autoPhi", settings.AutoMeshDiameter)
                       .Add("phi", settings.ForcedMeshDiameterMm)
                       .Add("top", settings.TopMesh)
                       .Add("starters", settings.StarterBarCount)
                       .Add("phiStarter", settings.StarterBarDiameterMm);

            return fingerprint.Compute();
        }
    }
}
