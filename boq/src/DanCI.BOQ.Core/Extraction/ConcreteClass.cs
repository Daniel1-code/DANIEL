using System.Text.RegularExpressions;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Reconnait une classe de resistance du beton (EN 206 : C20/25, C25/30, C30/37...) dans un
    /// nom de materiau ou de type. Ne devine jamais : sans motif Cxx/yy explicite, pas de classe.
    /// </summary>
    public static class ConcreteClass
    {
        private static readonly Regex Pattern = new Regex(
            @"(?<![A-Za-z0-9])C\s?(?<fck>\d{2,3})\s?/\s?(?<cube>\d{2,3})(?!\d)",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Classe normalisee (« C25/30 ») ou null.</summary>
        public static string Parse(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }
            Match m = Pattern.Match(text);
            return m.Success ? "C" + m.Groups["fck"].Value + "/" + m.Groups["cube"].Value : null;
        }
    }
}
