using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Export.Excel
{
    /// <summary>Page de garde et tableau de bord du classeur.</summary>
    public static partial class BoqWorkbookWriter
    {
        public const string DashboardSheetName = "Tableau de bord";

        private sealed class SheetTheme
        {
            public XLColor Page, Panel, Text, Muted, Accent, Accent2, Ok, Warn, Critical;
            public bool Dark;
        }

        private static SheetTheme ThemeFor(ExportContext ctx) => ctx.Cover?.Theme == CoverTheme.Classique
            ? new SheetTheme
            {
                Page = XLColor.White, Panel = XLColor.FromHtml("#F3F7FB"), Text = XLColor.FromHtml("#1B2633"), Muted = XLColor.FromHtml("#5B6B7D"),
                Accent = XLColor.FromHtml("#1F4E79"), Accent2 = XLColor.FromHtml("#E07B00"), Ok = XLColor.FromHtml("#2E7D32"),
                Warn = XLColor.FromHtml("#B7791F"), Critical = XLColor.FromHtml("#C00000"), Dark = false
            }
            : new SheetTheme
            {
                Page = XLColor.FromHtml("#070D1A"), Panel = XLColor.FromHtml("#0F1A2E"), Text = XLColor.FromHtml("#E6F1FF"), Muted = XLColor.FromHtml("#8AA4C8"),
                Accent = XLColor.FromHtml("#00E5FF"), Accent2 = XLColor.FromHtml("#FF8A00"), Ok = XLColor.FromHtml("#21E6A1"),
                Warn = XLColor.FromHtml("#FFC233"), Critical = XLColor.FromHtml("#FF4D6D"), Dark = true
            };

        // ---------------------------------------------------------------- 1. Page de garde
        // Meme modele que le PDF : marque, titre, sous-titre, image, cartes d'identite, montant TTC.
        private static void Cover(IXLWorksheet ws, ExportContext ctx)
        {
            SheetTheme t = ThemeFor(ctx);
            ws.ShowGridLines = false;
            ws.Column(1).Width = 3;
            for (int c = 2; c <= 7; c++) ws.Column(c).Width = 17;
            ws.Column(8).Width = 3;
            if (t.Dark)
            {
                ws.Range(1, 1, 120, 8).Style.Fill.SetBackgroundColor(t.Page);
            }
            ProjectInfo p = ctx.Project ?? new ProjectInfo();
            int r = 2;

            string imported = ctx.Cover?.ImportedPage;
            if (!string.IsNullOrWhiteSpace(imported) && File.Exists(imported))
            {
                // Page de garde realisee a part : en tete, sur la largeur d'une page A4.
                var picture = ws.AddPicture(imported).MoveTo(ws.Cell(r, 2));
                double scale = Math.Min(1.0, 690.0 / Math.Max(1, picture.OriginalWidth));
                picture.Scale(scale);
                r += (int)Math.Ceiling(picture.OriginalHeight * scale / 20.0) + 2;
                ws.Row(r - 1).AddHorizontalPageBreak();
            }

            string logo = ctx.EffectiveLogo;
            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
            {
                ws.AddPicture(logo).MoveTo(ws.Cell(r, 2)).WithSize(150, 70);
                r += 4;
            }
            PartyInfo company = ctx.Company;
            ws.Cell(r, 2).Value = company.DisplayShort;
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(t.Accent);
            ws.Range(r, 6, r, 7).Merge().Value = string.IsNullOrWhiteSpace(p.QuoteReference) ? "Réf. —" : p.QuoteReference;
            ws.Cell(r, 6).Style.Font.SetFontColor(t.Muted).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(r).Height = 28;
            r++;
            ws.Range(r, 2, r, 5).Merge().Value = PartyInfo.JoinPublic("  ·  ", company.Name == company.DisplayShort ? null : company.Name, company.Activity, company.City).ToUpper(Fr);
            ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r, 6, r, 7).Merge().Value = "INDICE " + (string.IsNullOrWhiteSpace(p.Revision) ? "—" : p.Revision);
            ws.Cell(r, 6).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(t.Accent2).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            r += 4;

            ws.Range(r, 2, r, 7).Merge().Value = "BORDEREAU DES QUANTITÉS  &  DEVIS ESTIMATIF";
            ws.Cell(r, 2).Style.Font.SetFontSize(11).Font.SetFontColor(t.Muted)
                .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
            r++;
            ws.Range(r, 2, r, 7).Merge().Value = (ctx.ProjectTitle ?? string.Empty).ToUpper(Fr);
            ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontSize(26).Font.SetFontColor(t.Text)
                .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent)
                .Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Center);
            ws.Row(r).Height = (ctx.ProjectTitle ?? string.Empty).Length > 34 ? 70 : 38;
            r++;
            string subtitle = string.IsNullOrWhiteSpace(ctx.Cover?.Subtitle) ? p.Purpose : ctx.Cover.Subtitle;
            if (!string.IsNullOrWhiteSpace(subtitle))
            {
                ws.Range(r, 2, r, 7).Merge().Value = subtitle;
                ws.Cell(r, 2).Style.Font.SetFontSize(13).Font.SetFontColor(t.Accent)
                    .Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
                ws.Row(r).Height = 20;
                r++;
            }
            r += 2;

            string image = ctx.EffectiveProjectImage;
            if (!string.IsNullOrWhiteSpace(image) && File.Exists(image))
            {
                var pic = ws.AddPicture(image).MoveTo(ws.Cell(r, 2), 2, 2);
                double scale = Math.Min(1.0, Math.Min(680.0 / Math.Max(1, pic.OriginalWidth), 300.0 / Math.Max(1, pic.OriginalHeight)));
                pic.Scale(scale);
                int imageRows = (int)Math.Ceiling(pic.OriginalHeight * scale / 20.0) + 1;
                ws.Range(r, 2, r + imageRows - 1, 7).Style.Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(t.Accent);
                r += imageRows + 1;
            }

            // Cartes d'identite : 3 par ligne, comme dans le PDF.
            void Card(int row, int col, string label, string value)
            {
                IXLRange l = ws.Range(row, col, row, col + 1).Merge();
                l.Value = label;
                l.Style.Font.SetFontSize(7).Font.SetFontColor(t.Muted).Fill.SetBackgroundColor(t.Panel);
                IXLRange v = ws.Range(row + 1, col, row + 1, col + 1).Merge();
                v.Value = string.IsNullOrWhiteSpace(value) ? "—" : value;
                v.Style.Font.SetBold().Font.SetFontColor(t.Text).Fill.SetBackgroundColor(t.Panel).Alignment.SetWrapText()
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(row, col, row + 1, col).Style.Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent);
                ws.Row(row + 1).Height = Math.Max(ws.Row(row + 1).Height, (value ?? string.Empty).Length > 30 ? 30 : 18);
            }
            Card(r, 2, "MAÎTRE D'OUVRAGE", p.Client);
            Card(r, 4, "LIEU", p.Location);
            Card(r, 6, "DATE", string.IsNullOrWhiteSpace(p.IssueDate) ? ctx.GeneratedAt.ToString("dd/MM/yyyy", Fr) : p.IssueDate);
            r += 3;
            Card(r, 2, "MAÎTRE D'ŒUVRE", p.Architect);
            Card(r, 4, "BUREAU D'ÉTUDES", string.IsNullOrWhiteSpace(p.Engineer) ? company.DisplayShort : p.Engineer);
            Card(r, 6, "N° PROJET", p.Number);
            r += 3;

            // Fiche client : logo, coordonnees, contact.
            PartyInfo client = p.ClientParty;
            if (client != null && !client.IsEmpty)
            {
                IXLRange band = ws.Range(r, 2, r + 3, 7);
                band.Style.Fill.SetBackgroundColor(t.Panel);
                ws.Range(r, 2, r + 3, 2).Style.Border.SetLeftBorder(XLBorderStyleValues.Thick).Border.SetLeftBorderColor(t.Accent2);
                int textCol = 2;
                if (ctx.ClientLogo != null)
                {
                    ws.AddPicture(ctx.ClientLogo).MoveTo(ws.Cell(r, 2), 6, 4).WithSize(110, 60);
                    textCol = 3;
                }
                ws.Range(r, textCol, r, 7).Merge().Value = "CLIENT";
                ws.Cell(r, textCol).Style.Font.SetFontSize(7).Font.SetFontColor(t.Muted);
                ws.Range(r + 1, textCol, r + 1, 7).Merge().Value = string.IsNullOrWhiteSpace(client.Name) ? client.ShortName : client.Name;
                ws.Cell(r + 1, textCol).Style.Font.SetBold().Font.SetFontSize(12).Font.SetFontColor(t.Text);
                ws.Range(r + 2, textCol, r + 2, 7).Merge().Value = client.AddressLine;
                ws.Range(r + 3, textCol, r + 3, 7).Merge().Value =
                    PartyInfo.JoinPublic("  ·  ", PartyInfo.JoinPublic(", ", client.Contact, client.ContactRole), client.ContactLine);
                ws.Range(r + 2, textCol, r + 3, 7).Style.Font.SetFontSize(9).Font.SetFontColor(t.Muted);
                ws.Row(r + 1).Height = 20;
                r += 5;
            }

            // Montant total TTC.
            IXLRange box = ws.Range(r, 2, r + 2, 7);
            box.Style.Fill.SetBackgroundColor(t.Panel).Border.SetOutsideBorder(XLBorderStyleValues.Medium).Border.SetOutsideBorderColor(t.Accent);
            ws.Range(r, 2, r, 4).Merge().Value = "MONTANT TOTAL TTC";
            ws.Cell(r, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r + 1, 2, r + 1, 4).Merge().Value = ctx.Boq.TotalIncludingVat.ToString("N2", Fr) + " " + ctx.Boq.Currency;
            ws.Cell(r + 1, 2).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(t.Accent);
            ws.Row(r + 1).Height = 30;
            if (ctx.Boq.TotalIncludingVatSecondary.HasValue && ctx.Boq.SecondaryCurrency.HasValue)
            {
                ws.Range(r + 2, 2, r + 2, 4).Merge().Value = "soit " + ctx.Boq.TotalIncludingVatSecondary.Value.ToString("N2", Fr) + " " + ctx.Boq.SecondaryCurrency;
                ws.Cell(r + 2, 2).Style.Font.SetFontSize(9).Font.SetFontColor(t.Muted);
            }
            ws.Range(r, 5, r, 7).Merge().Value = "HT " + ctx.Boq.TotalExcludingVat.ToString("N2", Fr) + " " + ctx.Boq.Currency;
            ws.Range(r + 1, 5, r + 1, 7).Merge().Value = "TVA " + ctx.Boq.VatPercent.ToString("0.##", Fr) + " % : " + ctx.Boq.Vat.ToString("N2", Fr);
            ws.Range(r, 5, r + 1, 7).Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right).Font.SetFontColor(t.Text);
            if (!ctx.Boq.IsComplete)
            {
                ws.Range(r + 2, 5, r + 2, 7).Merge().Value = "TOTAL INCOMPLET (minorant)";
                ws.Cell(r + 2, 5).Style.Font.SetBold().Font.SetFontColor(t.Critical).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            }
            r += 4;

            // Informations complementaires (champs renseignes seulement).
            var rows = new List<(string, string)>
            {
                ("Objet", p.Purpose),
                ("Entreprise", p.Contractor),
                ("Établi par", string.IsNullOrWhiteSpace(p.Author) ? company.DisplayShort : p.Author),
                ("Vérifié par", p.CheckedBy),
                ("Statut", p.Status),
                ("Validité de l'offre", string.IsNullOrWhiteSpace(p.ValidityDays) ? null : p.ValidityDays + " jours"),
                ("Délai d'exécution", p.ExecutionTime),
                ("Conditions de paiement", p.PaymentTerms),
                ("Maquette", ctx.DocumentTitle),
                ("Portée du métré", ctx.ScopeDescription),
                ("Généré le", ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr) + " — " + ProductInfo.Name + " " + ProductInfo.ApplicationVersion +
                              " (moteur " + ProductInfo.QuantityEngineVersion + ")"),
                ("Empreinte du métré", DanCI.BOQ.Core.Validation.QuantityFingerprint.Short(ctx.Fingerprint) +
                                       " (SHA-256 des quantités : deux analyses du même modèle donnent la même empreinte)")
            };
            foreach ((string label, string value) in rows.Where(x => !string.IsNullOrWhiteSpace(x.Item2)))
            {
                ws.Range(r, 2, r, 3).Merge().Value = label.ToUpper(Fr);
                ws.Cell(r, 2).Style.Font.SetFontSize(7).Font.SetFontColor(t.Muted).Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                ws.Range(r, 4, r, 7).Merge().Value = value;
                ws.Cell(r, 4).Style.Font.SetFontColor(t.Text).Font.SetFontSize(9).Alignment.SetWrapText();
                ws.Row(r).Height = value.Length > 70 ? 26 : 14;
                r++;
            }
            r++;
            if (!string.IsNullOrWhiteSpace(p.Notes))
            {
                ws.Range(r, 2, r + 1, 7).Merge().Value = p.Notes;
                ws.Range(r, 2, r + 1, 7).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontColor(t.Text);
                r += 3;
            }
            string coords = PartyInfo.JoinPublic("  ·  ", company.Name, company.AddressLine, company.ContactLine, company.LegalLine, company.Bank);
            if (!string.IsNullOrWhiteSpace(coords))
            {
                ws.Range(r, 2, r + 1, 7).Merge().Value = coords;
                ws.Range(r, 2, r + 1, 7).Style.Alignment.SetWrapText().Font.SetFontSize(8).Font.SetFontColor(t.Text)
                    .Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(t.Accent);
                r += 3;
            }
            ws.Range(r, 2, r + 2, 7).Merge().Value =
                "Les quantités sont issues de la maquette numérique ; chaque ligne est traçable jusqu'aux éléments " +
                "(feuille « Détail du métré »). Les quantités estimées (ratios, hypothèses) sont signalées et justifiées " +
                "(feuilles « Audit » et « Hypothèses »). Document à vérifier par l'ingénieur ou le métreur responsable avant signature.";
            ws.Range(r, 2, r + 2, 7).Style.Alignment.SetWrapText().Alignment.SetVertical(XLAlignmentVerticalValues.Top).Font.SetFontColor(t.Muted).Font.SetFontSize(8);
            r += 3;
            ws.PageSetup.PrintAreas.Add(1, 1, r + 1, 8);
            SetupPrint(ws, ctx, XLPageOrientation.Portrait);
        }

        // ---------------------------------------------------------------- 2. Tableau de bord
        // Meme modele que la fenetre et le PDF : 7 tuiles, lots | voyants, Pareto | quantites cles.
        private const int DashFirstCol = 2;   // B
        private const int DashLastCol = 15;   // O
        private const int DashMidCol = 9;     // I : fin du panneau gauche

        private static void DashboardSheet(IXLWorksheet ws, ExportContext ctx, BoqSheetMap map)
        {
            BoqDashboard d = ctx.Dashboard;
            SheetTheme t = ThemeFor(ctx);
            string cur = d.Currency;
            ws.ShowGridLines = false;
            ws.Column(1).Width = 2;
            for (int c = DashFirstCol; c <= DashLastCol; c++) ws.Column(c).Width = 11;
            ws.Column(DashLastCol + 1).Width = 2;
            if (t.Dark)
            {
                ws.Range(1, 1, 200, DashLastCol + 1).Style.Fill.SetBackgroundColor(t.Page);
            }

            ws.Range(1, 2, 1, 11).Merge().Value = "TABLEAU DE BORD DE PILOTAGE";
            ws.Cell(1, 2).Style.Font.SetBold().Font.SetFontSize(18).Font.SetFontColor(t.Accent);
            ws.Row(1).Height = 30;
            ws.Range(2, 2, 2, 11).Merge().Value = ctx.ProjectTitle + " — indice " + (string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "—" : ctx.Project.Revision) +
                                                   " — " + ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", Fr);
            ws.Cell(2, 2).Style.Font.SetFontColor(t.Muted);
            ws.Range(1, 13, 1, 15).Merge().Value = "Note de santé";
            ws.Cell(1, 13).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                .Alignment.SetVertical(XLAlignmentVerticalValues.Bottom);
            ws.Range(2, 13, 2, 15).Merge().Value = d.HealthScore;
            ws.Cell(2, 13).Style.NumberFormat.Format = "0\" / 100\"";
            ws.Cell(2, 13).Style.Font.SetBold().Font.SetFontSize(20).Font.SetFontColor(d.HealthScore >= 85 ? t.Ok : d.HealthScore >= 60 ? t.Warn : t.Critical)
                .Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right);
            ws.Row(2).Height = 28;

            // 7 tuiles de 2 colonnes (B a O), comme la fenetre. Totaux = FORMULES vers la feuille BOQ.
            int r = 4;
            string money = MoneyFormat + "\" " + cur + "\"";
            Tile(ws, t, r, 2, "TOTAL TRAVAUX HT", "'BOQ'!" + map.WorksTotal, money, null, "majorations " + d.MarkupsTotal.ToString("N2", Fr));
            Tile(ws, t, r, 4, "TOTAL TTC", "'BOQ'!" + map.TotalIncludingVat, money, d.IsComplete ? t.Accent : t.Warn,
                d.TotalIncludingVatSecondary.HasValue ? "soit " + d.TotalIncludingVatSecondary.Value.ToString("N2", Fr) + " " + d.SecondaryCurrency
                    : "TVA " + d.VatPercent.ToString("0.##", Fr) + " % : " + d.Vat.ToString("N2", Fr));
            if (d.CostPerSquareMeter.HasValue && d.ReferenceArea.HasValue)
            {
                Tile(ws, t, r, 6, "COÛT TTC AU M²", "'BOQ'!" + map.TotalIncludingVat + "/" + d.ReferenceArea.Value.ToString(CultureInfo.InvariantCulture),
                    MoneyFormat, null, d.ReferenceArea.Value.ToString("N2", Fr) + " m² " + (d.ReferenceAreaLabel ?? string.Empty));
            }
            else
            {
                TileText(ws, t, r, 6, "COÛT TTC AU M²", "—", t.Muted, "surface de référence à saisir");
            }
            TileValue(ws, t, r, 8, "PRIX RENSEIGNÉS", d.PriceCoverage, "0.0%", d.PriceCoverage >= 1m ? t.Ok : t.Critical, d.PricedLineCount + " / " + d.LineCount + " lignes");
            if (d.ReliabilityIndex.HasValue)
                TileValue(ws, t, r, 10, "FIABILITÉ", d.ReliabilityIndex.Value, "0.0%", d.ReliabilityIndex >= 0.8m ? t.Ok : t.Warn, "part mesurée du montant");
            else
                TileText(ws, t, r, 10, "FIABILITÉ", "—", t.Muted, "aucun montant");
            TileText(ws, t, r, 12, "AUDIT", d.AuditErrors + " err. · " + d.AuditWarnings + " av.", d.AuditErrors > 0 ? t.Critical : d.AuditWarnings > 0 ? t.Warn : t.Ok,
                d.ElementCount + " éléments · " + d.ExcludedCount + " exclus");
            TileText(ws, t, r, 14, "RATIO ACIER", d.RebarRatio.HasValue ? d.RebarRatio.Value.ToString("N0", Fr) + " kg/m³" : "—", null, "armatures / béton (nets)");
            if (t.Dark)
            {
                // Liseré de couleur propre a chaque tuile, comme dans la fenetre.
                string[] accents = { Palette.Blue, Palette.Green, Palette.Violet, Palette.Amber, Palette.Teal, Palette.Magenta, Palette.Orange };
                for (int i = 0; i < accents.Length; i++)
                {
                    ws.Range(r, 2 + 2 * i, r, 3 + 2 * i).Style.Border.SetTopBorder(XLBorderStyleValues.Thick).Border.SetTopBorderColor(XLColor.FromHtml(accents[i]));
                }
            }
            r += 4;

            // Panneaux cote a cote : Repartition par lot | Voyants.
            int left = r, right = r;
            PanelTitle(ws, t, left, DashFirstCol, DashMidCol, "RÉPARTITION PAR LOT");
            left++;
            decimal max = d.Lots.Count == 0 ? 0m : d.Lots.Max(l => l.Share);
            int lotFirst = left;
            foreach (LotShare lot in d.Lots)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                XLColor lotColor = t.Dark ? XLColor.FromHtml(Palette.ForLot(lot.Code)) : (lot.IsComplete ? t.Accent : t.Warn);
                ws.Cell(left, 2).Value = lot.Code;
                ws.Cell(left, 2).Style.Font.SetBold().Font.SetFontColor(t.Dark ? lotColor : t.Muted);
                ws.Range(left, 3, left, 5).Merge().Value = lot.Designation + (lot.IsComplete ? string.Empty : "  (" + lot.LinesWithoutPrice + " sans prix)");
                // Barre : caracteres pleins proportionnels a la part (formule vivante sur la part).
                ws.Range(left, 6, left, 7).Merge();
                ws.Cell(left, 6).FormulaA1 = "REPT(\"█\",ROUND(IF(MAX(I$" + lotFirst + ":I$" + (lotFirst + d.Lots.Count - 1) + ")=0,0,I" + left +
                                                              "/MAX(I$" + lotFirst + ":I$" + (lotFirst + d.Lots.Count - 1) + "))*14,0))";
                ws.Cell(left, 6).Style.Font.SetFontColor(lotColor).Font.SetFontSize(8);
                ws.Cell(left, 8).FormulaA1 = "'BOQ'!" + map.LotSubtotals[lot.Code];
                ws.Cell(left, 8).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(left, 9).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,H" + left + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(left, 9).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).Style.Font.SetFontColor(t.Accent);
                left++;
            }

            PanelTitle(ws, t, right, DashMidCol + 2, DashLastCol, "VOYANTS");
            right++;
            foreach (DashboardSignal s in d.Signals)
            {
                XLColor color = s.Level == SignalLevel.Critique ? t.Critical : s.Level == SignalLevel.Attention ? t.Warn : s.Level == SignalLevel.Info ? t.Accent : t.Ok;
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Cell(right, DashMidCol + 2).Value = "●";
                ws.Cell(right, DashMidCol + 2).Style.Font.SetFontColor(color).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Right)
                    .Alignment.SetVertical(XLAlignmentVerticalValues.Top);
                IXLRange text = ws.Range(right, DashMidCol + 3, right, DashLastCol).Merge();
                text.Value = s.Title;
                text.Style.Font.SetBold().Font.SetFontColor(t.Text);
                right++;
                string more = string.Join("  —  ", new[] { s.Detail, s.Action }.Where(x => !string.IsNullOrWhiteSpace(x)));
                if (more.Length > 0)
                {
                    PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                    IXLRange m = ws.Range(right, DashMidCol + 3, right, DashLastCol).Merge();
                    m.Value = more;
                    m.Style.Font.SetFontSize(8).Font.SetFontColor(color).Alignment.SetWrapText();
                    ws.Row(right).Height = more.Length > 75 ? 24 : 13;
                    right++;
                }
            }
            r = Math.Max(left, right) + 1;

            // Panneaux cote a cote : Pareto | Quantites cles.
            left = r;
            right = r;
            PanelTitle(ws, t, left, DashFirstCol, DashMidCol, "PARETO — LES ARTICLES QUI FONT LE DEVIS");
            left++;
            int paretoFirst = left;
            foreach (ParetoLine l in d.Pareto)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Cell(left, 2).Value = l.Rank;
                ws.Cell(left, 2).Style.Font.SetBold().Font.SetFontColor(t.Accent2).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
                ws.Cell(left, 3).Value = l.Code;
                ws.Cell(left, 3).Style.Font.SetFontColor(t.Muted);
                ws.Range(left, 4, left, 6).Merge().Value = l.Designation + (l.Reliability == Reliability.Estimated ? " (E)" : string.Empty);
                ws.Cell(left, 7).FormulaA1 = map.LineAmounts.TryGetValue(l.Code, out string a) ? "'BOQ'!" + a : l.Amount.ToString(CultureInfo.InvariantCulture);
                ws.Cell(left, 7).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(left, 8).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,G" + left + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(left, 8).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).FormulaA1 = "SUM(H$" + paretoFirst + ":H" + left + ")";
                ws.Cell(left, 9).Style.NumberFormat.Format = "0.0%";
                ws.Cell(left, 9).Style.Font.SetFontColor(t.Accent2);
                left++;
            }
            if (d.Pareto.Count == 0)
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Range(left, 2, left, DashMidCol).Merge().Value = "Aucun montant pour l'instant : saisir les prix unitaires (Paramètres du projet > Prix unitaires).";
                ws.Cell(left, 2).Style.Font.SetFontColor(t.Warn);
                left++;
            }
            else
            {
                PanelRow(ws, t, left, DashFirstCol, DashMidCol);
                ws.Range(left, 2, left, DashMidCol).Merge().Value = "Colonnes : montant HT, part, part cumulée du total des travaux.";
                ws.Cell(left, 2).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
                left++;
            }

            PanelTitle(ws, t, right, DashMidCol + 2, DashLastCol, "QUANTITÉS CLÉS (NETTES)");
            right++;
            foreach (KeyQuantity k in d.KeyQuantities)
            {
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Range(right, DashMidCol + 2, right, DashMidCol + 3).Merge().Value = k.Label;
                ws.Range(right, DashMidCol + 4, right, DashMidCol + 5).Merge().Value = k.Value;
                ws.Cell(right, DashMidCol + 4).Style.NumberFormat.Format = (k.Unit == BoqUnit.Kilogram ? "#,##0" : "#,##0.00") + "\" " + k.Unit.Symbol() + "\"";
                ws.Cell(right, DashMidCol + 4).Style.Font.SetBold();
                ws.Cell(right, DashMidCol + 6).Value = k.MeasuredShare;
                ws.Cell(right, DashMidCol + 6).Style.NumberFormat.Format = "0.0%\" mesuré\"";
                ws.Cell(right, DashMidCol + 6).Style.Font.SetFontSize(8).Font.SetFontColor(k.MeasuredShare >= 1m ? t.Ok : t.Warn);
                right++;
            }
            if (d.RebarRatio.HasValue)
            {
                PanelRow(ws, t, right, DashMidCol + 2, DashLastCol);
                ws.Range(right, DashMidCol + 2, right, DashMidCol + 3).Merge().Value = "Ratio armatures / béton";
                ws.Range(right, DashMidCol + 4, right, DashMidCol + 5).Merge().Value = d.RebarRatio.Value;
                ws.Cell(right, DashMidCol + 4).Style.NumberFormat.Format = "#,##0\" kg/m³\"";
                right++;
            }
            r = Math.Max(left, right) + 1;

            if (d.MissingPrices.Count > 0)
            {
                PanelTitle(ws, t, r, DashFirstCol, DashLastCol, "PRIX À SAISIR (" + d.MissingPrices.Count + ")");
                ws.Cell(r, DashFirstCol).Style.Font.SetFontColor(t.Critical);
                r++;
                foreach (MissingPrice m in d.MissingPrices)
                {
                    PanelRow(ws, t, r, DashFirstCol, DashLastCol);
                    ws.Cell(r, 2).Value = m.Code;
                    ws.Cell(r, 2).Style.Font.SetFontColor(t.Critical);
                    ws.Range(r, 3, r, 11).Merge().Value = m.Designation;
                    ws.Range(r, 12, r, 13).Merge().Value = m.Quantity;
                    ws.Cell(r, 12).Style.NumberFormat.Format = "#,##0.00";
                    ws.Cell(r, 14).Value = m.Unit.Symbol();
                    r++;
                }
                r++;
            }
            r = ChartsAndTables(ws, ctx, map, t, r);
            ws.Range(r, 2, r + 1, DashLastCol).Merge().Value =
                "Indicateurs calculés uniquement à partir du BOQ, de l'audit et du métré de cette analyse ; les totaux sont des formules vers la feuille BOQ. " +
                "La note de santé (prix 50, fiabilité 30, audit 20) aide au pilotage : elle ne remplace pas la vérification du métreur.";
            ws.Range(r, 2, r + 1, DashLastCol).Style.Alignment.SetWrapText().Font.SetItalic().Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.PageSetup.PrintAreas.Add(1, 1, r + 2, DashLastCol + 1);
            SetupPrint(ws, ctx, XLPageOrientation.Landscape);
        }

        /// <summary>
        /// Graphiques natifs (lots, repartition, articles, statuts) puis tableaux Excel filtrables.
        /// Les graphiques lisent ces tableaux et ne montrent que les lignes visibles : filtrer un
        /// tableau (fleche ▼) met les graphiques a jour.
        /// </summary>
        private static int ChartsAndTables(IXLWorksheet ws, ExportContext ctx, BoqSheetMap map, SheetTheme t, int r)
        {
            BoqResult boq = ctx.Boq;
            string sheetRef = "'" + ws.Name + "'!";
            PanelTitle(ws, t, r, DashFirstCol, DashLastCol, "GRAPHIQUES — ils suivent les filtres des tableaux ci-dessous");
            r++;
            int chartRow1 = r;          // deux rangees de deux graphiques, 16 lignes chacune
            int chartRow2 = r + 17;
            r = chartRow2 + 17;

            // ---- Tableau des lots (filtrable). La designation est en derniere colonne : elle deborde a droite.
            PanelTitle(ws, t, r, DashFirstCol, DashLastCol, "DONNÉES FILTRABLES — cliquez sur ▼ dans l'en-tête d'une colonne pour filtrer");
            r += 2;
            string[] lotHeaders = { "Lot", "Montant HT", "Part", "Lignes", "Sans prix", "État", "Désignation du lot" };
            int lotHeader = r;
            WriteHeader(ws, t, r, 2, lotHeaders);
            r++;
            int lotFirst = r;
            foreach (BoqLotResult lot in boq.Lots)
            {
                ws.Range(r, 2, r, DashLastCol).Style.Fill.SetBackgroundColor(t.Panel).Font.SetFontColor(t.Text);
                ws.Cell(r, 2).Value = lot.Code;
                ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.ForLot(lot.Code)) : t.Accent);
                ws.Cell(r, 3).FormulaA1 = "'BOQ'!" + map.LotSubtotals[lot.Code];
                ws.Cell(r, 3).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 3).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.Green) : t.Text);
                ws.Cell(r, 4).FormulaA1 = "IF('BOQ'!" + map.WorksTotal + "=0,0,C" + r + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(r, 4).Style.NumberFormat.Format = "0.0%";
                ws.Cell(r, 5).Value = lot.Lines.Count;
                ws.Cell(r, 6).Value = lot.Lines.Count(l => !l.Amount.HasValue);
                ws.Cell(r, 7).Value = lot.IsComplete ? "Complet" : "Incomplet";
                ws.Cell(r, 7).Style.Font.SetFontColor(lot.IsComplete ? t.Ok : t.Warn);
                ws.Cell(r, 8).Value = lot.Designation;
                r++;
            }
            int lotLast = r - 1;
            if (lotLast >= lotFirst)
            {
                IXLTable lots = ws.Range(lotHeader, 2, lotLast, 8).CreateTable("T_Lots");
                lots.Theme = XLTableTheme.None;
                lots.ShowRowStripes = false;
            }
            r += 2;

            // ---- Tableau des articles (filtrable), tries par montant decroissant ; designation en dernier.
            string[] artHeaders = { "Lot", "Code", "Unité", "Quantité", "PU", "Montant HT", "Part", "Statut", "Prix", "Désignation" };
            int artHeader = r;
            WriteHeader(ws, t, r, 2, artHeaders);
            r++;
            int artFirst = r;
            List<BoqLine> lines = boq.AllLines.OrderByDescending(l => l.Amount ?? -1m).ThenBy(l => l.Code, StringComparer.Ordinal).ToList();
            EstimateSettings settings = ctx.Configuration?.Settings ?? new EstimateSettings();
            foreach (BoqLine line in lines)
            {
                ws.Range(r, 2, r, DashLastCol).Style.Fill.SetBackgroundColor(t.Panel).Font.SetFontColor(t.Text);
                ws.Cell(r, 2).Value = line.LotCode;
                ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.ForLot(line.LotCode)) : t.Accent);
                ws.Cell(r, 3).Value = line.Code;
                ws.Cell(r, 4).Value = line.Unit.Symbol();
                ws.Cell(r, 5).Value = line.Quantity;
                ws.Cell(r, 5).Style.NumberFormat.Format = QuantityFormat(settings.DecimalsFor(line.Unit));
                if (line.UnitPrice.HasValue) ws.Cell(r, 6).Value = line.UnitPrice.Value;
                ws.Cell(r, 6).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 6).Style.Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.Amber) : t.Text);
                ws.Cell(r, 7).FormulaA1 = map.LineAmounts.TryGetValue(line.Code, out string amount) ? "'BOQ'!" + amount : "\"\"";
                ws.Cell(r, 7).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 7).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.Green) : t.Text);
                ws.Cell(r, 8).FormulaA1 = "IF(OR(G" + r + "=\"\",'BOQ'!" + map.WorksTotal + "=0),0,G" + r + "/'BOQ'!" + map.WorksTotal + ")";
                ws.Cell(r, 8).Style.NumberFormat.Format = "0.0%";
                ws.Cell(r, 9).Value = StatusLabel(line.Reliability);
                ws.Cell(r, 9).Style.Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.ForReliability(line.Reliability)) : t.Text);
                ws.Cell(r, 10).Value = line.Amount.HasValue ? "Renseigné" : "Manquant";
                ws.Cell(r, 10).Style.Font.SetFontColor(line.Amount.HasValue ? t.Ok : t.Critical);
                ws.Cell(r, 11).Value = line.Designation;
                r++;
            }
            int artLast = r - 1;
            if (artLast >= artFirst)
            {
                IXLTable arts = ws.Range(artHeader, 2, artLast, 11).CreateTable("T_Articles");
                arts.Theme = XLTableTheme.None;
                arts.ShowRowStripes = false;
            }
            r += 2;

            // ---- Montant par statut (somme des articles)
            string[] statusHeaders = { "Statut", "Montant HT", "Lignes" };
            int stHeader = r;
            WriteHeader(ws, t, r, 2, statusHeaders);
            r++;
            int stFirst = r;
            var statuses = new[] { Reliability.Measured, Reliability.Estimated, Reliability.Manual, Reliability.Missing };
            foreach (Reliability st in statuses)
            {
                ws.Range(r, 2, r, 4).Style.Fill.SetBackgroundColor(t.Panel).Font.SetFontColor(t.Text);
                ws.Cell(r, 2).Value = StatusLabel(st);
                ws.Cell(r, 2).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml(Palette.ForReliability(st)) : t.Text);
                ws.Cell(r, 3).FormulaA1 = artLast >= artFirst ? "SUMIF(I" + artFirst + ":I" + artLast + ",B" + r + ",G" + artFirst + ":G" + artLast + ")" : "0";
                ws.Cell(r, 3).Style.NumberFormat.Format = MoneyFormat;
                ws.Cell(r, 4).FormulaA1 = artLast >= artFirst ? "COUNTIF(I" + artFirst + ":I" + artLast + ",B" + r + ")" : "0";
                r++;
            }
            int stLast = r - 1;
            IXLTable stTable = ws.Range(stHeader, 2, stLast, 4).CreateTable("T_Statuts");
            stTable.Theme = XLTableTheme.None;
            stTable.ShowRowStripes = false;
            r += 1;

            // ---- Graphiques natifs, relies aux tableaux ci-dessus
            if (lotLast >= lotFirst)
            {
                ChartSpec lotsChart = Chart(ws, t, ChartKind.Column, "Montant HT par lot", sheetRef, "B", lotFirst, lotLast, "C", 1, chartRow1, 8, chartRow1 + 16);
                ChartSpec lotsDonut = Chart(ws, t, ChartKind.Doughnut, "Répartition du montant par lot", sheetRef, "H", lotFirst, lotLast, "C", 8, chartRow1, 15, chartRow1 + 16);
                foreach (BoqLotResult lot in boq.Lots)
                {
                    string color = Palette.ForLot(lot.Code);
                    lotsChart.PointColors.Add(color);
                    lotsDonut.PointColors.Add(color);
                    lotsChart.Values.Add((double)lot.Total);
                    lotsDonut.Values.Add((double)lot.Total);
                    lotsChart.Categories.Add(lot.Code);
                    lotsDonut.Categories.Add(lot.Designation);
                }
                map.Charts.Add(lotsChart);
                map.Charts.Add(lotsDonut);
            }
            if (artLast >= artFirst)
            {
                ChartSpec artChart = Chart(ws, t, ChartKind.Bar, "Montant HT par article (filtrable)", sheetRef, "C", artFirst, artLast, "G", 1, chartRow2, 8, chartRow2 + 16);
                foreach (BoqLine line in lines)
                {
                    artChart.Categories.Add(line.Code);
                    artChart.Values.Add((double)(line.Amount ?? 0m));
                    artChart.PointColors.Add(Palette.ForLot(line.LotCode));
                }
                map.Charts.Add(artChart);
            }
            ChartSpec stChart = Chart(ws, t, ChartKind.Doughnut, "Montant par statut de quantité", sheetRef, "B", stFirst, stLast, "C", 8, chartRow2, 15, chartRow2 + 16);
            foreach (Reliability st in statuses)
            {
                stChart.Categories.Add(StatusLabel(st));
                stChart.Values.Add((double)lines.Where(l => l.Reliability == st).Sum(l => l.Amount ?? 0m));
                stChart.PointColors.Add(Palette.ForReliability(st));
            }
            map.Charts.Add(stChart);
            return r;
        }

        private static ChartSpec Chart(IXLWorksheet ws, SheetTheme t, ChartKind kind, string title, string sheetRef, string catCol, int first, int last,
            string valCol, int fromCol, int fromRow, int toCol, int toRow) => new ChartSpec
            {
                SheetName = ws.Name,
                Kind = kind,
                Title = title,
                SeriesName = "Montant HT",
                CategoryRef = sheetRef + "$" + catCol + "$" + first + ":$" + catCol + "$" + last,
                ValueRef = sheetRef + "$" + valCol + "$" + first + ":$" + valCol + "$" + last,
                // Ancrage OpenXML : indices a partir de 0.
                FromColumn = fromCol,
                FromRow = fromRow - 1,
                ToColumn = toCol,
                ToRow = toRow - 1,
                Dark = t.Dark,
                SeriesColor = t.Dark ? Palette.Cyan : "#1F4E79"
            };

        private static string StatusLabel(Reliability r)
        {
            switch (r)
            {
                case Reliability.Measured: return "Mesuré";
                case Reliability.Estimated: return "Estimé";
                case Reliability.Manual: return "Forfait";
                default: return "Manquant";
            }
        }

        private static void WriteHeader(IXLWorksheet ws, SheetTheme t, int r, int c, string[] headers)
        {
            for (int i = 0; i < headers.Length; i++)
            {
                ws.Cell(r, c + i).Value = headers[i];
            }
            ws.Range(r, c, r, c + headers.Length - 1).Style.Font.SetBold().Font.SetFontColor(t.Dark ? XLColor.FromHtml("#E6F1FF") : XLColor.White)
                .Fill.SetBackgroundColor(t.Dark ? XLColor.FromHtml("#13233D") : t.Accent)
                .Border.SetBottomBorder(XLBorderStyleValues.Medium).Border.SetBottomBorderColor(t.Accent);
        }

        private static void PanelTitle(IXLWorksheet ws, SheetTheme t, int r, int c1, int c2, string text)
        {
            IXLRange title = ws.Range(r, c1, r, c2);
            title.Style.Fill.SetBackgroundColor(t.Panel).Border.SetTopBorder(XLBorderStyleValues.Thin).Border.SetTopBorderColor(t.Dark ? XLColor.FromHtml("#1E3A5F") : t.Accent);
            ws.Range(r, c1, r, c2).Merge().Value = text;
            XLColor titleColor = !t.Dark ? t.Accent :
                text.StartsWith("VOYANTS", StringComparison.Ordinal) ? XLColor.FromHtml(Palette.Amber) :
                text.StartsWith("PARETO", StringComparison.Ordinal) ? XLColor.FromHtml(Palette.Orange) :
                text.StartsWith("QUANTIT", StringComparison.Ordinal) ? XLColor.FromHtml(Palette.Green) :
                text.StartsWith("PRIX", StringComparison.Ordinal) ? XLColor.FromHtml(Palette.Red) : t.Accent;
            ws.Cell(r, c1).Style.Font.SetBold().Font.SetFontColor(titleColor);
            ws.Row(r).Height = Math.Max(ws.Row(r).Height, 20);
        }

        private static void PanelRow(IXLWorksheet ws, SheetTheme t, int r, int c1, int c2)
        {
            ws.Range(r, c1, r, c2).Style.Fill.SetBackgroundColor(t.Panel).Font.SetFontColor(t.Text);
        }

        private static void TileFrame(IXLWorksheet ws, SheetTheme t, int r, int c, string label, XLColor color)
        {
            IXLRange tile = ws.Range(r, c, r + 2, c + 1);
            tile.Style.Fill.SetBackgroundColor(t.Panel).Border.SetOutsideBorder(XLBorderStyleValues.Thin).Border.SetOutsideBorderColor(color ?? t.Accent);
            ws.Range(r, c, r, c + 1).Merge().Value = label;
            ws.Cell(r, c).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Range(r + 1, c, r + 1, c + 1).Merge();
            ws.Cell(r + 1, c).Style.Font.SetBold().Font.SetFontSize(14).Font.SetFontColor(color ?? t.Text).Alignment.SetHorizontal(XLAlignmentHorizontalValues.Left);
            ws.Range(r + 2, c, r + 2, c + 1).Merge();
            ws.Cell(r + 2, c).Style.Font.SetFontSize(8).Font.SetFontColor(t.Muted);
            ws.Row(r + 1).Height = 24;
        }

        private static void Tile(IXLWorksheet ws, SheetTheme t, int r, int c, string label, string formula, string format, XLColor color, string sub = null)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).FormulaA1 = formula;
            ws.Cell(r + 1, c).Style.NumberFormat.Format = format;
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }

        private static void TileValue(IXLWorksheet ws, SheetTheme t, int r, int c, string label, decimal value, string format, XLColor color, string sub)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).Value = value;
            ws.Cell(r + 1, c).Style.NumberFormat.Format = format;
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }

        private static void TileText(IXLWorksheet ws, SheetTheme t, int r, int c, string label, string value, XLColor color, string sub)
        {
            TileFrame(ws, t, r, c, label, color);
            ws.Cell(r + 1, c).Value = value;
            ws.Cell(r + 1, c).Style.Font.SetFontSize(11);
            ws.Cell(r + 2, c).Value = sub ?? string.Empty;
        }
    }
}
