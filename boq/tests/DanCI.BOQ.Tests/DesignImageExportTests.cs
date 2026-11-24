using System;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;
using Xunit;

namespace DanCI.BOQ.Tests
{
    /// <summary>Images de la conception (vue 3D, plans) dans le PDF et le classeur.</summary>
    public class DesignImageExportTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-design-" + Guid.NewGuid().ToString("N"));

        public DesignImageExportTests() => Directory.CreateDirectory(dir);

        public void Dispose()
        {
            try { Directory.Delete(dir, true); } catch (IOException) { }
        }

        private ExportContext WithImages(CoverTheme theme)
        {
            ExportContext ctx = ExportTests.Context();
            ctx.Cover = new CoverSettings { Theme = theme };
            ctx.DesignImages = new[]
            {
                new DesignImage { Title = "Vue 3D du modèle", Caption = "Vue axonométrique", Path = ProjectSettingsTests.Png(dir, "3d.png"), Is3D = true },
                new DesignImage { Title = "Plan — RDC", Caption = "niveau à +0,00 m", Path = ProjectSettingsTests.Png(dir, "rdc.png") },
                new DesignImage { Title = "Plan — Étage 1", Path = Path.Combine(dir, "absent.png") } // fichier manquant : ignore
            };
            return ctx;
        }

        [Theory]
        [InlineData(CoverTheme.Futuriste)]
        [InlineData(CoverTheme.Classique)]
        public void PDF_une_page_par_image_de_conception(CoverTheme theme)
        {
            ExportContext without = ExportTests.Context();
            without.Cover = new CoverSettings { Theme = theme };
            int before = BoqPdfWriter.GeneratePageImages(without, 40).Count;
            int after = BoqPdfWriter.GeneratePageImages(WithImages(theme), 40).Count;
            Assert.Equal(before + 2, after);
        }

        [Fact]
        public void Classeur_feuille_Conception_apres_le_tableau_de_bord()
        {
            string xlsx = Path.Combine(dir, "devis.xlsx");
            BoqWorkbookWriter.Write(WithImages(CoverTheme.Futuriste), xlsx);
            using (var wb = new XLWorkbook(xlsx))
            {
                var names = wb.Worksheets.Select(w => w.Name).ToList();
                Assert.Equal(names.IndexOf(BoqWorkbookWriter.DashboardSheetName) + 1, names.IndexOf(BoqWorkbookWriter.DesignSheetName));
                IXLWorksheet ws = wb.Worksheet(BoqWorkbookWriter.DesignSheetName);
                Assert.Equal(2, ws.Pictures.Count);
                Assert.Contains(ws.CellsUsed(), c => c.GetString() == "Plan — RDC");
                // Sans image de projet choisie, la vue 3D illustre la page de garde.
                Assert.Equal(1, wb.Worksheet("Page de garde").Pictures.Count);
            }
        }

        [Fact]
        public void Sans_image_aucune_feuille_ni_page_ajoutee()
        {
            ExportContext ctx = ExportTests.Context();
            string xlsx = Path.Combine(dir, "sans.xlsx");
            BoqWorkbookWriter.Write(ctx, xlsx);
            using (var wb = new XLWorkbook(xlsx))
            {
                Assert.DoesNotContain(wb.Worksheets, w => w.Name == BoqWorkbookWriter.DesignSheetName);
            }
            Assert.Null(ctx.EffectiveProjectImage);
        }

        [Fact]
        public void Image_de_projet_choisie_prioritaire_sur_la_vue_3D()
        {
            ExportContext ctx = WithImages(CoverTheme.Classique);
            string chosen = ProjectSettingsTests.Png(dir, "rendu.png");
            ctx.Cover.ProjectImage = chosen;
            Assert.Equal(chosen, ctx.EffectiveProjectImage);
            ctx.Cover.ProjectImage = null;
            Assert.EndsWith("3d.png", ctx.EffectiveProjectImage);
        }
    }
}
