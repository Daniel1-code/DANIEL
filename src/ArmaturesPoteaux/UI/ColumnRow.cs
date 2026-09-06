using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.UI
{
    /// <summary>Une ligne du tableau de resultats.</summary>
    public class ColumnRow
    {
        public DesignResult Result { get; private set; }

        public ColumnRow(DesignResult result)
        {
            Result = result;
        }

        public string Poteau { get { return Result.Geometry.HostName; } }

        public string Section { get { return Result.Geometry.SectionLabel; } }

        public string Hauteur { get { return string.Format("{0:0}", Result.Geometry.HeightMm); } }

        public string Longitudinales { get { return Result.LongitudinalLabel; } }

        public string Transversales { get { return Result.TransverseLabel; } }

        public string Taux { get { return string.Format("{0:0.00} %", Result.RatioPercent); } }

        public string Recouvrement { get { return string.Format("{0:0}", Result.LapLengthMm); } }

        public string Etat { get { return Result.Status; } }
    }
}
