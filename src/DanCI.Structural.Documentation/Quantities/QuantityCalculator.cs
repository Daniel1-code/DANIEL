using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Documentation.Quantities
{
    /// <summary>
    /// Chiffre l'acier reellement pose, a partir du plan de ferraillage lui-meme : le poids
    /// annonce correspond donc exactement aux barres modelisees. Les longueurs developpees
    /// incluent les retours de crochets, comme sur un plan de ferraillage.
    /// </summary>
    public static class QuantityCalculator
    {
        /// <summary>Retour droit d'un crochet a 135 degres, exprime en diametres.</summary>
        private const double HookReturnInDiameters = 10.0;

        public static SteelQuantities Compute(ColumnData column, ReinforcementPlan plan)
        {
            var quantities = new SteelQuantities();
            if (column != null)
            {
                quantities.ConcreteVolumeM3 =
                    UnitConverter.Mm3ToM3(column.GrossAreaMm2 * column.HeightMm);
            }
            if (plan == null) return quantities;

            foreach (RebarGroup group in plan.Groups)
            {
                int count = group.BarCount;
                if (count <= 0) continue;

                double hookAllowance = group.WithHooks
                    ? 2.0 * HookReturnInDiameters * group.DiameterMm
                    : 0.0;
                double cutLength = group.BarLengthMm + hookAllowance;
                double totalLengthM = count * cutLength / 1000.0;
                double mass = totalLengthM * SteelQuantities.MassPerMetre(group.DiameterMm);

                switch (group.Kind)
                {
                    case RebarKind.Longitudinal:
                        quantities.LongitudinalBarCount += count;
                        quantities.LongitudinalLengthM += totalLengthM;
                        quantities.LongitudinalMassKg += mass;
                        quantities.LongitudinalCutLengthMm = cutLength;
                        break;
                    case RebarKind.Stirrup:
                        quantities.StirrupCount += count;
                        quantities.StirrupLengthM += totalLengthM;
                        quantities.StirrupMassKg += mass;
                        quantities.StirrupCutLengthMm = cutLength;
                        break;
                    default:
                        quantities.CrossTieCount += count;
                        quantities.CrossTieLengthM += totalLengthM;
                        quantities.CrossTieMassKg += mass;
                        break;
                }

                quantities.AddMass(group.DiameterMm, mass);
            }

            return quantities;
        }
    }
}
