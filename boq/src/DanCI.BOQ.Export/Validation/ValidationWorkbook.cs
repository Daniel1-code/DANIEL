using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Validation;

namespace DanCI.BOQ.Export.Validation
{
    /// <summary>
    /// Classeurs de l'etape 8 : gabarit de metre manuel (a remplir), rapport de comparaison
    /// metre manuel / modele, rapport de comparaison de deux analyses (Revit 2026 / 2027).
    /// </summary>
    public static class ValidationWorkbook
    {
        public const string ElementSheet = "Métré manuel — éléments";
        public const string ArticleSheet = "Métré manuel — articles";
        private static readonly XLColor Blue = XLColor.FromHtml("#1F4E79");
        private static readonly XLColor Input = XLColor.FromHtml("#FFF2CC");
        private static readonly XLColor Bad = XLColor.FromHtml("#F8CBAD");
        private static readonly XLColor Good = XLColor.FromHtml("#E2EFDA");
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Natures proposees au metre manuel (comptages exclus : rien a mesurer).</summary>
        private static readonly string[] Kinds =
        {
            QuantityKinds.ConcreteVolume, QuantityKinds.Reinforcement, QuantityKinds.StructuralSteel,
            QuantityKinds.Formwork, QuantityKinds.Masonry, QuantityKinds.BlindingConcrete, QuantityKinds.Excavation
        };

        // ------------------------------------------------------------------ gabarit
        /// <summary>
        /// Gabarit a remplir par le metreur. Les quantites du MODELE n'y figurent PAS, volontairement :
        /// un metre manuel fait a cote du resultat a verifier n'est plus independant.
        /// </summary>
        public static void WriteTemplate(string path, IReadOnlyList<ElementQuantity> quantities, BoqResult boq, string projectTitle)
        {
            using (var wb = new XLWorkbook())
            {
                IXLWorksheet ws = wb.AddWorksheet(ElementSheet);
                string[] h = { "Document", "ElementId", "Nature", "Unité", "Catégorie", "Type", "Niveau", "Matériau", "Quantité manuelle", "Commentaire (méthode, cotes)" };
                int[] w = { 16, 11, 16, 7, 20, 28, 12, 22, 16, 50 };
                Header(ws, h, w, 3);
                ws.Cell(1, 1).Value = "MÉTRÉ MANUEL DE VALIDATION — " + projectTitle +
                                      " — saisir la quantité NETTE mesurée à la main (cellules jaunes), selon le protocole VALIDATION.md. " +
                                      "Les quantités du modèle ne sont volontairement pas affichées.";
                ws.Range(1, 1, 1, h.Length).Merge().Style.Font.SetBold().Font.SetFontColor(Blue).Alignment.SetWrapText();
                ws.Row(1).Height = 32;
                int r = 4;
                foreach (var g in quantities
                             .Where(q => !q.ExcludedByUser && Kinds.Contains(q.Kind))
                             .GroupBy(q => new { q.SourceDocument, q.ElementId, q.Kind })
                             .OrderBy(g => Array.IndexOf(Kinds, g.Key.Kind))
                             .ThenBy(g => g.First().Category, StringComparer.Ordinal)
                             .ThenBy(g => g.First().Level, StringComparer.Ordinal)
                             .ThenBy(g => g.Key.ElementId))
                {
                    ElementQuantity q = g.First();
                    ws.Cell(r, 1).Value = q.SourceDocument ?? string.Empty;
                    ws.Cell(r, 2).Value = q.ElementId;
                    ws.Cell(r, 3).Value = q.Kind;
                    ws.Cell(r, 4).Value = q.Unit.Symbol();
                    ws.Cell(r, 5).Value = q.CategoryLabel ?? q.Category;
                    ws.Cell(r, 6).Value = q.TypeName ?? string.Empty;
                    ws.Cell(r, 7).Value = q.Level ?? string.Empty;
                    ws.Cell(r, 8).Value = string.Join(" + ", g.Select(x => x.Material).Where(m => !string.IsNullOrEmpty(m)).Distinct());
                    ws.Cell(r, 9).Style.Fill.SetBackgroundColor(Input).NumberFormat.SetFormat("#,##0.000");
                    r++;
                }
                ws.Range(3, 1, Math.Max(3, r - 1), h.Length).SetAutoFilter();
                ws.SheetView.FreezeRows(3);

                IXLWorksheet wa = wb.AddWorksheet(ArticleSheet);
                string[] ha = { "Code", "Désignation", "Unité", "Quantité manuelle NETTE", "Commentaire" };
                Header(wa, ha, new[] { 12, 70, 7, 22, 50 }, 3);
                wa.Cell(1, 1).Value = "Facultatif : quantité nette par article (avant pertes et coefficients), pour comparer les totaux du BOQ.";
                wa.Range(1, 1, 1, ha.Length).Merge().Style.Font.SetItalic();
                r = 4;
                foreach (BoqLine line in boq?.AllLines.Where(l => !l.IsManual) ?? Enumerable.Empty<BoqLine>())
                {
                    wa.Cell(r, 1).Value = line.Code;
                    wa.Cell(r, 2).Value = line.Designation;
                    wa.Cell(r, 3).Value = line.Unit.Symbol();
                    wa.Cell(r, 4).Style.Fill.SetBackgroundColor(Input).NumberFormat.SetFormat("#,##0.000");
                    r++;
                }
                wb.SaveAs(path);
            }
        }

