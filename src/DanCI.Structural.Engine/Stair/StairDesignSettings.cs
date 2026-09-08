using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.NationalAnnex;
using DanCI.Structural.Reinforcement.Plan;

namespace DanCI.Structural.Engine.Stair
{
    /// <summary>D'ou viennent les sollicitations de calcul.</summary>
    public enum StairMomentSource
    {
        /// <summary>
        /// Calculees par le moteur : descente de charge EN 1991-1-1 puis travee
        /// isostatique sous deux charges reparties.
        /// </summary>
        FromLoads,

        /// <summary>Saisies par l'utilisateur, issues d'une analyse exterieure.</summary>
        Entered
    }

    /// <summary>Reglages de dimensionnement d'une volee d'escalier droit.</summary>
    public sealed class StairDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }

        // --- Charges ---
        public StairMomentSource MomentSource { get; set; }
        /// <summary>Revetement de marche, sur la projection horizontale (kN/m2).</summary>
        public double TreadFinishKnM2 { get; set; }
        /// <summary>Enduit de sous-face, sur la surface inclinee reelle (kN/m2).</summary>
        public double SoffitFinishKnM2 { get; set; }
        /// <summary>Charge d'exploitation repartie (kN/m2).</summary>
        public double VariableLoadKnM2 { get; set; }

        /// <summary>
        /// Charge concentree Q_k (kN), EN 1991-1-1 6.3.1.2(1) et tableau 6.2. Elle
        /// s'applique en ALTERNATIVE a la charge repartie, sur 50 x 50 mm. Sa valeur releve
        /// du tableau et de l'annexe nationale : zero desactive la verification.
        /// </summary>
        public double ConcentratedLoadKn { get; set; }
        public bool IncludeSelfWeight { get; set; }
        public double ConcreteUnitWeightKnM3 { get; set; }

        /// <summary>
        /// Categorie d'usage. EN 1991-1-1 6.3.1(1) : un escalier prend celle de la zone
        /// qu'il dessert, il n'en possede pas en propre.
        /// </summary>
        public UseCategory Category { get; set; }

        // --- Sollicitations saisies, par metre de largeur ---
        public double SpanMomentKnmPerM { get; set; }
        public double SupportMomentKnmPerM { get; set; }
        public double ShearKnPerM { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public double CoverMm { get; set; }

        // --- Service ---
        public bool SupportsPartitions { get; set; }

        /// <summary>Ouverture de fissure visee ; 0 = valeur recommandee du tableau 7.1N.</summary>
        public double CrackWidthLimitMm { get; set; }

        // --- Ferraillage ---
        public bool AutoMeshDiameter { get; set; }
        public double ForcedMeshDiameterMm { get; set; }

        /// <summary>
        /// Poser des chapeaux aux appuis. Une volee declaree isostatique est en realite
        /// toujours partiellement encastree dans ses paliers : le defaut est de les poser.
        /// </summary>
        public bool TopReinforcement { get; set; }

        // --- Dispositions ---

        /// <summary>
        /// Regles de disposition : les decisions de ferraillage qui ne sont pas imposees par
        /// la geometrie. Le moteur en propose des valeurs deduites de la geometrie et de la
        /// norme, dit d'ou elles viennent, et accepte qu'on les change.
        /// </summary>
        public StairDetailingRules Detailing { get; set; }

        public StairDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;

            MomentSource = StairMomentSource.FromLoads;
            TreadFinishKnM2 = 1.0;
            SoffitFinishKnM2 = 0.3;
            VariableLoadKnM2 = 3.0;
            ConcentratedLoadKn = 2.0;
            IncludeSelfWeight = true;
            ConcreteUnitWeightKnM3 = 25.0;
            Category = UseCategory.Residential;

            AutoCover = true;
            Exposure = ExposureClass.XC1;
            DesignLife = DesignWorkingLife.Years50;
            CoverMm = 25.0;

            SupportsPartitions = false;
            CrackWidthLimitMm = 0.0;

            AutoMeshDiameter = true;
            ForcedMeshDiameterMm = 12.0;
            TopReinforcement = true;
            Detailing = new StairDetailingRules();
        }

        public StairDesignSettings Clone()
        {
            var copy = (StairDesignSettings)MemberwiseClone();
            // MemberwiseClone partage la reference : sans copie explicite, modifier les
            // dispositions d'un reglage cloné modifierait aussi l'original.
            copy.Detailing = Detailing != null ? Detailing.Clone() : new StairDetailingRules();
            return copy;
        }

        public IEnumerable<string> Validate()
        {
            var errors = new List<string>();
            if (ConcreteStrengthMPa < 12 || ConcreteStrengthMPa > 90)
                errors.Add("La resistance du beton doit etre comprise entre 12 et 90 MPa.");
            if (SteelStrengthMPa < 200 || SteelStrengthMPa > 700)
                errors.Add("La limite d'elasticite de l'acier doit etre comprise entre 200 et 700 MPa.");
            if (ConcreteUnitWeightKnM3 <= 0 || ConcreteUnitWeightKnM3 > 40)
                errors.Add("Le poids volumique du beton doit etre compris entre 0 et 40 kN/m3.");
            if (MomentSource == StairMomentSource.FromLoads)
            {
                if (TreadFinishKnM2 < 0 || SoffitFinishKnM2 < 0 || VariableLoadKnM2 < 0)
                    errors.Add("Les charges ne peuvent pas etre negatives.");
                if (VariableLoadKnM2 <= 0)
                    errors.Add("Un escalier porte toujours une charge d'exploitation : renseignez q_k.");
                if (ConcentratedLoadKn < 0)
                    errors.Add("La charge concentree Q_k ne peut pas etre negative.");
            }
            else if (SpanMomentKnmPerM <= 0 && SupportMomentKnmPerM <= 0)
            {
                errors.Add("Renseignez au moins un moment de calcul.");
            }
            if (!AutoCover && (CoverMm < 10 || CoverMm > 100))
                errors.Add("L'enrobage impose doit etre compris entre 10 et 100 mm.");
            if (CrackWidthLimitMm < 0 || CrackWidthLimitMm > 0.5)
                errors.Add("L'ouverture de fissure visee doit etre comprise entre 0 et 0,5 mm.");
            if (Detailing != null) errors.AddRange(Detailing.Validate());
            return errors;
        }
    }
}
