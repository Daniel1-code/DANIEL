using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Results;

namespace DanCI.Structural.Engine.Column
{
    /// <summary>
    /// Empreinte des donnees d'un poteau : geometrie, materiaux, efforts et preferences de
    /// ferraillage. Deux calculs de meme empreinte sont identiques ; une empreinte differente
    /// signifie que l'element doit etre recalcule.
    /// </summary>
    public static class ColumnDesignFingerprint
    {
        public static string Compute(ColumnData column, ColumnDesignSettings settings)
        {
            var fingerprint = new DesignFingerprint();

            fingerprint.Add("shape", column.Shape.ToString())
                       .Add("b", column.WidthMm)
                       .Add("h", column.DepthMm)
                       .Add("d", column.DiameterMm)
                       .Add("H", column.HeightMm);

            fingerprint.Add("code", settings.DetailingCode.ToString())
                       .Add("gen", settings.Generation.ToString())
                       .Add("na", settings.NationalAnnex.ToString())
                       .Add("fck", settings.ConcreteStrengthMPa)
                       .Add("fyk", settings.SteelStrengthMPa);

            fingerprint.Add("autoCover", settings.AutoCover)
                       .Add("exposure", settings.Exposure.ToString())
                       .Add("life", settings.DesignLife.ToString())
                       .Add("qc", settings.SpecialQualityControl)
                       .Add("cover", settings.CoverMm)
                       .Add("dg", settings.AggregateSizeMm);

            fingerprint.Add("N", settings.AxialLoadKn)
                       .Add("Mx", settings.MomentAboutXKnm)
                       .Add("My", settings.MomentAboutYKnm)
                       .Add("verify", settings.VerifyCapacity)
                       .Add("l0", settings.BucklingFactor)
                       .Add("creep", settings.CreepCoefficient);

            fingerprint.Add("rho", settings.TargetRatioPercent)
                       .Add("autoPhi", settings.AutoLongitudinalDiameter)
                       .Add("phi", settings.ForcedLongitudinalDiameterMm)
                       .Add("autoN", settings.AutoBarCount)
                       .Add("nx", settings.ForcedBarsAlongX)
                       .Add("ny", settings.ForcedBarsAlongY)
                       .Add("nc", settings.ForcedCircularBarCount);

            fingerprint.Add("autoT", settings.AutoTransverse)
                       .Add("phit", settings.ForcedStirrupDiameterMm)
                       .Add("st", settings.ForcedSpacingMm)
                       .Add("zones", settings.UseCriticalZones)
                       .Add("ties", settings.AddCrossTies)
                       .Add("seismic", settings.Seismic);

            fingerprint.Add("first", settings.FirstStirrupOffsetMm)
                       .Add("bottom", settings.BottomOffsetMm)
                       .Add("top", settings.TopExtensionMm);

            return fingerprint.Compute();
        }
    }
}
