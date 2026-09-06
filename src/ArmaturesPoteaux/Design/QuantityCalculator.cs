using System;
using System.Collections.Generic;
using ArmaturesPoteaux.Core;

namespace ArmaturesPoteaux.Design
{
    /// <summary>
    /// Chiffre l'acier reellement pose : longueurs de coupe, nombre d'elements et poids.
    /// Les longueurs developpees incluent les retours de crochets, comme sur un plan
    /// de ferraillage, afin que le poids annonce soit celui commande au faconneur.
    /// </summary>
    public static class QuantityCalculator
    {
        /// <summary>Retour droit d'un crochet a 135 degres, exprime en diametres.</summary>
        private const double HookReturnInDiameters = 10.0;

        public static SteelQuantities Compute(ColumnGeometry g, DesignResult d)
        {
            var q = new SteelQuantities();
            if (!d.IsValid || d.BarDiameterMm <= 0) return q;

            q.ConcreteVolumeM3 = g.GrossAreaMm2 * g.HeightMm * 1e-9;

            // --- Barres longitudinales ---
            double barLengthMm = g.HeightMm + d.TopExtensionMm - d.BottomOffsetMm;
            q.LongitudinalCutLengthMm = barLengthMm;
            q.LongitudinalBarCount = d.TotalBars;
            q.LongitudinalLengthM = d.TotalBars * barLengthMm / 1000.0;
            q.LongitudinalMassKg = q.LongitudinalLengthM * SteelQuantities.MassPerMetre(d.BarDiameterMm);
            q.AddMass(d.BarDiameterMm, q.LongitudinalMassKg);

            // --- Cadres ---
            double hookReturn = 2.0 * HookReturnInDiameters * d.StirrupDiameterMm;
            double stirrupLengthMm;
            if (g.Kind == SectionKind.Circular)
            {
                stirrupLengthMm = 2.0 * Math.PI * RebarLayout.StirrupRadius(g, d) + hookReturn;
            }
            else
            {
                stirrupLengthMm = 4.0 * (RebarLayout.StirrupHalfX(g, d) + RebarLayout.StirrupHalfY(g, d))
                                  + hookReturn;
            }
            q.StirrupCutLengthMm = stirrupLengthMm;

            List<StirrupZone> zones = StirrupZones.Compute(g, d);
            int stirrupCount = 0;
            foreach (StirrupZone zone in zones) stirrupCount += zone.Count;
            q.StirrupCount = stirrupCount;
            q.StirrupLengthM = stirrupCount * stirrupLengthMm / 1000.0;
            q.StirrupMassKg = q.StirrupLengthM * SteelQuantities.MassPerMetre(d.StirrupDiameterMm);
            q.AddMass(d.StirrupDiameterMm, q.StirrupMassKg);

            // --- Epingles ---
            if (g.Kind == SectionKind.Rectangular)
            {
                double spanY = 2.0 * RebarLayout.BarHalfSpanY(g, d) + hookReturn;
                double spanX = 2.0 * RebarLayout.BarHalfSpanX(g, d) + hookReturn;
                int tiesPerLevel = RebarLayout.CrossTieXPositions(g, d).Count;
                int tiesPerLevelX = RebarLayout.CrossTieYPositions(g, d).Count;

                double crossTieLengthMm = tiesPerLevel * spanY + tiesPerLevelX * spanX;
                if (crossTieLengthMm > 0)
                {
                    q.CrossTieCount = (tiesPerLevel + tiesPerLevelX) * stirrupCount;
                    q.CrossTieLengthM = stirrupCount * crossTieLengthMm / 1000.0;
                    q.CrossTieMassKg = q.CrossTieLengthM
                                       * SteelQuantities.MassPerMetre(d.StirrupDiameterMm);
                    q.AddMass(d.StirrupDiameterMm, q.CrossTieMassKg);
                }
            }

            return q;
        }
    }
}
