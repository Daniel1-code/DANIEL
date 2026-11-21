using System;
using System.Collections.Generic;
using System.IO;
using DanCI.BOQ.Export.Csv;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;

namespace DanCI.BOQ.Export
{
    /// <summary>Resultat d'un export : fichiers ecrits, et formats en echec (les autres sont livres quand meme).</summary>
    public sealed class ExportResult
    {
        public List<string> Files { get; } = new List<string>();
        public List<string> Errors { get; } = new List<string>();
    }

    /// <summary>Excel + PDF + CSV bruts. Un format en echec n'empeche pas les autres.</summary>
    public static class BoqExporter
    {
        public static ExportResult ExportAll(ExportContext ctx, string folder, string stem)
        {
            var result = new ExportResult();
            Directory.CreateDirectory(folder);
            string basePath = Path.Combine(folder, stem);

            Try(result, basePath + "_BOQ.xlsx", p => BoqWorkbookWriter.Write(ctx, p), "Excel");
            Try(result, basePath + "_BOQ.pdf", p => BoqPdfWriter.Write(ctx, p), "PDF");
            // CSV bruts pour d'autres outils : virgule, point decimal.
            Try(result, basePath + "_BOQ_lignes_brut.csv", p => BoqLinesCsvWriter.WriteFile(p, ctx.Boq, CsvFormat.Invariant), "CSV lignes");
            Try(result, basePath + "_metre_brut.csv", p => QuantityCsvWriter.WriteFile(p, ctx.Quantities, CsvFormat.Invariant), "CSV métré");
            return result;
        }

        private static void Try(ExportResult result, string path, Action<string> write, string label)
        {
            try
            {
                write(path);
                result.Files.Add(path);
            }
            catch (Exception ex)
            {
                result.Errors.Add(label + " : " + ex.GetType().Name + " — " + ex.Message);
            }
        }
    }
}
