using System;
using System.Globalization;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static DanCI.BOQ.Export.Pdf.PdfTheme;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// Pages « Conception » du PDF : images extraites du modele Revit (vue 3D, plans par niveau),
    /// une par page A4 paysage, dans le theme de la page de garde. Aucune image : aucune page.
    /// </summary>
    internal static class DesignPdf
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static void Pages(IDocumentContainer doc, ExportContext ctx)
        {
            var images = ctx.ExistingDesignImages;
            if (images.Count == 0) return;
            PdfTheme t = For(ctx.Cover);
            for (int i = 0; i < images.Count; i++)
            {
                DesignImage img = images[i];
                int index = i + 1;
                doc.Page(page =>
                {
                    page.Size(PageSizes.A4.Landscape());
                    page.Margin(12, Unit.Millimetre);
                    page.PageColor(t.Page);
                    page.DefaultTextStyle(x => x.FontSize(9).FontColor(t.Text));

                    page.Header().Column(h =>
                    {
                        h.Item().Row(row =>
                        {
                            string logo = ctx.EffectiveLogo;
                            if (!string.IsNullOrEmpty(logo) && File.Exists(logo))
                            {
                                row.ConstantItem(70).Height(32).Image(logo).FitArea();
                                row.ConstantItem(10);
                            }
                            row.RelativeItem().Column(c =>
                            {
                                c.Item().Text("CONCEPTION — VUES DU MODÈLE").Bold().FontSize(13).FontColor(t.Accent).LetterSpacing(0.04f);
                                c.Item().Text(Clean(ctx.ProjectTitle + (string.IsNullOrWhiteSpace(ctx.Project?.Number) ? string.Empty : " — n° " + ctx.Project.Number))).FontSize(10).FontColor(t.Muted);
                            });
                            row.ConstantItem(150).AlignRight().AlignMiddle().Text(index + " / " + images.Count).FontSize(10).Bold().FontColor(t.Accent2);
                        });
                        h.Item().PaddingTop(5).LineHorizontal(1).LineColor(t.Accent);
                    });

                    page.Content().PaddingVertical(8).Column(col =>
                    {
                        col.Item().Row(r =>
                        {
                            r.RelativeItem().Text(Clean(img.Title)).Bold().FontSize(12);
                            if (!string.IsNullOrWhiteSpace(img.Caption))
                            {
                                r.RelativeItem().AlignRight().AlignBottom().Text(Clean(img.Caption)).FontSize(9).FontColor(t.Muted);
                            }
                        });
                        // Image sur fond blanc (rendu Revit), cadre a la couleur du theme.
                        col.Item().PaddingTop(6).Extend().Border(1.2f).BorderColor(t.Accent).Background(Colors.White).Padding(6)
                           .AlignCenter().AlignMiddle().Image(img.Path).FitArea();
                    });

                    page.Footer().Row(row =>
                    {
                        row.RelativeItem().Text("Image extraite du modèle Revit le " + ctx.GeneratedAt.ToString("dd/MM/yyyy à HH:mm", Fr) +
                                                " — illustration de la conception, sans échelle ; les quantités proviennent du métré.").FontSize(7.5f).FontColor(t.Muted);
                        row.ConstantItem(120).AlignRight().Text(Clean(ctx.Company.DisplayShort)).FontSize(7.5f).FontColor(t.Muted);
                    });
                });
            }
        }
    }
}
