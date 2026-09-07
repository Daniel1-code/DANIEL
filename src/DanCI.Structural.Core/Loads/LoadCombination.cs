using System.Collections.Generic;

namespace DanCI.Structural.Core.Loads
{
    /// <summary>Situations de projet de l'EN 1990.</summary>
    public enum DesignSituation
    {
        UltimateFundamental,
        UltimateAccidental,
        UltimateSeismic,
        ServiceabilityCharacteristic,
        ServiceabilityFrequent,
        ServiceabilityQuasiPermanent
    }

    /// <summary>Sollicitations en un point donne le long d'un element.</summary>
    public sealed class ForceStation
    {
        /// <summary>Abscisse le long de l'element (mm). 0 pour un element ponctuel.</summary>
        public double PositionMm { get; set; }

        public InternalForces Forces { get; set; }

        public ForceStation()
        {
        }

        public ForceStation(double positionMm, InternalForces forces)
        {
            PositionMm = positionMm;
            Forces = forces;
        }
    }

    /// <summary>
    /// Une combinaison d'actions : un identifiant, une situation de projet et les
    /// sollicitations correspondantes le long de l'element.
    /// </summary>
    public sealed class LoadCombination
    {
        public string Id { get; set; }

        public DesignSituation Situation { get; set; }

        public List<ForceStation> Stations { get; private set; }

        public LoadCombination()
        {
            Id = "COMB-001";
            Situation = DesignSituation.UltimateFundamental;
            Stations = new List<ForceStation>();
        }

        public LoadCombination(string id, DesignSituation situation) : this()
        {
            Id = id;
            Situation = situation;
        }

        /// <summary>Combinaison a une seule station, cas courant d'un poteau.</summary>
        public static LoadCombination Single(string id, DesignSituation situation, InternalForces forces)
        {
            var combination = new LoadCombination(id, situation);
            combination.Stations.Add(new ForceStation(0.0, forces));
            return combination;
        }

        public bool IsUltimate
        {
            get
            {
                return Situation == DesignSituation.UltimateFundamental
                       || Situation == DesignSituation.UltimateAccidental
                       || Situation == DesignSituation.UltimateSeismic;
            }
        }
    }
}
