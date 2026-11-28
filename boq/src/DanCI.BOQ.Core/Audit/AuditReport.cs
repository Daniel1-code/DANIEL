using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Audit
{
    /// <summary>Rubrique de l'audit prealable (ordre du rapport).</summary>
    public enum AuditSection
    {
        Materiaux = 1,
        BetonSansClasse = 2,
        Armatures = 3,
        Profils = 4,
        PiecesEtMurs = 5,
        Doublons = 6,
        NonClasses = 7,
        Liens = 8,
        AutresAnomalies = 9
    }

    /// <summary>Un constat d'audit : ce qui fausse le metre, avec les elements a selectionner.</summary>
    public sealed class AuditFinding
    {
        public AuditSection Section { get; set; }
        public IssueSeverity Severity { get; set; }
        public string Code { get; set; }
        public string Message { get; set; }
        public string ArticleCode { get; set; }
        public List<long> ElementIds { get; } = new List<long>();

        /// <summary>Instance de lien RVT dont les ElementIds proviennent ; null = modele hote.</summary>
        public long? LinkInstanceId { get; set; }

        public IReadOnlyCollection<ElementRef> ElementRefs => ElementIds.Distinct().Select(i => new ElementRef(i, LinkInstanceId)).ToList();
    }

    public sealed class AuditReport
    {
        public List<AuditFinding> Findings { get; } = new List<AuditFinding>();

        /// <summary>Part du montant issue de quantites mesurees (voir BoqResult.ReliabilityIndex).</summary>
        public decimal? ReliabilityIndex { get; set; }

        /// <summary>Part de la QUANTITE de chaque nature issue de mesures (utile tant qu'il n'y a pas de prix).</summary>
        public Dictionary<string, decimal> MeasuredShareByKind { get; } = new Dictionary<string, decimal>();

        public int ErrorCount => Findings.Count(f => f.Severity == IssueSeverity.Error);
        public int WarningCount => Findings.Count(f => f.Severity == IssueSeverity.Warning);

        public static string Title(AuditSection s)
        {
            switch (s)
            {
                case AuditSection.Materiaux: return "Éléments sans matériau ou avec matériau par défaut";
                case AuditSection.BetonSansClasse: return "Béton sans classe identifiable";
                case AuditSection.Armatures: return "Armatures : éléments sans armature (ratio) ou ferraillage incomplet";
                case AuditSection.Profils: return "Profilés métalliques sans masse linéique connue";
                case AuditSection.PiecesEtMurs: return "Pièces non fermées ou non placées, murs sans hauteur liée";
                case AuditSection.Doublons: return "Éléments en double ou chevauchements non joints";
                case AuditSection.NonClasses: return "Quantités non mappées à un article (Non classés)";
                case AuditSection.Liens: return "Liens Revit déchargés";
                default: return "Autres anomalies d'extraction, de configuration ou de calcul";
            }
        }
    }
}
