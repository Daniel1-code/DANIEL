using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>
    /// Bibliotheque de prix en Excel (§7 : import / export Excel). Le modele exporte liste TOUS
    /// les articles du catalogue (PU vide s'il n'y a pas de prix) et les prix existants : le
    /// metreur remplit, puis l'import produit une bibliotheque JSON. Aucun prix n'est invente :
    /// une ligne sans PU numerique n'est pas importee.
    /// </summary>
    public static class PriceLibraryWorkbook
    {
        public const string Sheet = "Prix unitaires";
        private const int HeaderRow = 7;
        private static readonly string[] Headers =
        {
            "Code article", "Désignation", "Unité", "Type (motif, facultatif)", "Famille (motif, facultatif)",
            "PU", "Devise", "Date du prix", "Source", "Matériaux", "Main-d'œuvre", "Matériel"
        };
        private static readonly XLColor Blue = XLColor.FromHtml("#1F4E79");
        private static readonly XLColor Input = XLColor.FromHtml("#FFF2CC");
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static void WriteTemplate(string path, MappingCatalog catalog, PriceLibrary existing)
        {
            using (var wb = new XLWorkbook())
            {
                IXLWorksheet ws = wb.AddWorksheet(Sheet);
                ws.Cell(1, 1).Value = "BIBLIOTHÈQUE DE PRIX UNITAIRES — DanCI Métré & BOQ";
                ws.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
                Meta(ws, 2, "Nom", existing?.Name);
                Meta(ws, 3, "Ville", existing?.City ?? "Kinshasa");
                Meta(ws, 4, "Client", existing?.Client);
                Meta(ws, 5, "Date", existing?.Date?.ToString("dd/MM/yyyy", Fr));
                ws.Cell(5, 4).Value = "Saisir PU, devise (USD/CDF), date et source dans les cellules jaunes. Une ligne sans PU n'est pas importée. " +
                                      "Décomposition facultative : si elle est donnée, sa somme doit égaler le PU.";
                ws.Cell(5, 4).Style.Font.SetItalic();

                int[] widths = { 12, 60, 7, 18, 18, 12, 8, 13, 36, 12, 12, 12 };
                for (int c = 0; c < Headers.Length; c++)
                {
                    ws.Cell(HeaderRow, c + 1).Value = Headers[c];
                    ws.Column(c + 1).Width = widths[c];
                }
                ws.Range(HeaderRow, 1, HeaderRow, Headers.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White)
                    .Fill.SetBackgroundColor(Blue).Alignment.SetWrapText();

                int r = HeaderRow + 1;
                var prices = existing?.Prices ?? new List<PriceEntry>();
                foreach (Article a in catalog?.Articles ?? new List<Article>())
                {
                    List<PriceEntry> forArticle = prices.Where(p => p.Code == a.Code).ToList();
                    if (forArticle.Count == 0)
                    {
                        forArticle.Add(null);
                    }
                    foreach (PriceEntry p in forArticle)
                    {
                        ws.Cell(r, 1).Value = a.Code;
                        ws.Cell(r, 2).Value = a.Designation;
                        ws.Cell(r, 3).Value = a.Unit.Symbol();
                        if (p != null)
                        {
                            ws.Cell(r, 4).Value = p.TypeName?.ToString() ?? string.Empty;
                            ws.Cell(r, 5).Value = p.Family?.ToString() ?? string.Empty;
                            ws.Cell(r, 6).Value = p.UnitPrice;
                            ws.Cell(r, 7).Value = p.Currency.ToString();
                            if (p.Date.HasValue) ws.Cell(r, 8).Value = p.Date.Value;
                            ws.Cell(r, 9).Value = p.Source ?? string.Empty;
                            if (p.Breakdown != null)
                            {
                                ws.Cell(r, 10).Value = p.Breakdown.Materials;
                                ws.Cell(r, 11).Value = p.Breakdown.Labour;
                                ws.Cell(r, 12).Value = p.Breakdown.Equipment;
                            }
                        }
                        else
                        {
                            ws.Cell(r, 7).Value = "USD";
                        }
                        ws.Range(r, 4, r, 12).Style.Fill.SetBackgroundColor(Input);
                        ws.Cell(r, 6).Style.NumberFormat.Format = "#,##0.00";
                        ws.Cell(r, 8).Style.NumberFormat.Format = "dd/mm/yyyy";
                        ws.Range(r, 10, r, 12).Style.NumberFormat.Format = "#,##0.00";
                        r++;
                    }
                }
                if (r > HeaderRow + 1)
                {
                    ws.Range(HeaderRow + 1, 7, r - 1, 7).CreateDataValidation().List("\"USD,CDF\"", true);
                    ws.Range(HeaderRow, 1, r - 1, Headers.Length).SetAutoFilter();
                }
                ws.SheetView.FreezeRows(HeaderRow);
                wb.SaveAs(path);
            }
        }

        /// <summary>Lit un classeur de prix. Les anomalies de lecture sont rendues avec le numero de ligne.</summary>
        public static PriceLibrary Read(string path, List<BoqIssue> issues)
        {
            var lib = new PriceLibrary { SchemaVersion = 1 };
            using (var wb = new XLWorkbook(path))
            {
                if (!wb.TryGetWorksheet(Sheet, out IXLWorksheet ws))
                {
                    throw new FormatException("Feuille « " + Sheet + " » introuvable : utilisez le modèle exporté par DanCI.");
                }
                lib.Name = ws.Cell(2, 2).GetString();
                lib.City = ws.Cell(3, 2).GetString();
                lib.Client = NullIfEmpty(ws.Cell(4, 2).GetString());
                lib.Date = ReadDate(ws.Cell(5, 2));
                foreach (IXLRow row in ws.RowsUsed().Where(x => x.RowNumber() > HeaderRow))
                {
                    string code = row.Cell(1).GetString().Trim();
                    if (code.Length == 0 || !TryNumber(row.Cell(6), out decimal pu))
                    {
                        continue; // pas de PU : non importe (jamais invente)
                    }
                    string where = "ligne " + row.RowNumber() + " (" + code + ")";
                    if (!BoqUnits.TryParse(row.Cell(3).GetString(), out BoqUnit unit))
                    {
                        issues.Add(new BoqIssue(IssueSeverity.Error, "PRIX_UNITE", where + " : unité « " + row.Cell(3).GetString() + " » inconnue.") { File = path });
                        continue;
                    }
                    string currency = row.Cell(7).GetString().Trim().ToUpperInvariant();
                    if (currency != "USD" && currency != "CDF")
                    {
                        issues.Add(new BoqIssue(IssueSeverity.Error, "PRIX_DEVISE", where + " : devise « " + currency + " » (USD ou CDF attendu).") { File = path });
                        continue;
                    }
                    var entry = new PriceEntry
                    {
                        Code = code,
                        Designation = row.Cell(2).GetString(),
                        Unit = unit,
                        UnitPrice = pu,
                        Currency = currency == "CDF" ? Currency.CDF : Currency.USD,
                        Date = ReadDate(row.Cell(8)),
                        Source = NullIfEmpty(row.Cell(9).GetString()),
                        TypeName = Patterns(row.Cell(4).GetString()),
                        Family = Patterns(row.Cell(5).GetString())
                    };
                    bool m = TryNumber(row.Cell(10), out decimal mat), l = TryNumber(row.Cell(11), out decimal lab), e = TryNumber(row.Cell(12), out decimal eq);
                    if (m || l || e)
                    {
                        entry.Breakdown = new PriceBreakdown { Materials = mat, Labour = lab, Equipment = eq };
                    }
                    lib.Prices.Add(entry);
                }
            }
            return lib;
        }

        private static PatternList Patterns(string text)
        {
            string[] parts = (text ?? string.Empty).Split(new[] { '|' }, StringSplitOptions.RemoveEmptyEntries).Select(p => p.Trim()).Where(p => p.Length > 0).ToArray();
            return parts.Length == 0 ? null : new PatternList(parts);
        }

        private static void Meta(IXLWorksheet ws, int row, string label, string value)
        {
            ws.Cell(row, 1).Value = label;
            ws.Cell(row, 1).Style.Font.SetBold();
            ws.Cell(row, 2).Value = value ?? string.Empty;
            ws.Cell(row, 2).Style.Fill.SetBackgroundColor(Input);
        }

        private static DateTime? ReadDate(IXLCell cell)
        {
            if (cell.Value.IsDateTime) return cell.Value.GetDateTime();
            if (cell.Value.IsNumber) return DateTime.FromOADate(cell.Value.GetNumber());
            string s = cell.GetString().Trim();
            return DateTime.TryParseExact(s, new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" }, Fr, DateTimeStyles.None, out DateTime d) ? d : (DateTime?)null;
        }

        private static bool TryNumber(IXLCell cell, out decimal value)
        {
            value = 0;
            if (cell.Value.IsNumber)
            {
                value = (decimal)cell.Value.GetNumber();
                return true;
            }
            string s = cell.GetString().Trim().Replace(' ', ' ').Replace(" ", string.Empty);
            return s.Length > 0 && (decimal.TryParse(s, NumberStyles.Float, Fr, out value) || decimal.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value));
        }

        private static string NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
    }
}
