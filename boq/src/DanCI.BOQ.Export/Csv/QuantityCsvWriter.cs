using System.Collections.Generic;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Export.Csv
{
    /// <summary>
    /// Metre brut : une ligne par quantite extraite, AVANT toute regle. C'est le fichier qui
    /// permet de controler l'extraction contre un metre manuel, element par element.
    /// </summary>
    public static class QuantityCsvWriter
    {
        public static readonly string[] Header =
        {
            "Document", "ElementId", "UniqueId", "Categorie", "Libelle categorie", "Famille", "Type",
            "Materiau", "Niveau", "Nature", "Valeur", "Unite", "Methode", "Description methode",
            "Fiabilite", "Hypothese", "Exclu (DanCI_BOQ_Exclure)", "Parametres"
        };

        public static void Write(TextWriter writer, IEnumerable<ElementQuantity> quantities, CsvFormat format)
        {
            format.WriteRow(writer, Header.Select(format.Field).ToArray());
            foreach (ElementQuantity q in quantities
                         .OrderBy(q => q.SourceDocument ?? string.Empty, System.StringComparer.Ordinal)
                         .ThenBy(q => q.ElementId)
                         .ThenBy(q => q.Kind ?? string.Empty, System.StringComparer.Ordinal)
                         .ThenBy(q => q.Material ?? string.Empty, System.StringComparer.Ordinal))
            {
                string parameters = q.Parameters == null
                    ? string.Empty
                    : string.Join(" | ", q.Parameters.OrderBy(p => p.Key, System.StringComparer.Ordinal).Select(p => p.Key + "=" + p.Value));
                format.WriteRow(writer,
                    format.Field(q.SourceDocument),
                    q.ElementId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    format.Field(q.UniqueId),
                    format.Field(q.Category),
                    format.Field(q.CategoryLabel),
                    format.Field(q.Family),
                    format.Field(q.TypeName),
                    format.Field(q.Material),
                    format.Field(q.Level),
                    format.Field(q.Kind),
                    format.Number(q.Value),
                    format.Field(q.Unit.Symbol()),
                    format.Field(q.MethodCode),
                    format.Field(q.MethodDescription),
                    format.Field(q.Reliability.Label()),
                    format.Field(q.Assumption),
                    q.ExcludedByUser ? "Oui" : string.Empty,
                    format.Field(parameters));
            }
        }

        public static void WriteFile(string path, IEnumerable<ElementQuantity> quantities, CsvFormat format)
        {
            using (StreamWriter w = CsvFormat.OpenFile(path))
            {
                Write(w, quantities, format);
            }
        }
    }

    /// <summary>
    /// Lignes du BOQ calculees par le moteur, a plat (sans mise en forme) : pour verifier le
    /// classement et les montants avant l'export Excel de l'etape 6.
    /// </summary>
    public static class BoqLinesCsvWriter
    {
        public static readonly string[] Header =
        {
            "Lot", "Code", "Article", "Designation", "Unite", "Quantite nette", "Coefficient", "Quantite brute",
            "Quantite valorisee", "PU", "Montant", "Fiabilite", "Nb elements", "ElementIds", "Hypotheses"
        };

        public static void Write(TextWriter writer, BoqResult result, CsvFormat format)
        {
            format.WriteRow(writer, Header.Select(format.Field).ToArray());
            foreach (BoqLotResult lot in result.Lots)
            {
                foreach (BoqLine l in lot.Lines)
                {
                    format.WriteRow(writer,
                        format.Field(lot.Code),
                        format.Field(l.Code),
                        format.Field(l.ArticleCode),
                        format.Field(l.Designation),
                        format.Field(l.Unit.Symbol()),
                        format.Number(l.NetQuantity),
                        format.Number(l.CoefficientFactor),
                        format.Number(l.GrossQuantity),
                        format.Number(l.Quantity),
                        format.Number(l.UnitPrice),
                        format.Number(l.Amount),
                        format.Field(l.Reliability.Label()),
                        l.Contributions.Count.ToString(System.Globalization.CultureInfo.InvariantCulture),
                        format.Field(string.Join(" ", l.ElementIds)),
                        format.Field(string.Join(" / ", l.Assumptions)));
                }
            }
        }

        public static void WriteFile(string path, BoqResult result, CsvFormat format)
        {
            using (StreamWriter w = CsvFormat.OpenFile(path))
            {
                Write(w, result, format);
            }
        }
    }
}

namespace DanCI.BOQ.Export.Csv
{
    /// <summary>Anomalies (extraction, configuration, moteur), avec les ElementId concernes.</summary>
    public static class IssuesCsvWriter
    {
        public static readonly string[] Header = { "Gravite", "Code", "Fichier", "Article", "Message", "ElementIds" };

        public static void Write(System.IO.TextWriter writer, System.Collections.Generic.IEnumerable<BoqIssue> issues, CsvFormat format)
        {
            format.WriteRow(writer, System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(Header, format.Field)));
            foreach (BoqIssue i in System.Linq.Enumerable.OrderByDescending(issues, x => x.Severity))
            {
                format.WriteRow(writer,
                    format.Field(Label(i.Severity)),
                    format.Field(i.Code),
                    format.Field(i.File),
                    format.Field(i.ArticleCode),
                    format.Field(i.Message),
                    format.Field(string.Join(" ", i.ElementIds)));
            }
        }

        public static void WriteFile(string path, System.Collections.Generic.IEnumerable<BoqIssue> issues, CsvFormat format)
        {
            using (System.IO.StreamWriter w = CsvFormat.OpenFile(path))
            {
                Write(w, issues, format);
            }
        }

        public static string Label(IssueSeverity s) =>
            s == IssueSeverity.Error ? "Erreur" : s == IssueSeverity.Warning ? "Avertissement" : "Information";
    }
}
