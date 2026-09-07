using System;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Reinforcement.Optimization
{
    /// <summary>Un choix de barres pour une section d'acier requise.</summary>
    public sealed class BarSelection
    {
        public double DiameterMm { get; set; }

        /// <summary>Nombre total de barres.</summary>
        public int Count { get; set; }

        /// <summary>Nombre de lits superposes.</summary>
        public int Layers { get; set; }

        /// <summary>Nombre de barres du lit le plus garni.</summary>
        public int BarsPerLayer { get; set; }

        public double AreaMm2
        {
            get { return Count * UnitConverter.BarArea(DiameterMm); }
        }

        public string Label
        {
            get
            {
                if (Count <= 0) return "-";
                string text = string.Format("{0} HA{1:0}", Count, DiameterMm);
                return Layers > 1 ? text + string.Format(" ({0} lits)", Layers) : text;
            }
        }

        public static BarSelection None()
        {
            return new BarSelection { DiameterMm = 0, Count = 0, Layers = 0, BarsPerLayer = 0 };
        }
    }
}
