using System;
using System.Collections.Generic;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>Resultat d'une extraction : quantites, et tout ce qui n'a pas pu etre mesure.</summary>
    public sealed class ExtractionReport
    {
        public List<ElementQuantity> Quantities { get; } = new List<ElementQuantity>();

        /// <summary>Anomalies d'extraction (element sans materiau, echec de lecture...).</summary>
        public List<BoqIssue> Notes { get; } = new List<BoqIssue>();

        /// <summary>Nombre d'elements parcourus par extracteur.</summary>
        public Dictionary<string, int> ElementsScanned { get; } = new Dictionary<string, int>();

        /// <summary>Nombre d'elements ignores et pourquoi (option de conception secondaire...).</summary>
        public Dictionary<string, int> Skipped { get; } = new Dictionary<string, int>();

        public TimeSpan Duration { get; set; }

        internal void Count(Dictionary<string, int> map, string key)
        {
            map.TryGetValue(key, out int n);
            map[key] = n + 1;
        }

        /// <summary>Document en cours d'extraction (vide = hote) et son instance de lien : poses sur chaque constat.</summary>
        internal string CurrentSource { get; set; } = string.Empty;
        internal long? CurrentLinkInstance { get; set; }

        internal void Note(IssueSeverity severity, string code, string message, long elementId)
        {
            var issue = new BoqIssue(severity, code,
                string.IsNullOrEmpty(CurrentSource) ? message : "[lien " + CurrentSource + "] " + message)
            {
                LinkInstanceId = CurrentLinkInstance
            };
            issue.ElementIds.Add(elementId);
            Notes.Add(issue);
        }
    }
}