        /// <summary>Relit un gabarit rempli : seules les lignes dont la quantite manuelle est un nombre comptent.</summary>
        public static List<ManualEntry> ReadManual(string path)
        {
            var entries = new List<ManualEntry>();
            using (var wb = new XLWorkbook(path))
            {
                if (!wb.TryGetWorksheet(ElementSheet, out IXLWorksheet ws))
                {
                    throw new FormatException("Feuille « " + ElementSheet + " » introuvable : utilisez le gabarit généré par DanCI.");
                }
                foreach (IXLRow row in ws.RowsUsed().Where(x => x.RowNumber() > 3))
                {
                    if (!TryNumber(row.Cell(9), out decimal value) || !TryLong(row.Cell(2), out long id))
                    {
                        continue;
                    }
                    entries.Add(new ManualEntry
                    {
                        SourceDocument = row.Cell(1).GetString(),
                        ElementId = id,
                        Kind = row.Cell(3).GetString(),
                        Value = value,
                        Comment = row.Cell(10).GetString()
                    });
                }
                if (wb.TryGetWorksheet(ArticleSheet, out IXLWorksheet wa))
                {
                    foreach (IXLRow row in wa.RowsUsed().Where(x => x.RowNumber() > 3))
                    {
                        if (TryNumber(row.Cell(4), out decimal value) && !string.IsNullOrWhiteSpace(row.Cell(1).GetString()))
                        {
                            entries.Add(new ManualEntry { ArticleCode = row.Cell(1).GetString().Trim(), Value = value, Comment = row.Cell(5).GetString() });
                        }
                    }
                }
            }
            return entries;
        }

        // ------------------------------------------------------------------ rapport metre manuel / modele
        public static void WriteComparison(string path, TakeoffComparisonReport report, string projectTitle, string fingerprint)
        {
            using (var wb = new XLWorkbook())
            {
                IXLWorksheet s = wb.AddWorksheet("Synthèse");
                s.Column(1).Width = 22; s.Column(2).Width = 9; s.Column(3).Width = 12; s.Column(4).Width = 16;
                s.Column(5).Width = 16; s.Column(6).Width = 12; s.Column(7).Width = 12; s.Column(8).Width = 14; s.Column(9).Width = 18;
                s.Cell(1, 1).Value = "VALIDATION DU MÉTRÉ — " + projectTitle;
                s.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
                s.Cell(2, 1).Value = ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — moteur " + ProductInfo.QuantityEngineVersion +
                                     " — empreinte du métré " + QuantityFingerprint.Short(fingerprint) + " — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm", Fr);
                s.Cell(3, 1).Value = "Écart % = (modèle − manuel) ÷ manuel. Cible : " + TakeoffComparer.TargetTolerancePercent.ToString("0", Fr) +
                                     " % sur le TOTAL du béton et de l'acier (éléments mesurés des deux côtés) ; autres natures : " +
                                     TakeoffComparer.OtherTolerancePercent.ToString("0", Fr) + " % indicatif.";
                string[] h = { "Nature", "Unité", "Éléments", "Total modèle", "Total manuel", "Écart %", "Tolérance", "Hors tol. (élém.)", "Verdict" };
                for (int c = 0; c < h.Length; c++) s.Cell(5, c + 1).Value = h[c];
                s.Range(5, 1, 5, h.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue);
                int r = 6;
                foreach (KindSummary k in report.Kinds)
                {
                    s.Cell(r, 1).Value = k.Kind + (k.IsTarget ? " (cible)" : string.Empty);
                    s.Cell(r, 2).Value = k.Unit;
                    s.Cell(r, 3).Value = k.Compared;
                    s.Cell(r, 4).Value = k.ModelTotal;
                    s.Cell(r, 5).Value = k.ManualTotal;
                    s.Cell(r, 4).Style.NumberFormat.Format = s.Cell(r, 5).Style.NumberFormat.Format = "#,##0.000";
                    if (k.DifferencePercent.HasValue) s.Cell(r, 6).Value = k.DifferencePercent.Value / 100m;
                    s.Cell(r, 6).Style.NumberFormat.Format = "+0.00%;-0.00%";
                    s.Cell(r, 7).Value = k.TolerancePercent / 100m;
                    s.Cell(r, 7).Style.NumberFormat.Format = "0%";
                    s.Cell(r, 8).Value = k.OutOfTolerance;
                    s.Cell(r, 9).Value = k.WithinTolerance ? "✔ conforme" : "✘ à expliquer";
                    s.Range(r, 1, r, h.Length).Style.Fill.SetBackgroundColor(k.WithinTolerance ? Good : Bad);
                    r++;
                }
                r++;
                s.Cell(r, 1).Value = report.Kinds.Any(k => k.IsTarget && k.Compared > 0)
                    ? (report.TargetsMet ? "OBJECTIF ATTEINT : béton et acier dans la tolérance." : "OBJECTIF NON ATTEINT : écarts à expliquer (feuille « Par élément », colonne « Pistes »).")
                    : "Aucun élément de béton ou d'acier mesuré des deux côtés : objectif non évaluable.";
                s.Cell(r, 1).Style.Font.SetBold().Font.SetFontColor(report.TargetsMet ? XLColor.DarkGreen : XLColor.FromHtml("#C00000"));

                Rows(wb.AddWorksheet("Par élément"), report.Elements, byElement: true);
                Rows(wb.AddWorksheet("Par article"), report.Articles, byElement: false);
                wb.SaveAs(path);
            }
        }

