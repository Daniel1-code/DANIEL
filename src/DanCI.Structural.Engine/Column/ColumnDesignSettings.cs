using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Optimization;

namespace DanCI.Structural.Engine.Column
{
    /// <summary>Reglement applique aux dispositions constructives.</summary>
    public enum DetailingCodeKind
    {
        Eurocode2,
        Aci318
    }

    /// <summary>
    /// Reglages de dimensionnement d'un poteau. Type volontairement plat et compose
    /// uniquement de valeurs simples : il est serialisable tel quel dans les configurations
    /// enregistrees.
    /// </summary>
    public sealed class ColumnDesignSettings
    {
        // --- Normes ---
        public DetailingCodeKind DetailingCode { get; set; }
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }

        // --- Enrobage ---

        /// <summary>Calcule l'enrobage selon l'EC2 4.4.1 au lieu d'utiliser la valeur saisie.</summary>
        public bool AutoCover { get; set; }

        /// <summary>Classe d'exposition, EC2 tableau 4.1.</summary>
        public ExposureClass Exposure { get; set; }

        /// <summary>Duree d'utilisation de projet, EN 1990 tableau 2.1.</summary>
        public DesignWorkingLife DesignLife { get; set; }

        /// <summary>Controle de production du beton assure : reduit la classe structurale.</summary>
        public bool SpecialQualityControl { get; set; }

        /// <summary>Enrobage nominal impose (mm), utilise si AutoCover est faux.</summary>
        public double CoverMm { get; set; }

        public double AggregateSizeMm { get; set; }

        // --- Efforts ---
        public double AxialLoadKn { get; set; }
        public double MomentAboutXKnm { get; set; }
        public double MomentAboutYKnm { get; set; }

        // --- Ferraillage longitudinal ---
        public double TargetRatioPercent { get; set; }
        public bool AutoLongitudinalDiameter { get; set; }
        public double ForcedLongitudinalDiameterMm { get; set; }
        public bool AutoBarCount { get; set; }
        public int ForcedBarsAlongX { get; set; }
        public int ForcedBarsAlongY { get; set; }
        public int ForcedCircularBarCount { get; set; }

        // --- Ferraillage transversal ---
        public bool AutoTransverse { get; set; }
        public double ForcedStirrupDiameterMm { get; set; }
        public double ForcedSpacingMm { get; set; }
        public bool UseCriticalZones { get; set; }
        public bool AddCrossTies { get; set; }
        public bool Seismic { get; set; }

        // --- Longueurs ---
        public double FirstStirrupOffsetMm { get; set; }
        public double BottomOffsetMm { get; set; }
        /// <summary>Depassement en tete (mm). -1 = longueur de recouvrement calculee.</summary>
        public double TopExtensionMm { get; set; }

        // --- Verification de resistance ---
        public bool VerifyCapacity { get; set; }
        /// <summary>Coefficient de longueur de flambement : l0 = coefficient x hauteur.</summary>
        public double BucklingFactor { get; set; }
        /// <summary>Coefficient de fluage effectif phi_ef.</summary>
        public double CreepCoefficient { get; set; }

        public ColumnDesignSettings()
        {
            DetailingCode = DetailingCodeKind.Eurocode2;
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;
            AutoCover = true;
            Exposure = ExposureClass.XC1;
            DesignLife = DesignWorkingLife.Years50;
            SpecialQualityControl = false;
            CoverMm = 30.0;
            AggregateSizeMm = 20.0;
            AxialLoadKn = 0.0;
            MomentAboutXKnm = 0.0;
            MomentAboutYKnm = 0.0;
            TargetRatioPercent = 1.0;
            AutoLongitudinalDiameter = true;
            ForcedLongitudinalDiameterMm = 16.0;
            AutoBarCount = true;
            ForcedBarsAlongX = 3;
            ForcedBarsAlongY = 3;
            ForcedCircularBarCount = 6;
            AutoTransverse = true;
            ForcedStirrupDiameterMm = 8.0;
            ForcedSpacingMm = 200.0;
            UseCriticalZones = true;
            AddCrossTies = true;
            Seismic = false;
            FirstStirrupOffsetMm = 50.0;
            BottomOffsetMm = 0.0;
            TopExtensionMm = -1.0;
            VerifyCapacity = false;
            BucklingFactor = 1.0;
            CreepCoefficient = 2.0;
        }

        public ColumnDesignSettings Clone()
        {
            return (ColumnDesignSettings)MemberwiseClone();
        }

        public ColumnLayoutOptions ToLayoutOptions()
        {
            return new ColumnLayoutOptions
            {
                CoverMm = CoverMm,
                AggregateSizeMm = AggregateSizeMm,
                AutoDiameter = AutoLongitudinalDiameter,
                ForcedDiameterMm = ForcedLongitudinalDiameterMm,
                AutoCount = AutoBarCount,
                ForcedBarsAlongX = ForcedBarsAlongX,
                ForcedBarsAlongY = ForcedBarsAlongY,
                ForcedCircularBarCount = ForcedCircularBarCount,
                AutoTransverse = AutoTransverse,
                ForcedTransverseDiameterMm = ForcedStirrupDiameterMm
            };
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (!AutoCover && (CoverMm < 10 || CoverMm > 120))
                errors.Add("L'enrobage impose doit etre compris entre 10 et 120 mm.");
            if (TargetRatioPercent < 0 || TargetRatioPercent > 4)
                errors.Add("Le taux d'armature vise doit etre compris entre 0 et 4 %.");
            if (!AutoBarCount && (ForcedBarsAlongX < 2 || ForcedBarsAlongY < 2))
                errors.Add("Il faut au moins 2 barres par face pour une section rectangulaire.");
            if (!AutoBarCount && ForcedCircularBarCount < 6)
                errors.Add("Il faut au moins 6 barres pour une section circulaire.");
            if (!AutoTransverse && ForcedSpacingMm < 40)
                errors.Add("L'espacement des cadres doit etre d'au moins 40 mm.");
            if (BucklingFactor < 0.3 || BucklingFactor > 4.0)
                errors.Add("Le coefficient de longueur de flambement doit etre compris entre 0,3 et 4.");
            if (CreepCoefficient < 0 || CreepCoefficient > 4)
                errors.Add("Le coefficient de fluage doit etre compris entre 0 et 4.");
            if (VerifyCapacity && AxialLoadKn <= 0)
                errors.Add("La verification de resistance demande un effort normal NEd superieur a 0.");
            return errors;
        }
    }
}
