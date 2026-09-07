using System;
using System.Collections.Generic;
using System.Linq;

namespace DanCI.Structural.Documentation.Quantities
{
    /// <summary>Quantitatif d'acier d'un element : longueurs, poids et ratio.</summary>
    public sealed class SteelQuantities
    {
        /// <summary>Masse volumique de l'acier d'armature (kg/m3).</summary>
        public const double SteelDensityKgPerM3 = 7850.0;

        public double LongitudinalLengthM { get; set; }
        public double LongitudinalMassKg { get; set; }
        public int LongitudinalBarCount { get; set; }
        /// <summary>Longueur de coupe d'une barre longitudinale (mm).</summary>
        public double LongitudinalCutLengthMm { get; set; }

        public double StirrupLengthM { get; set; }
        public double StirrupMassKg { get; set; }
        public int StirrupCount { get; set; }
        /// <summary>Longueur developpee d'un cadre, crochets compris (mm).</summary>
        public double StirrupCutLengthMm { get; set; }

        public double CrossTieLengthM { get; set; }
        public double CrossTieMassKg { get; set; }
        public int CrossTieCount { get; set; }

        public double ConcreteVolumeM3 { get; set; }

        /// <summary>Masse d'acier par diametre nominal (mm -> kg).</summary>
        public Dictionary<double, double> MassByDiameterKg { get; private set; }

        public SteelQuantities()
        {
            MassByDiameterKg = new Dictionary<double, double>();
        }

        public double TotalMassKg
        {
            get { return LongitudinalMassKg + StirrupMassKg + CrossTieMassKg; }
        }

        public double TotalLengthM
        {
            get { return LongitudinalLengthM + StirrupLengthM + CrossTieLengthM; }
        }

        /// <summary>Ratio usuel en chiffrage : kilogrammes d'acier par metre cube de beton.</summary>
        public double RatioKgPerM3
        {
            get { return ConcreteVolumeM3 > 0 ? TotalMassKg / ConcreteVolumeM3 : 0.0; }
        }

        /// <summary>Masse lineique d'une barre (kg/m) a partir de son diametre nominal.</summary>
        public static double MassPerMetre(double diameterMm)
        {
            double areaMm2 = Math.PI * diameterMm * diameterMm / 4.0;
            return areaMm2 * SteelDensityKgPerM3 * 1e-6;
        }

        public void AddMass(double diameterMm, double massKg)
        {
            double key = Math.Round(diameterMm, 1);
            double current;
            MassByDiameterKg.TryGetValue(key, out current);
            MassByDiameterKg[key] = current + massKg;
        }

        public void Merge(SteelQuantities other)
        {
            LongitudinalLengthM += other.LongitudinalLengthM;
            LongitudinalMassKg += other.LongitudinalMassKg;
            LongitudinalBarCount += other.LongitudinalBarCount;
            StirrupLengthM += other.StirrupLengthM;
            StirrupMassKg += other.StirrupMassKg;
            StirrupCount += other.StirrupCount;
            CrossTieLengthM += other.CrossTieLengthM;
            CrossTieMassKg += other.CrossTieMassKg;
            CrossTieCount += other.CrossTieCount;
            ConcreteVolumeM3 += other.ConcreteVolumeM3;
            foreach (var entry in other.MassByDiameterKg) AddMass(entry.Key, entry.Value);
        }

        public string DiameterBreakdown()
        {
            if (MassByDiameterKg.Count == 0) return "-";
            return string.Join(" + ", MassByDiameterKg
                .OrderBy(e => e.Key)
                .Select(e => string.Format("HA{0:0} : {1:0.0} kg", e.Key, e.Value)));
        }
    }
}
