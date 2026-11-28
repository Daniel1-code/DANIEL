using System;
using System.Globalization;
using System.Text;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Export.Pdf
{
    /// <summary>Couleurs de la page de garde et du tableau de bord, selon le theme choisi.</summary>
    internal sealed class PdfTheme
    {
        public string Page;
        public string Panel;
        public string PanelBorder;
        public string Text;
        public string Muted;
        public string Accent;
        public string Accent2;
        public string Ok;
        public string Warn;
        public string Critical;
        public bool Dark;

        /// <summary>Nuit bleutee, cyan et orange DanCI : lisible a l'ecran comme a l'impression couleur.</summary>
        public static readonly PdfTheme Futuristic = new PdfTheme
        {
            Page = "#070D1A", Panel = "#0F1A2E", PanelBorder = "#1E3A5F", Text = "#E6F1FF", Muted = "#8AA4C8",
            Accent = "#00E5FF", Accent2 = "#FF8A00", Ok = "#21E6A1", Warn = "#FFC233", Critical = "#FF4D6D", Dark = true
        };

        public static readonly PdfTheme Classic = new PdfTheme
        {
            Page = "#FFFFFF", Panel = "#F3F7FB", PanelBorder = "#C9D6E3", Text = "#1B2633", Muted = "#5B6B7D",
            Accent = "#1F4E79", Accent2 = "#E07B00", Ok = "#2E7D32", Warn = "#B7791F", Critical = "#C00000", Dark = false
        };

        public static PdfTheme For(CoverSettings cover) => cover?.Theme == CoverTheme.Classique ? Classic : Futuristic;

        /// <summary>
        /// Fond vectoriel de la page de garde futuriste : trame technique, arcs et traits lumineux.
        /// Pur decor (aucune donnee) ; dessine en SVG, net a toute echelle.
        /// </summary>
        public static string FuturisticBackground()
        {
            var inv = CultureInfo.InvariantCulture;
            var sb = new StringBuilder();
            sb.Append("<svg xmlns='http://www.w3.org/2000/svg' width='595' height='842' viewBox='0 0 595 842' preserveAspectRatio='none'>");
            sb.Append("<defs><linearGradient id='g' x1='0' y1='0' x2='1' y2='1'><stop offset='0' stop-color='#0B1A33'/><stop offset='1' stop-color='#05080F'/></linearGradient>");
            sb.Append("<linearGradient id='c' x1='0' y1='0' x2='1' y2='0'><stop offset='0' stop-color='#00E5FF' stop-opacity='0'/><stop offset='0.5' stop-color='#00E5FF'/><stop offset='1' stop-color='#00E5FF' stop-opacity='0'/></linearGradient></defs>");
            sb.Append("<rect width='595' height='842' fill='url(#g)'/>");
            for (int x = 0; x <= 595; x += 28)
            {
                sb.AppendFormat(inv, "<line x1='{0}' y1='0' x2='{0}' y2='842' stroke='#12345A' stroke-width='0.4' stroke-opacity='0.55'/>", x);
            }
            for (int y = 0; y <= 842; y += 28)
            {
                sb.AppendFormat(inv, "<line x1='0' y1='{0}' x2='595' y2='{0}' stroke='#12345A' stroke-width='0.4' stroke-opacity='0.55'/>", y);
            }
            // Arcs concentriques (evocation d'un plan / d'un radar) en haut a droite.
            for (int i = 0; i < 5; i++)
            {
                double r = 90 + i * 38;
                sb.AppendFormat(inv, "<circle cx='560' cy='70' r='{0}' fill='none' stroke='#00E5FF' stroke-width='{1}' stroke-opacity='{2}'/>",
                    r, i == 1 ? 1.4 : 0.6, 0.55 - i * 0.09);
            }
            sb.Append("<path d='M 0 610 L 210 610 L 250 570 L 595 570' fill='none' stroke='#FF8A00' stroke-width='1.6'/>");
            sb.Append("<path d='M 0 618 L 214 618 L 254 578 L 595 578' fill='none' stroke='#FF8A00' stroke-width='0.5' stroke-opacity='0.6'/>");
            sb.Append("<rect x='0' y='300' width='595' height='1.2' fill='url(#c)'/>");
            sb.Append("<rect x='38' y='120' width='4' height='150' fill='#00E5FF'/>");
            // Points lumineux (noeuds de structure).
            int[,] nodes = { { 470, 210 }, { 520, 260 }, { 430, 300 }, { 560, 330 }, { 500, 380 } };
            for (int i = 0; i < nodes.GetLength(0); i++)
            {
                sb.AppendFormat(inv, "<circle cx='{0}' cy='{1}' r='2.2' fill='#00E5FF'/><circle cx='{0}' cy='{1}' r='6' fill='#00E5FF' fill-opacity='0.15'/>", nodes[i, 0], nodes[i, 1]);
                if (i > 0)
                {
                    sb.AppendFormat(inv, "<line x1='{0}' y1='{1}' x2='{2}' y2='{3}' stroke='#00E5FF' stroke-width='0.5' stroke-opacity='0.5'/>",
                        nodes[i - 1, 0], nodes[i - 1, 1], nodes[i, 0], nodes[i, 1]);
                }
            }
            sb.Append("<rect x='0' y='812' width='595' height='30' fill='#00E5FF' fill-opacity='0.06'/>");
            sb.Append("</svg>");
            return sb.ToString();
        }

        /// <summary>Texte compatible avec la police du PDF (espaces insecables, symboles absents).</summary>
        public static string Clean(string s) =>
            (s ?? string.Empty).Replace(' ', ' ').Replace(' ', ' ').Replace("≥", ">=").Replace("≤", "<=")
                .Replace("✅", string.Empty).Replace("⚠️", string.Empty).Replace("❌", string.Empty).Replace("✍", string.Empty).Trim();

        public static string Percent(decimal share) => (share * 100m).ToString("0.0", CultureInfo.GetCultureInfo("fr-FR")) + " %";

        public static string Money(decimal value, int decimals = 2) =>
            Clean(value.ToString("N" + Math.Max(0, decimals), CultureInfo.GetCultureInfo("fr-FR")));
    }
}
