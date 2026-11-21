using System.Collections.Generic;

namespace DanCI.BOQ.Core.Model
{
    public enum IssueSeverity
    {
        /// <summary>Information de tracabilite (ex. element exclu par une regle).</summary>
        Info,
        /// <summary>Le BOQ est produit mais une ligne est incomplete ou estimee.</summary>
        Warning,
        /// <summary>Une quantite ou un montant est faux ou absent : a corriger avant diffusion.</summary>
        Error
    }

    /// <summary>Anomalie relevee par le moteur ou la validation, avec les elements concernes.</summary>
    public sealed class BoqIssue
    {
        public BoqIssue(IssueSeverity severity, string code, string message)
        {
            Severity = severity;
            Code = code;
            Message = message;
        }

        public IssueSeverity Severity { get; }

        /// <summary>Code stable (ex. PRIX_ABSENT), pour filtrer et tester.</summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>Fichier de configuration concerne, le cas echeant.</summary>
        public string File { get; set; }

        public string ArticleCode { get; set; }

        public List<long> ElementIds { get; } = new List<long>();

        public override string ToString() =>
            Severity + " " + Code + (ArticleCode != null ? " [" + ArticleCode + "]" : string.Empty) +
            (File != null ? " (" + File + ")" : string.Empty) + " : " + Message;
    }
}
