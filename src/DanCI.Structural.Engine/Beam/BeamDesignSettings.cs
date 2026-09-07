using System.Collections.Generic;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.Beam
{
    /// <summary>
    /// Reglages de dimensionnement d'une poutre. Type plat et compose de valeurs simples :
    /// il est serialisable tel quel dans les configurations enregistrees.
    /// </summary>
    public sealed class BeamDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public bool SpecialQualityControl { get; set; }
        public double CoverMm { get; set; }
        public double AggregateSizeMm { get; set; }

        // --- Sollicitations ---
        /// <summary>Moment maximal en travee (kN.m), positif.</summary>
        public double SpanMomentKnm { get; set; }
        /// <summary>Moment sur l'appui de gauche (kN.m), en valeur absolue.</summary>
        public double LeftSupportMomentKnm { get; set; }
        /// <summary>Moment sur l'appui de droite (kN.m), en valeur absolue.</summary>
        public double RightSupportMomentKnm { get; set; }
        /// <summary>Effort tranchant au nu de l'appui de gauche (kN).</summary>
        public double LeftShearKn { get; set; }
        /// <summary>Effort tranchant au nu de l'appui de droite (kN).</summary>
        public double RightShearKn { get; set; }
        /// <summary>Effort normal de compression eventuel (kN), pour sigma_cp.</summary>
        public double AxialForceKn { get; set; }

        /// <summary>Coefficient de redistribution delta (1,0 = aucune redistribution).</summary>
        public double RedistributionRatio { get; set; }

        // --- Table collaborante ---
        // La dalle n'appartient pas a l'element poutre dans Revit : c'est l'ingenieur qui
        // declare si la poutre travaille en section en T, et avec quelle table.

        /// <summary>Traiter la poutre en section en T avec la dalle qu'elle porte.</summary>
        public bool TreatAsTSection { get; set; }

        /// <summary>Largeur totale de table disponible (mm).</summary>
        public double FlangeWidthMm { get; set; }

        /// <summary>Epaisseur de la table, soit l'epaisseur de la dalle (mm).</summary>
        public double FlangeThicknessMm { get; set; }

        /// <summary>Conditions d'appui, pour la distance entre moments nuls l0.</summary>
        public BeamSpanKind SpanKind { get; set; }

        // --- Ferraillage longitudinal ---
        public bool AutoLongitudinalDiameter { get; set; }
        public double ForcedLongitudinalDiameterMm { get; set; }
        /// <summary>Nombre maximal de lits superposes.</summary>
        public int MaxLayers { get; set; }

        // --- Ferraillage transversal ---
        public bool AutoStirrupDiameter { get; set; }
        public double ForcedStirrupDiameterMm { get; set; }
        /// <summary>Nombre de brins d'un cadre.</summary>
        public int StirrupLegs { get; set; }

        public BeamDesignSettings()
        {
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
            RedistributionRatio = 1.0;
            TreatAsTSection = false;
            FlangeWidthMm = 0.0;
            FlangeThicknessMm = 0.0;
            SpanKind = BeamSpanKind.SimplySupported;
            AutoLongitudinalDiameter = true;
            ForcedLongitudinalDiameterMm = 16.0;
            MaxLayers = 2;
            AutoStirrupDiameter = true;
            ForcedStirrupDiameterMm = 8.0;
            StirrupLegs = 2;
        }

        public BeamDesignSettings Clone()
        {
            return (BeamDesignSettings)MemberwiseClone();
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
            if (RedistributionRatio < 0.7 || RedistributionRatio > 1.0)
                errors.Add("Le coefficient de redistribution doit etre compris entre 0,7 et 1,0.");
            if (MaxLayers < 1 || MaxLayers > 3)
                errors.Add("Le nombre de lits doit etre compris entre 1 et 3.");
            if (StirrupLegs < 2 || StirrupLegs > 6)
                errors.Add("Un cadre comporte entre 2 et 6 brins.");
            if (TreatAsTSection && FlangeThicknessMm <= 0)
                errors.Add("Renseignez l'epaisseur de la table pour une section en T.");
            if (SpanMomentKnm < 0 || LeftSupportMomentKnm < 0 || RightSupportMomentKnm < 0)
                errors.Add("Les moments doivent etre saisis en valeur absolue.");
            if (SpanMomentKnm <= 0 && LeftSupportMomentKnm <= 0 && RightSupportMomentKnm <= 0)
                errors.Add("Renseignez au moins un moment de calcul.");
            return errors;
        }
    }
}