        private static void Rows(IXLWorksheet ws, List<ComparisonRow> rows, bool byElement)
        {
            string[] h = { byElement ? "Document" : "Article", byElement ? "ElementId" : "", "Nature", "Description", "Unité",
                           "Modèle", "Manuel", "Écart", "Écart %", "Tolérance", "Statut", "Commentaire du métreur", "Pistes d'explication (à vérifier)" };
            Header(ws, h, new[] { 16, 11, 16, 40, 7, 13, 13, 12, 10, 10, 16, 30, 90 }, 1);
            int r = 2;
            foreach (ComparisonRow x in rows)
            {
                ws.Cell(r, 1).Value = byElement ? x.SourceDocument : x.ArticleCode;
                if (byElement && x.ElementId.HasValue) ws.Cell(r, 2).Value = x.ElementId.Value;
                ws.Cell(r, 3).Value = x.Kind ?? string.Empty;
                ws.Cell(r, 4).Value = x.Description ?? string.Empty;
                ws.Cell(r, 5).Value = x.Unit ?? string.Empty;
                if (x.Model.HasValue) ws.Cell(r, 6).Value = x.Model.Value;
                if (x.Manual.HasValue) ws.Cell(r, 7).Value = x.Manual.Value;
                if (x.Difference.HasValue) ws.Cell(r, 8).Value = x.Difference.Value;
                if (x.DifferencePercent.HasValue) ws.Cell(r, 9).Value = x.DifferencePercent.Value / 100m;
                ws.Cell(r, 10).Value = x.TolerancePercent / 100m;
                ws.Range(r, 6, r, 8).Style.NumberFormat.Format = "#,##0.000";
                ws.Cell(r, 9).Style.NumberFormat.Format = "+0.00%;-0.00%";
                ws.Cell(r, 10).Style.NumberFormat.Format = "0%";
                ws.Cell(r, 11).Value = Label(x.Status);
                ws.Cell(r, 12).Value = x.Comment ?? string.Empty;
                ws.Cell(r, 13).Value = string.Join(" • ", x.Hints);
                ws.Cell(r, 13).Style.Alignment.SetWrapText();
                if (x.Status == ComparisonStatus.HorsTolerance || x.Status == ComparisonStatus.AbsentDuModele)
                {
                    ws.Range(r, 1, r, h.Length).Style.Fill.SetBackgroundColor(Bad);
                }
                r++;
            }
            if (r > 2) ws.Range(1, 1, r - 1, h.Length).SetAutoFilter();
            ws.SheetView.FreezeRows(1);
        }

