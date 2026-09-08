using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>
    /// Donnees d'une semelle filante sous voile, independantes de Revit.
    /// Dimensions en millimetres.
    ///
    /// Le dimensionnement se mene sur un **metre courant** de semelle : les sections sont
    /// exprimees par metre, comme sur un plan de ferraillage.
    /// </summary>
    public sealed class StripFootingData
    {
        /// <summary>Longueur de la tranche de calcul (mm).</summary>
        public const double RunLengthMm = 1000.0;

        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Largeur de la semelle B, transversalement (mm).</summary>
        public double WidthMm { get; set; }

        /// <summary>Epaisseur de la semelle h (mm).</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Longueur totale de la semelle (mm), pour les quantitatifs.</summary>
        public double LengthMm { get; set; }

        /// <summary>Epaisseur du voile porte (mm).</summary>
        public double WallThicknessMm { get; set; }

        public List<string> Remarks { get; private set; }

        public StripFootingData()
        {
            Remarks = new List<string>();
            Name = "Semelle filante";
            Mark = "SF";
            WidthMm = 900.0;
            ThicknessMm = 400.0;
            WallThicknessMm = 200.0;
        }

        /// <summary>Debord depuis le nu du voile, de chaque cote (mm).</summary>
        public double OverhangMm
        {
            get { return (WidthMm - WallThicknessMm) / 2.0; }
        }

        /// <summary>Volume de beton total (mm3).</summary>
        public double VolumeMm3 { get { return WidthMm * ThicknessMm * LengthMm; } }

        /// <summary>Volume de beton d'un metre courant (mm3).</summary>
        public double VolumePerRunMm3 { get { return WidthMm * ThicknessMm * RunLengthMm; } }

        /// <summary>
        /// La semelle est-elle rigide ? Au-dela d'un debord d'environ deux fois
        /// l'epaisseur, l'hypothese de repartition lineaire des contraintes et le modele
        /// de console encastree cessent d'etre representatifs.
        /// </summary>
        public bool IsRigid
        {
            get { return ThicknessMm > 0 && OverhangMm <= 2.0 * ThicknessMm; }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format("{0:0} x {1:0} mm sous voile de {2:0} mm",
                                     WidthMm, ThicknessMm, WallThicknessMm);
            }
        }
    }
}
