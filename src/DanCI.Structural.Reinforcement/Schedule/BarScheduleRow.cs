using System;
using System.Collections.Generic;
using System.Globalization;

namespace DanCI.Structural.Reinforcement.Schedule
{
    /// <summary>
    /// Une ligne du carnet de ferraillage : UN TYPE de barre, pas une barre.
    ///
    /// C'est la distinction qui fait un carnet. Un repere designe une forme faconnee — un
    /// diametre, une suite de longueurs, une suite de plis — et toutes les barres
    /// identiques du projet le partagent, quel que soit l'element qui les porte. Donner un
    /// repere different a deux barres identiques oblige le facconnier a produire deux fois
    /// la meme chose.
    /// </summary>
    public sealed class BarScheduleRow
    {
        /// <summary>Repere de forme, unique dans le projet.</summary>
        public string Mark { get; set; }

        public double DiameterMm { get; set; }

        /// <summary>Longueurs des tronçons droits, dans l'ordre (mm).</summary>
        public List<double> SegmentLengthsMm { get; private set; }

        /// <summary>Angles de deviation des plis, dans l'ordre (degres).</summary>
        public List<double> BendAnglesDegrees { get; private set; }

        /// <summary>Diametre de mandrin retenu (mm), EN 1992-1-1 tableau 8.1N.</summary>
        public double MandrelDiameterMm { get; set; }

        /// <summary>Developpe d'angle a angle (mm).</summary>
        public double PolylineLengthMm { get; set; }

        /// <summary>Deduction totale des plis (mm).</summary>
        public double BendDeductionMm { get; set; }

        /// <summary>Retours de crochets (mm).</summary>
        public double HookAllowanceMm { get; set; }

        /// <summary>Longueur de coupe d'UNE barre (mm).</summary>
        public double CutLengthMm { get; set; }

        /// <summary>Nombre de barres de ce type dans le projet.</summary>
        public int Count { get; set; }

        /// <summary>Elements qui portent ces barres, et combien chacun en porte.</summary>
        public List<BarScheduleUse> Uses { get; private set; }

        public bool IsClosedLoop { get; set; }

        /// <summary>Un pli sort du domaine du modele : sa deduction n'est pas comptee.</summary>
        public bool HasBendBeyondModel { get; set; }

        public BarScheduleRow()
        {
            SegmentLengthsMm = new List<double>();
            BendAnglesDegrees = new List<double>();
            Uses = new List<BarScheduleUse>();
        }

        /// <summary>Longueur totale de ce type dans le projet (m).</summary>
        public double TotalLengthM { get { return Count * CutLengthMm / 1000.0; } }

        /// <summary>Masse lineique d'une barre ronde en acier, 7 850 kg/m3.</summary>
        public static double MassPerMetreKgM(double diameterMm)
        {
            double areaMm2 = Math.PI * diameterMm * diameterMm / 4.0;
            return areaMm2 * 1e-6 * 7850.0;
        }

        public double TotalMassKg { get { return TotalLengthM * MassPerMetreKgM(DiameterMm); } }

        /// <summary>Forme en clair : "droite", "L", "U", "cadre", ou le nombre de plis.</summary>
        public string ShapeLabel
        {
            get
            {
                if (IsClosedLoop) return "cadre ferme";
                switch (BendAnglesDegrees.Count)
                {
                    case 0: return "droite";
                    case 1: return "pliee en L";
                    case 2: return "pliee en U ou en Z";
                    default:
                        return string.Format(CultureInfo.CurrentCulture, "{0} plis",
                                             BendAnglesDegrees.Count);
                }
            }
        }

        /// <summary>Cotes de faconnage, dans l'ordre : "1250 x 300 x 1250".</summary>
        public string DimensionsLabel
        {
            get
            {
                if (SegmentLengthsMm.Count == 0) return "-";
                var parts = new List<string>();
                foreach (double length in SegmentLengthsMm)
                {
                    parts.Add(length.ToString("0", CultureInfo.CurrentCulture));
                }
                return string.Join(" x ", parts);
            }
        }
    }

    /// <summary>Ou un type de barre est employe, et en quelle quantite.</summary>
    public sealed class BarScheduleUse
    {
        public string ElementName { get; set; }
        public string GroupLabel { get; set; }
        public int Count { get; set; }
    }
}
