using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Export.Csv
{
    /// <summary>
    /// Relit un metre brut ecrit par QuantityCsvWriter, au format Excel francais (« ; », virgule
    /// decimale) ou brut (« , », point decimal) : le format est reconnu sur l'en-tete. Sert a
    /// comparer deux analyses (Revit 2026 / 2027) sans Revit.
    /// </summary>
    public static class QuantityCsvReader
    {
        public static List<ElementQuantity> ReadFile(string path) => Read(File.ReadAllText(path, Encoding.UTF8));

        public static List<ElementQuantity> Read(string text)
        {
            text = text.TrimStart('﻿');
            List<List<string>> rows = Parse(text, text.Split('\n')[0].Contains(';') ? ';' : ',');
            if (rows.Count == 0 || rows[0].Count < QuantityCsvWriter.Header.Length || rows[0][0] != QuantityCsvWriter.Header[0])
            {
                throw new FormatException("Ce fichier n'est pas un métré brut DanCI (en-tête « " + QuantityCsvWriter.Header[0] + ";ElementId;… » attendu).");
            }
            CultureInfo culture = text.Split('\n')[0].Contains(';') ? CultureInfo.GetCultureInfo("fr-FR") : CultureInfo.InvariantCulture;
            var result = new List<ElementQuantity>();
            foreach (List<string> c in rows.Skip(1).Where(r => r.Count >= QuantityCsvWriter.Header.Length))
            {
                var q = new ElementQuantity
                {
                    SourceDocument = Clean(c[0]),
                    ElementId = long.Parse(c[1], CultureInfo.InvariantCulture),
                    UniqueId = Clean(c[2]),
                    Category = Clean(c[3]),
                    CategoryLabel = Clean(c[4]),
                    Family = Clean(c[5]),
                    TypeName = Clean(c[6]),
                    Material = Clean(c[7]),
                    Level = Clean(c[8]),
                    Kind = Clean(c[9]),
                    Value = double.Parse(c[10].Replace(' ', ' ').Replace(" ", string.Empty), NumberStyles.Float, culture),
                    Unit = BoqUnits.Parse(c[11]),
                    MethodCode = Clean(c[12]),
                    MethodDescription = Clean(c[13]),
                    Reliability = ParseReliability(c[14]),
                    Assumption = Clean(c[15]),
                    ExcludedByUser = c[16] == "Oui",
                    Parameters = ParseParameters(c[17])
                };
                result.Add(q);
            }
            return result;
        }

        private static Reliability ParseReliability(string label)
        {
            foreach (Reliability r in Enum.GetValues(typeof(Reliability)))
            {
                if (r.Label() == label) return r;
            }
            throw new FormatException("Fiabilité inconnue : « " + label + " ».");
        }

        private static Dictionary<string, string> ParseParameters(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (string part in Clean(text)?.Split(new[] { " | " }, StringSplitOptions.RemoveEmptyEntries) ?? new string[0])
            {
                int i = part.IndexOf('=');
                if (i > 0) d[part.Substring(0, i)] = part.Substring(i + 1);
            }
            return d;
        }

        /// <summary>Retire la protection anti-formule (« ' » devant = + @ -) posee a l'ecriture.</summary>
        private static string Clean(string s)
        {
            if (string.IsNullOrEmpty(s)) return string.Empty;
            return s.Length > 1 && s[0] == '\'' && "=+@-".IndexOf(s[1]) >= 0 ? s.Substring(1) : s;
        }

        private static List<List<string>> Parse(string text, char sep)
        {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < text.Length; i++)
            {
                char ch = text[i];
                if (quoted)
                {
                    if (ch == '"' && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else if (ch == '"') quoted = false;
                    else field.Append(ch);
                }
                else if (ch == '"') quoted = true;
                else if (ch == sep) { row.Add(field.ToString()); field.Clear(); }
                else if (ch == '\r') { }
                else if (ch == '\n') { row.Add(field.ToString()); field.Clear(); rows.Add(row); row = new List<string>(); }
                else field.Append(ch);
            }
            if (field.Length > 0 || row.Count > 0)
            {
                row.Add(field.ToString());
                rows.Add(row);
            }
            return rows;
        }
    }
}
