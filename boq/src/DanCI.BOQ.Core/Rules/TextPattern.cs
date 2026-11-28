using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace DanCI.BOQ.Core.Rules
{
    /// <summary>
    /// Motif de texte pour les regles : comparaison insensible a la casse, jokers « * » (toute
    /// suite de caracteres) et « ? » (un caractere). Pas d'expression reguliere libre : une
    /// regle doit rester lisible par un metreur.
    /// </summary>
    public sealed class TextPattern
    {
        private readonly Regex regex;

        public TextPattern(string pattern)
        {
            if (pattern == null)
            {
                throw new ArgumentNullException(nameof(pattern));
            }
            Pattern = pattern;
            string body = Regex.Escape(pattern.Trim()).Replace(@"\*", ".*").Replace(@"\?", ".");
            regex = new Regex("^" + body + "$",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Singleline);
        }

        public string Pattern { get; }

        /// <summary>Une valeur absente (null) ne correspond qu'au motif « * ».</summary>
        public bool IsMatch(string value)
        {
            return regex.IsMatch((value ?? string.Empty).Trim());
        }

        public override string ToString() => Pattern;
    }

    /// <summary>
    /// Liste de motifs alternatifs (OU). En JSON : une chaine ou un tableau de chaines.
    /// Liste vide = critere absent (ne contraint rien).
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(DanCI.BOQ.Core.Configuration.PatternListJsonConverter))]
    public sealed class PatternList
    {
        public PatternList(IEnumerable<string> patterns)
        {
            Items = (patterns ?? Enumerable.Empty<string>())
                .Where(p => !string.IsNullOrWhiteSpace(p))
                .Select(p => new TextPattern(p))
                .ToArray();
        }

        public IReadOnlyList<TextPattern> Items { get; }

        public bool IsEmpty => Items.Count == 0;

        public bool IsMatch(string value) => IsEmpty || Items.Any(p => p.IsMatch(value));

        public override string ToString() => string.Join(" | ", Items.Select(i => i.Pattern));
    }
}
