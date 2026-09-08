using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.Wall
{
    /// <summary>Empreinte des donnees d'entree d'un voile.</summary>
    public static class WallDesignFingerprint
    {
        public static string Compute(WallData wall, WallDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("t", wall.ThicknessMm)
                       .Add("L", wall.LengthMm)
                       .Add("h", wall.ClearHeightMm);

            fingerprint.Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa);

            fingerprint.Add("restraint", settings.Restraint.ToString())
                       .Add("b", settings.RestraintSpacingMm)
                       .Add("phi_ef", settings.CreepCoefficient);

            fingerprint.Add("N", settings.AxialLoadKnPerM)
                       .Add("Mop", settings.OutOfPlaneMomentKnmPerM)
                       .Add("Vip", settings.InPlaneShearKn)
                       .Add("Mip", settings.InPlaneMomentKnm);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("cover", settings.CoverMm);

            fingerprint.Add("autoV", settings.AutoVerticalDiameter)
                       .Add("phiV", settings.ForcedVerticalDiameterMm)
                       .Add("autoH", settings.AutoHorizontalDiameter)
                       .Add("phiH", settings.ForcedHorizontalDiameterMm)
                       .Add("edge", settings.EdgeBars)
                       .Add("edgeN", settings.EdgeBarCount)
                       .Add("edgePhi", settings.EdgeBarDiameterMm);

            return fingerprint.Compute();
        }
    }
}
