using System;
using System.Collections.Generic;
using System.Linq;

namespace DanCI.BOQ.Core.Reference
{
    /// <summary>
    /// Masse lineique des barres HA (acier de densite 7 850 kg/m³), table commerciale
    /// usuelle. Sert au metre des armatures modelisees : masse = longueur x masse lineique.
    /// Un diametre absent de la table n'est PAS interpole : la quantite est signalee.
    /// </summary>
    public static class RebarMass
    {
        private static readonly IReadOnlyDictionary<int, decimal> KgPerMeter = new Dictionary<int, decimal>
        {
            { 6, 0.222m }, { 8, 0.395m }, { 10, 0.617m }, { 12, 0.888m }, { 14, 1.208m },
            { 16, 1.578m }, { 20, 2.466m }, { 25, 3.853m }, { 32, 6.313m }, { 40, 9.865m }
        };

        public static IEnumerable<int> Diameters => KgPerMeter.Keys.OrderBy(d => d);

        public static bool TryGet(int diameterMm, out decimal kgPerMeter) =>
            KgPerMeter.TryGetValue(diameterMm, out kgPerMeter);

        public static decimal Get(int diameterMm)
        {
            if (TryGet(diameterMm, out decimal value))
            {
                return value;
            }
            throw new ArgumentOutOfRangeException(nameof(diameterMm), diameterMm,
                "Diamètre HA" + diameterMm + " absent de la table (HA6 à HA40).");
        }
    }
}
