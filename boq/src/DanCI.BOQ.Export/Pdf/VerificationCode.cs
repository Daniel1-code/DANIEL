using System;
using System.Globalization;
using System.Text;
using DanCI.BOQ.Core;
using QRCoder;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>
    /// Code QR de verification du devis. Il porte, en clair : emetteur, projet, reference,
    /// indice, date, montant TTC et l'EMPREINTE complete du metre (SHA-256). Pour verifier un
    /// devis papier : scanner le code, relancer l'analyse du meme modele et comparer l'empreinte
    /// (onglet Validation), ou la comparer a l'instantane de revision enregistre a l'export.
    /// Aucun serveur, aucune donnee envoyee : tout est dans le code.
    /// </summary>
    public static class VerificationCode
    {
        public static string Payload(ExportContext ctx)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            var sb = new StringBuilder();
            sb.Append("DanCI-BOQ ").Append(ProductInfo.ApplicationVersion).Append('\n');
            sb.Append("Emetteur: ").Append(ctx.Company?.DisplayShort).Append('\n');
            sb.Append("Projet: ").Append(ctx.ProjectTitle).Append('\n');
            if (!string.IsNullOrWhiteSpace(ctx.Project?.QuoteReference)) sb.Append("Ref: ").Append(ctx.Project.QuoteReference).Append('\n');
            sb.Append("Indice: ").Append(string.IsNullOrWhiteSpace(ctx.Project?.Revision) ? "-" : ctx.Project.Revision).Append('\n');
            sb.Append("Date: ").Append(ctx.GeneratedAt.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture)).Append('\n');
            sb.Append("TTC: ").Append(ctx.Boq.TotalIncludingVat.ToString("0.00", CultureInfo.InvariantCulture)).Append(' ').Append(ctx.Boq.Currency)
              .Append(ctx.Boq.IsComplete ? string.Empty : " (incomplet)").Append('\n');
            sb.Append("Empreinte: ").Append(ctx.Fingerprint);
            return sb.ToString();
        }

        /// <summary>Image PNG du code QR (correction d'erreur M : lisible meme legerement abime).</summary>
        public static byte[] Png(string payload, int pixelsPerModule = 6, bool dark = false)
        {
            using (var generator = new QRCodeGenerator())
            using (QRCodeData data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.M, forceUtf8: true))
            {
                var png = new PngByteQRCode(data);
                return dark
                    ? png.GetGraphic(pixelsPerModule, new byte[] { 0xE6, 0xF1, 0xFF }, new byte[] { 0x0F, 0x1A, 0x2E }, drawQuietZones: true)
                    : png.GetGraphic(pixelsPerModule);
            }
        }
    }
}
