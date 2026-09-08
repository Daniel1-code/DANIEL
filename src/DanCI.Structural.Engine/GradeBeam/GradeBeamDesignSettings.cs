using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.EC8;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.GradeBeam
{
    /// <summary>Reglages de dimensionnement d'une longrine.</summary>
    public sealed class GradeBeamDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }
        public double ConcreteUnitWeightKnM3 { get; set; }

        // --- Appui et charges ---
        /// <summary>Ce sur quoi la longrine repose entre ses appuis.</summary>
        public GradeBeamBedding Bedding { get; set; }
        /// <summary>Charge lineique de calcul apportee par le mur porte (kN/m, ELU).</summary>
        public double WallLoadKnPerM { get; set; }
        public bool IncludeSelfWeight { get; set; }
        /// <summary>Contrainte admissible du sol, si la longrine repose dessus (kPa).</summary>
        public double AllowableBearingPressureKpa { get; set; }

        // --- Effort de liaison ---
        /// <summary>Le projet releve-t-il d'un dimensionnement sismique ?</summary>
        public bool SeismicDesign { get; set; }
        public GroundType Ground { get; set; }
        /// <summary>alpha = a_g / g.</summary>
        public double GroundAccelerationRatio { get; set; }
        /// <summary>Coefficient de sol S.</summary>
        public double SoilFactor { get; set; }
        /// <summary>N_Ed moyen des elements verticaux relies (kN).</summary>
        public double MeanColumnAxialLoadKn { get; set; }
        /// <summary>Nombre de niveaux du batiment, qui fixe la hauteur minimale.</summary>
        public int StoreyCount { get; set; }
        /// <summary>
        /// Effort de liaison impose par l'utilisateur (kN), hors dimensionnement sismique.
        /// L'EN 1992 seul n'en impose aucun.
        /// </summary>
        public double ManualTieForceKn { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public bool CastDirectlyAgainstSoil { get; set; }
        public double CoverMm { get; set; }

        // --- Ferraillage ---
        public bool AutoLongitudinalDiameter { get; set; }
        public double ForcedLongitudinalDiameterMm { get; set; }
        public int MaxLayers { get; set; }
        public bool AutoStirrupDiameter { get; set; }
        public double ForcedStirrupDiameterMm { get; set; }
        public int StirrupLegs { get; set; }
        public double AggregateSizeMm { get; set; }

        public GradeBeamDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;
            ConcreteUnitWeightKnM3 = 25.0;

            Bedding = GradeBeamBedding.Suspended;
            WallLoadKnPerM = 40.0;
            IncludeSelfWeight = true;
            AllowableBearingPressureKpa = 150.0;

            SeismicDesign = false;
            Ground = GroundType.C;
            GroundAccelerationRatio = 0.15;
            SoilFactor = 1.15;
            MeanColumnAxialLoadKn = 800.0;
            StoreyCount = 3;
            ManualTieForceKn = 0.0;

            AutoCover = true;
            Exposure = ExposureClass.XC2;
            DesignLife = DesignWorkingLife.Years50;
            CastDirectlyAgainstSoil = false;
            CoverMm = 40.0;

            AutoLongitudinalDiameter = true;
            ForcedLongitudinalDiameterMm = 16.0;
            MaxLayers = 2;
            AutoStirrupDiameter = true;
            ForcedStirrupDiameterMm = 8.0;
            StirrupLegs = 2;
            AggregateSizeMm = 20.0;
        }

        public GradeBeamDesignSettings Clone()
        {
            return (GradeBeamDesignSettings)MemberwiseClone();
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (WallLoadKnPerM < 0)
                errors.Add("La charge lineique ne peut pas etre negative.");
            if (SeismicDesign)
            {
                if (GroundAccelerationRatio <= 0 || GroundAccelerationRatio > 1)
                    errors.Add("Le rapport a_g/g doit etre compris entre 0 et 1.");
                if (SoilFactor < 1.0 || SoilFactor > 2.0)
                    errors.Add("Le coefficient de sol S doit etre compris entre 1,0 et 2,0.");
                if (MeanColumnAxialLoadKn <= 0)
                    errors.Add("Renseignez l'effort normal moyen des elements relies.");
            }
            if (StoreyCount < 1 || StoreyCount > 60)
                errors.Add("Le nombre de niveaux doit etre compris entre 1 et 60.");
            if (ManualTieForceKn < 0)
                errors.Add("L'effort de liaison impose ne peut pas etre negatif.");
            if (!AutoCover && (CoverMm < 20 || CoverMm > 150))
                errors.Add("L'enrobage impose doit etre compris entre 20 et 150 mm.");
            if (StirrupLegs < 2 || StirrupLegs > 6)
                errors.Add("Le nombre de brins par cadre doit etre compris entre 2 et 6.");
            if (MaxLayers < 1 || MaxLayers > 3)
                errors.Add("Le nombre de lits doit etre compris entre 1 et 3.");
            return errors;
        }
    }
}
