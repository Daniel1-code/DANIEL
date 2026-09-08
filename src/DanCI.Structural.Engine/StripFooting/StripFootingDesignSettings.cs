using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.StripFooting
{
    /// <summary>Reglages de dimensionnement d'une semelle filante sous voile.</summary>
    public sealed class StripFootingDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }
        public double ConcreteUnitWeightKnM3 { get; set; }

        // --- Sol ---
        public double AllowableBearingPressureKpa { get; set; }
        public double InterfaceFrictionAngleDeg { get; set; }
        public double InterfaceAdhesionKpa { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public bool CastDirectlyAgainstSoil { get; set; }
        public double CoverMm { get; set; }

        // --- Sollicitations, par metre courant ---
        /// <summary>Charge verticale en pied de voile (kN/m).</summary>
        public double AxialLoadKnPerM { get; set; }
        /// <summary>Moment transversal en pied de voile, qui excentre la charge (kN.m/m).</summary>
        public double MomentKnmPerM { get; set; }
        /// <summary>Effort horizontal transversal (kN/m).</summary>
        public double HorizontalLoadKnPerM { get; set; }
        public bool IncludeSelfWeight { get; set; }

        // --- Ferraillage ---
        public bool AutoMeshDiameter { get; set; }
        public double ForcedMeshDiameterMm { get; set; }
        /// <summary>Poser une nappe transversale superieure.</summary>
        public bool TopMesh { get; set; }
        /// <summary>Poser les attentes du voile.</summary>
        public bool Starters { get; set; }
        public double StarterSpacingMm { get; set; }
        public double StarterDiameterMm { get; set; }

        public StripFootingDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;
            ConcreteUnitWeightKnM3 = 25.0;

            AllowableBearingPressureKpa = 200.0;
            InterfaceFrictionAngleDeg = 30.0;
            InterfaceAdhesionKpa = 0.0;

            AutoCover = true;
            Exposure = ExposureClass.XC2;
            DesignLife = DesignWorkingLife.Years50;
            CastDirectlyAgainstSoil = false;
            CoverMm = 40.0;

            AxialLoadKnPerM = 150.0;
            MomentKnmPerM = 0.0;
            HorizontalLoadKnPerM = 0.0;
            IncludeSelfWeight = true;

            AutoMeshDiameter = true;
            ForcedMeshDiameterMm = 12.0;
            TopMesh = false;
            Starters = true;
            StarterSpacingMm = 250.0;
            StarterDiameterMm = 10.0;
        }

        public StripFootingDesignSettings Clone()
        {
            return (StripFootingDesignSettings)MemberwiseClone();
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
            if (AxialLoadKnPerM <= 0)
                errors.Add("La charge verticale par metre doit etre superieure a 0.");
            if (!AutoCover && (CoverMm < 25 || CoverMm > 150))
                errors.Add("L'enrobage impose d'une semelle doit etre compris entre 25 et 150 mm.");
            if (Starters && (StarterSpacingMm < 50 || StarterSpacingMm > 600))
                errors.Add("L'espacement des attentes doit etre compris entre 50 et 600 mm.");
            return errors;
        }
    }
}
