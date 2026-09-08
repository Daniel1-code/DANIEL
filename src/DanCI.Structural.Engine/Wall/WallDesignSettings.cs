using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.Wall
{
    /// <summary>Reglages de dimensionnement d'un voile en beton arme.</summary>
    public sealed class WallDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }

        // --- Maintien et flambement ---
        public WallRestraint Restraint { get; set; }
        /// <summary>Distance entre rives verticales maintenues b (mm) ; 0 = longueur du voile.</summary>
        public double RestraintSpacingMm { get; set; }
        /// <summary>Coefficient de fluage effectif phi_ef.</summary>
        public double CreepCoefficient { get; set; }

        // --- Sollicitations, sur une bande verticale de 1 metre ---
        /// <summary>Effort normal de compression par metre de longueur (kN/m).</summary>
        public double AxialLoadKnPerM { get; set; }
        /// <summary>Moment hors plan par metre, en tete de voile (kN.m/m).</summary>
        public double OutOfPlaneMomentKnmPerM { get; set; }

        // --- Sollicitations de contreventement, sur le voile entier ---
        /// <summary>Effort tranchant dans le plan, sur toute la longueur (kN).</summary>
        public double InPlaneShearKn { get; set; }
        /// <summary>Moment de flexion dans le plan, en pied (kN.m).</summary>
        public double InPlaneMomentKnm { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public double CoverMm { get; set; }

        // --- Ferraillage ---
        public bool AutoVerticalDiameter { get; set; }
        public double ForcedVerticalDiameterMm { get; set; }
        public bool AutoHorizontalDiameter { get; set; }
        public double ForcedHorizontalDiameterMm { get; set; }
        /// <summary>Poser des barres de rive aux extremites.</summary>
        public bool EdgeBars { get; set; }
        public int EdgeBarCount { get; set; }
        public double EdgeBarDiameterMm { get; set; }

        public WallDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;

            Restraint = WallRestraint.TopAndBottom;
            RestraintSpacingMm = 0.0;
            CreepCoefficient = 2.0;

            AxialLoadKnPerM = 400.0;
            OutOfPlaneMomentKnmPerM = 0.0;
            InPlaneShearKn = 0.0;
            InPlaneMomentKnm = 0.0;

            AutoCover = true;
            Exposure = ExposureClass.XC1;
            DesignLife = DesignWorkingLife.Years50;
            CoverMm = 25.0;

            AutoVerticalDiameter = true;
            ForcedVerticalDiameterMm = 10.0;
            AutoHorizontalDiameter = true;
            ForcedHorizontalDiameterMm = 8.0;
            EdgeBars = false;
            EdgeBarCount = 4;
            EdgeBarDiameterMm = 16.0;
        }

        public WallDesignSettings Clone()
        {
            return (WallDesignSettings)MemberwiseClone();
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (AxialLoadKnPerM < 0)
                errors.Add("L'effort normal ne peut pas etre negatif : le moteur ne traite que " +
                           "les voiles comprimes.");
            if (CreepCoefficient < 0 || CreepCoefficient > 4)
                errors.Add("Le coefficient de fluage doit etre compris entre 0 et 4.");
            if (RestraintSpacingMm < 0)
                errors.Add("La distance entre rives maintenues ne peut pas etre negative.");
            if (!AutoCover && (CoverMm < 10 || CoverMm > 100))
                errors.Add("L'enrobage impose d'un voile doit etre compris entre 10 et 100 mm.");
            if (EdgeBars && (EdgeBarCount < 2 || EdgeBarCount > 20))
                errors.Add("Le nombre de barres de rive doit etre compris entre 2 et 20.");
            return errors;
        }
    }
}
