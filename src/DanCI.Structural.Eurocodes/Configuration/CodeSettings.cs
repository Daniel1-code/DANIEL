using DanCI.Structural.Eurocodes.NationalAnnex;

namespace DanCI.Structural.Eurocodes.Configuration
{
    /// <summary>Generation des Eurocodes appliquee.</summary>
    public enum EurocodeGeneration
    {
        /// <summary>EN 1992-1-1:2004 + A1:2014, generation en vigueur dans la pratique courante.</summary>
        En1992_2004,

        /// <summary>EN 1992-1-1:2023, deuxieme generation. Non encore implementee.</summary>
        En1992_2023
    }

    /// <summary>
    /// Configuration normative d'un calcul. Elle est stockee avec le resultat : deux calculs
    /// menes avec des Annexes Nationales differentes sont deux calculs distincts.
    /// </summary>
    public sealed class CodeSettings
    {
        public EurocodeGeneration Generation { get; set; }

        public INationalAnnex NationalAnnex { get; set; }

        public CodeSettings()
        {
            Generation = EurocodeGeneration.En1992_2004;
            NationalAnnex = new RecommendedAnnex();
        }

        public CodeSettings(EurocodeGeneration generation, INationalAnnex nationalAnnex)
        {
            Generation = generation;
            NationalAnnex = nationalAnnex;
        }

        /// <summary>Libelle de la norme, tel qu'il apparait dans les notes de calcul.</summary>
        public string CodeLabel
        {
            get
            {
                return Generation == EurocodeGeneration.En1992_2023
                    ? "EN 1992-1-1:2023"
                    : "EN 1992-1-1:2004+A1:2014";
            }
        }

        public string AnnexLabel
        {
            get { return NationalAnnex != null ? NationalAnnex.Name : "Valeurs recommandees"; }
        }
    }
}
