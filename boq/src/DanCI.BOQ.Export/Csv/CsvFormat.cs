using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DanCI.BOQ.Export.Csv
{
    /// <summary>
    /// Format CSV. Par defaut, celui qu'ouvre Excel en francais sans assistant : separateur
    /// « ; », virgule decimale, UTF-8 avec BOM (sinon les accents sont mal lus).
    /// </summary>
    public sealed class CsvFormat
    {
        public static CsvFormat ExcelFrancais { get; } = new CsvFormat(';', CultureInfo.GetCultureInfo("fr-FR"));

        /// <summary>Pour d'autres outils : virgule, point decimal.</summary>
        public static CsvFormat Invariant { get; } = new CsvFormat(',', CultureInfo.InvariantCulture);

        public CsvFormat(char separator, CultureInfo culture)
        {
            Separator = separator;
            Culture = culture;
        }

        public char Separator { get; }
        public CultureInfo Culture { get; }

        /// <summary>Nombre sans separateur de milliers, jusqu'a 9 decimales (pas d'arrondi visible).</summary>
        public string Number(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString("0.#########", Culture);

        public string Number(decimal value) => value.ToString("0.#########", Culture);

        public string Number(decimal? value) => value.HasValue ? Number(value.Value) : string.Empty;

        public string Field(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }
            bool quote = value.IndexOf(Separator) >= 0 || value.IndexOf('"') >= 0 ||
                         value.IndexOf('\n') >= 0 || value.IndexOf('\r') >= 0 ||
                         value.StartsWith(" ", StringComparison.Ordinal) || value.EndsWith(" ", StringComparison.Ordinal);
            // Protection contre l'injection de formules a l'ouverture dans Excel.
            if (value[0] == '=' || value[0] == '+' || value[0] == '@' || (value[0] == '-' && value.Length > 1 && !char.IsDigit(value[1])))
            {
                value = "'" + value;
            }
            return quote ? "\"" + value.Replace("\"", "\"\"") + "\"" : value;
        }

        public void WriteRow(TextWriter writer, params string[] cells)
        {
            var sb = new StringBuilder();
            for (int i = 0; i < cells.Length; i++)
            {
                if (i > 0) sb.Append(Separator);
                sb.Append(cells[i] ?? string.Empty);
            }
            writer.Write(sb.ToString());
            writer.Write("\r\n");
        }

        public static StreamWriter OpenFile(string path) =>
            new StreamWriter(path, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
    }
}
