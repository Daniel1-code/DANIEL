using System.Collections.Generic;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.Configuration;
using DanCI.Structural.Eurocodes.EC0;
using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Engine.Slab
{
    /// <summary>D'ou viennent les moments de calcul.</summary>
    public enum SlabMomentSource
    {
        /// <summary>
        /// Calcules par le moteur a partir des charges. Isostatique et console relevent de
        /// la statique pure ; les travees continues utilisent des coefficients de continuite
        /// usuels, qui ne sont pas de l'Eurocode et sont annonces comme tels.
        /// </summary>
        FromLoads,

        /// <summary>Saisis par l'utilisateur, issus d'une analyse exterieure.</summary>
        Entered
    }

    /// <summary>Reglages de dimensionnement d'une dalle pleine portant dans un sens.</summary>
    public sealed class SlabDesignSettings
    {
        // --- Normes ---
        public EurocodeGeneration Generation { get; set; }
        public NationalAnnexKind NationalAnnex { get; set; }

        // --- Materiaux ---
        public double ConcreteStrengthMPa { get; set; }
        public double SteelStrengthMPa { get; set; }

        // --- Charges ---
        public SlabMomentSource MomentSource { get; set; }
        /// <summary>Charge permanente hors poids propre (kN/m2).</summary>
        public double PermanentLoadKnM2 { get; set; }
        /// <summary>Charge d'exploitation (kN/m2).</summary>
        public double VariableLoadKnM2 { get; set; }
        /// <summary>Inclure le poids propre de la dalle dans les charges permanentes.</summary>
        public bool IncludeSelfWeight { get; set; }
        /// <summary>Poids volumique du beton arme (kN/m3).</summary>
        public double ConcreteUnitWeightKnM3 { get; set; }
        /// <summary>Categorie d'usage, qui fixe psi_2 et donc la combinaison ELS.</summary>
        public UseCategory Category { get; set; }

        // --- Moments saisis, par metre de largeur ---
        public double SpanMomentKnmPerM { get; set; }
        public double SupportMomentKnmPerM { get; set; }
        public double ShearKnPerM { get; set; }

        // --- Enrobage ---
        public bool AutoCover { get; set; }
        public ExposureClass Exposure { get; set; }
        public DesignWorkingLife DesignLife { get; set; }
        public double CoverMm { get; set; }

        // --- Service ---
        /// <summary>La dalle supporte-t-elle des cloisons fragiles (correction 7/l_eff) ?</summary>
        public bool SupportsPartitions { get; set; }
        /// <summary>Ouverture de fissure visee ; 0 = valeur recommandee du tableau 7.1N.</summary>
        public double CrackWidthLimitMm { get; set; }

        // --- Ferraillage ---
        public bool AutoMeshDiameter { get; set; }
        public double ForcedMeshDiameterMm { get; set; }
        /// <summary>Poser des chapeaux sur appui, meme sans moment negatif declare.</summary>
        public bool TopReinforcement { get; set; }

        public SlabDesignSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = NationalAnnexKind.Recommended;
            ConcreteStrengthMPa = 25.0;
            SteelStrengthMPa = 500.0;

            MomentSource = SlabMomentSource.FromLoads;
            PermanentLoadKnM2 = 2.0;
            VariableLoadKnM2 = 2.5;
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
            ForcedMeshDiameterMm = 10.0;
            TopReinforcement = false;
        }

        public SlabDesignSettings Clone()
        {
            return (SlabDesignSettings)MemberwiseClone();
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
            if (MomentSource == SlabMomentSource.FromLoads)
            {
                if (PermanentLoadKnM2 < 0 || VariableLoadKnM2 < 0)
                    errors.Add("Les charges ne peuvent pas etre negatives.");
                if (PermanentLoadKnM2 + VariableLoadKnM2 <= 0 && !IncludeSelfWeight)
                    errors.Add("Renseignez au moins une charge, ou incluez le poids propre.");
            }
            else if (SpanMomentKnmPerM <= 0 && SupportMomentKnmPerM <= 0)
            {
                errors.Add("Renseignez au moins un moment de calcul.");
            }
            if (!AutoCover && (CoverMm < 10 || CoverMm > 100))
                errors.Add("L'enrobage impose d'une dalle doit etre compris entre 10 et 100 mm.");
            if (CrackWidthLimitMm < 0 || CrackWidthLimitMm > 0.5)
                errors.Add("L'ouverture de fissure visee doit etre comprise entre 0 et 0,5 mm.");
            return errors;
        }
    }
}