        public static string Label(ComparisonStatus s)
        {
            switch (s)
            {
                case ComparisonStatus.Conforme: return "Conforme";
                case ComparisonStatus.HorsTolerance: return "HORS TOLÉRANCE";
                case ComparisonStatus.AbsentDuModele: return "Absent du modèle";
                default: return "Non mesuré";
            }
        }

        // ------------------------------------------------------------------ rapport A / B (2026 / 2027)
        public static void WriteRunComparison(string path, RunComparisonReport report, string labelA, string labelB)
        {
            using (var wb = new XLWorkbook())
            {
                IXLWorksheet s = wb.AddWorksheet("Comparaison");
                s.Column(1).Width = 30; s.Column(2).Width = 70;
                s.Cell(1, 1).Value = "REPRODUCTIBILITÉ DU MÉTRÉ — même modèle, deux analyses";
                s.Cell(1, 1).Style.Font.SetBold().Font.SetFontSize(13).Font.SetFontColor(Blue);
                s.Cell(3, 1).Value = "Métré A"; s.Cell(3, 2).Value = labelA;
                s.Cell(4, 1).Value = "Métré B"; s.Cell(4, 2).Value = labelB;
                s.Cell(5, 1).Value = "Empreinte A"; s.Cell(5, 2).Value = report.FingerprintA + " (" + report.CountA + " quantités)";
                s.Cell(6, 1).Value = "Empreinte B"; s.Cell(6, 2).Value = report.FingerprintB + " (" + report.CountB + " quantités)";
                s.Cell(8, 1).Value = "Résultat";
                s.Cell(8, 2).Value = report.Identical
                    ? "IDENTIQUES : mêmes quantités à " + QuantityFingerprint.Decimals + " décimales près, mêmes statuts."
                    : report.Differences.Count + " quantité(s) différente(s) : causes à identifier (feuille « Différences »).";
                s.Range(8, 1, 8, 2).Style.Font.SetBold().Fill.SetBackgroundColor(report.Identical ? Good : Bad);
                s.Cell(10, 1).Value = "Pistes si différent";
                s.Cell(10, 2).Value = "Géométrie recalculée différemment d'une version à l'autre (jointures, booléens) • ouverture du fichier dans " +
                                      "une version plus récente (mise à niveau) • configuration DanCI différente entre les deux postes (comparer les dossiers config) • " +
                                      "familles ou matériaux rechargés entre les deux analyses • portée ou phase différente.";
                s.Cell(10, 2).Style.Alignment.SetWrapText();
                s.Row(10).Height = 60;

                IXLWorksheet d = wb.AddWorksheet("Différences");
                string[] h = { "Clé (document|ElementId|nature|matériau|unité)", "Description", "Unité", "Valeur A", "Valeur B", "Écart", "Fiabilité A", "Fiabilité B", "Nature de l'écart" };
                Header(d, h, new[] { 50, 45, 7, 14, 14, 12, 14, 14, 22 }, 1);
                int r = 2;
                foreach (RunDifference x in report.Differences)
                {
                    d.Cell(r, 1).Value = x.Key;
                    d.Cell(r, 2).Value = x.Description;
                    d.Cell(r, 3).Value = x.Unit;
                    if (x.ValueA.HasValue) d.Cell(r, 4).Value = x.ValueA.Value;
                    if (x.ValueB.HasValue) d.Cell(r, 5).Value = x.ValueB.Value;
                    if (x.ValueA.HasValue && x.ValueB.HasValue) d.Cell(r, 6).Value = x.ValueB.Value - x.ValueA.Value;
                    d.Range(r, 4, r, 6).Style.NumberFormat.Format = "#,##0.000000";
                    d.Cell(r, 7).Value = x.ReliabilityA ?? string.Empty;
                    d.Cell(r, 8).Value = x.ReliabilityB ?? string.Empty;
                    d.Cell(r, 9).Value = x.Nature;
                    r++;
                }
                wb.SaveAs(path);
            }
        }

        private static void Header(IXLWorksheet ws, string[] headers, int[] widths, int row)
        {
            for (int c = 0; c < headers.Length; c++)
            {
                ws.Cell(row, c + 1).Value = headers[c];
                ws.Column(c + 1).Width = widths[c];
            }
            ws.Range(row, 1, row, headers.Length).Style.Font.SetBold().Font.SetFontColor(XLColor.White).Fill.SetBackgroundColor(Blue).Alignment.SetWrapText();
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

        private static bool TryLong(IXLCell cell, out long value)
        {
            value = 0;
            if (cell.Value.IsNumber)
            {
                value = (long)cell.Value.GetNumber();
                return true;
            }
            return long.TryParse(cell.GetString().Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
        }
    }
}
