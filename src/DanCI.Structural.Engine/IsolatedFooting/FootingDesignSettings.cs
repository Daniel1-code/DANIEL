using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.IsolatedFooting
{
    /// <summary>Reglages de dimensionnement d'une semelle isolee.</summary>
    public sealed class FootingDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }
        /// <summary>Poids volumique du beton arme (kN/m3).</summary>
        public double ConcreteUnitWeightKnM3 { get; set; }

        // --- Sol ---
        public double AllowableBearingPressureKpa { get; set; }
        public double InterfaceFrictionAngleDeg { get; set; }
        public double InterfaceAdhesionKpa { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        /// <summary>Beton coule directement contre le sol (75 mm) ou sur beton de proprete (40 mm).</summary>
        public bool CastDirectlyAgainstSoil { get; set; }
        public double CoverMm { get; set; }

        // --- Sollicitations en pied de poteau ---
        public double AxialLoadKn { get; set; }
        /// <summary>Moment autour de X : il excentre la charge suivant Y (kN.m).</summary>
        public double MomentAboutXKnm { get; set; }
        /// <summary>Moment autour de Y : il excentre la charge suivant X (kN.m).</summary>
        public double MomentAboutYKnm { get; set; }
        public double ShearXKn { get; set; }
        public double ShearYKn { get; set; }

        /// <summary>Inclure le poids propre de la semelle dans la verification geotechnique.</summary>
        public bool IncludeSelfWeight { get; set; }

        // --- Ferraillage ---
        public bool AutoMeshDiameter { get; set; }
        public double ForcedMeshDiameterMm { get; set; }
        /// <summary>Poser une nappe superieure.</summary>
        public bool TopMesh { get; set; }
        public int StarterBarCount { get; set; }
        public double StarterBarDiameterMm { get; set; }

        public FootingDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;
            ConcreteUnitWeightKnM3 = 25.0;
            AllowableBearingPressureKpa = 250.0;
            InterfaceFrictionAngleDeg = 30.0;
            InterfaceAdhesionKpa = 0.0;
            AutoCover = true;
            Exposure = ExposureClass.XC2;
            DesignLife = DesignWorkingLife.Years50;
            CastDirectlyAgainstSoil = false;
            CoverMm = 50.0;
            AxialLoadKn = 1200.0;
            IncludeSelfWeight = true;
            AutoMeshDiameter = true;
            ForcedMeshDiameterMm = 12.0;
            TopMesh = false;
            StarterBarCount = 4;
            StarterBarDiameterMm = 16.0;
        }

        public FootingDesignSettings Clone()
        {
            return (FootingDesignSettings)MemberwiseClone();
        }

        public SoilProperties ToSoilProperties()
        {
            return new SoilProperties
            {
                AllowableBearingPressureKpa = AllowableBearingPressureKpa,
                InterfaceFrictionAngleDeg = InterfaceFrictionAngleDeg,
                InterfaceAdhesionKpa = InterfaceAdhesionKpa
            };
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (AllowableBearingPressureKpa <= 0)
                errors.Add("Renseignez la contrainte admissible du sol.");
            if (InterfaceFrictionAngleDeg < 0 || InterfaceFrictionAngleDeg > 45)
                errors.Add("L'angle de frottement d'interface doit etre compris entre 0 et 45 degres.");
            if (AxialLoadKn <= 0)
                errors.Add("La charge verticale doit etre superieure a 0.");
            if (!AutoCover && (CoverMm < 25 || CoverMm > 150))
                errors.Add("L'enrobage impose d'une semelle doit etre compris entre 25 et 150 mm.");
            if (StarterBarCount < 0 || StarterBarCount > 40)
                errors.Add("Le nombre d'attentes doit etre compris entre 0 et 40.");
            return errors;
        }
    }
}
