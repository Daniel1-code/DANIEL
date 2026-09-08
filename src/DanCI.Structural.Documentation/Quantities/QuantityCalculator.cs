using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Units;
using DanCI.Structural.Eurocodes.EC2;
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
            double volume = column != null
                ? UnitConverter.Mm3ToM3(column.GrossAreaMm2 * column.HeightMm) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'une poutre.</summary>
        public static SteelQuantities Compute(BeamData beam, ReinforcementPlan plan)
        {
            double volume = beam != null
                ? UnitConverter.Mm3ToM3(beam.WebWidthMm * beam.HeightMm * beam.SpanMm) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'une semelle isolee.</summary>
        public static SteelQuantities Compute(FootingData footing, ReinforcementPlan plan)
        {
            double volume = footing != null ? UnitConverter.Mm3ToM3(footing.VolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'un panneau de dalle.</summary>
        public static SteelQuantities Compute(SlabData slab, ReinforcementPlan plan)
        {
            double volume = slab != null ? UnitConverter.Mm3ToM3(slab.VolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'un voile.</summary>
        public static SteelQuantities Compute(WallData wall, ReinforcementPlan plan)
        {
            double volume = wall != null ? UnitConverter.Mm3ToM3(wall.VolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'une semelle filante.</summary>
        public static SteelQuantities Compute(StripFootingData footing, ReinforcementPlan plan)
        {
            double volume = footing != null ? UnitConverter.Mm3ToM3(footing.VolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>Quantitatif d'une longrine.</summary>
        public static SteelQuantities Compute(GradeBeamData beam, ReinforcementPlan plan)
        {
            double volume = beam != null ? UnitConverter.Mm3ToM3(beam.VolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>
        /// Quantitatif d'une volee d'escalier. Le volume compte la paillasse SUIVANT SA
        /// PENTE et les marches : c'est le meme oubli que pour le poids propre, et il
        /// couterait ici du beton non commande.
        /// </summary>
        public static SteelQuantities Compute(StairData stair, ReinforcementPlan plan)
        {
            double volume = stair != null ? UnitConverter.Mm3ToM3(stair.FlightVolumeMm3) : 0.0;
            return Compute(volume, plan);
        }

        /// <summary>
        /// Quantitatif d'un plan de ferraillage quelconque, pour un volume de beton donne.
        /// </summary>
        public static SteelQuantities Compute(double concreteVolumeM3, ReinforcementPlan plan)
        {
            var quantities = new SteelQuantities { ConcreteVolumeM3 = concreteVolumeM3 };
            if (plan == null) return quantities;

            foreach (RebarGroup group in plan.Groups)
            {
                int count = group.BarCount;
                if (count <= 0) continue;

                double hookAllowance = group.WithHooks
                    ? 2.0 * HookReturnInDiameters * group.DiameterMm
                    : 0.0;

                // LONGUEUR DE COUPE, pas developpe d'angle a angle. Une barre pliee coupe
                // le coin par un arc : sommer les segments de la polyligne surestime la
                // longueur de 8,6 mm par pli en HA8 et de 34,3 mm en HA20. Le quantitatif
                // l'a fait jusqu'a la 3.10.0 incluse, et commandait donc un peu trop
                // d'acier — 2,3 % sur un cadre courant. Voir EN 1992-1-1 art. 8.3.
                double cutLength = BarBending.CutLength(group.BarLengthMm,
                    group.BendAnglesDegrees().ToArray(), group.DiameterMm,
                    hookAllowance).CutLengthMm;
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
