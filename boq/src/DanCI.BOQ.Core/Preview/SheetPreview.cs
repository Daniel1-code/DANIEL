using System.Collections.Generic;

namespace DanCI.BOQ.Core.Preview
{
    /// <summary>Cellule telle qu'Excel l'affichera : texte formate (formules calculees) et style principal.</summary>
    public sealed class CellPreview
    {
        public int Column { get; set; }
        public int ColumnSpan { get; set; } = 1;
        public string Text { get; set; }
        public bool Bold { get; set; }
        public bool Italic { get; set; }
        public double FontSize { get; set; } = 11;
        /// <summary>Couleurs en #RRGGBB ; null = par defaut (texte noir, fond blanc).</summary>
        public string Foreground { get; set; }
        public string Background { get; set; }
        public bool AlignRight { get; set; }
        public bool Wrap { get; set; }
    }

    public sealed class RowPreview
    {
        public int Number { get; set; }
        public double Height { get; set; } = 15;
        public List<CellPreview> Cells { get; } = new List<CellPreview>();
    }

    public sealed class SheetPreview
    {
        public string Name { get; set; }
        /// <summary>Largeurs des colonnes en caracteres Excel (1 = colonne A).</summary>
        public List<double> ColumnWidths { get; } = new List<double>();
        public List<RowPreview> Rows { get; } = new List<RowPreview>();
        /// <summary>Lignes non affichees au-dela de la limite de l'apercu.</summary>
        public int HiddenRows { get; set; }
        public int PictureCount { get; set; }
        /// <summary>Graphiques natifs de la feuille (visibles dans le fichier, pas dans l'apercu).</summary>
        public int ChartCount { get; set; }
        public bool ShowGridLines { get; set; }
    }
}
