using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Engine;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Ecrit le BOQ dans une NOMENCLATURE Revit du projet (arborescence « Nomenclatures/Quantités »),
    /// posable sur une feuille. Revit n'accepte dans le corps d'une nomenclature que des elements ;
    /// le BOQ n'en est pas un. Le tableau est donc ecrit dans l'EN-TETE de la nomenclature (zone de
    /// texte libre, une colonne par champ) et le corps reste vide : une nomenclature de modeles
    /// generiques dont le filtre ne retient aucun element. MODIFIE le modele (une vue) : a appeler
    /// dans une transaction. Une nomenclature existante du meme nom est mise a jour (pas recreee :
    /// elle reste posee sur ses feuilles).
    /// </summary>
    internal static class BoqScheduleWriter
    {
        /// <summary>Valeur du filtre qui ne retient aucun element (corps vide).</summary>
        private const string NoMatch = "§DanCI-BOQ-aucun-élément§";

        public static ViewSchedule Write(Document doc, string name, string title, IReadOnlyList<ScheduleRow> rows)
        {
            int columns = BoqScheduleTable.Columns.Length;
            ViewSchedule vs = new FilteredElementCollector(doc).OfClass(typeof(ViewSchedule)).Cast<ViewSchedule>()
                .FirstOrDefault(v => !v.IsTemplate && v.Name == name);
            if (vs == null)
            {
                vs = ViewSchedule.CreateSchedule(doc, new ElementId(BuiltInCategory.OST_GenericModel));
                vs.Name = name;
            }
            PrepareDefinition(vs.Definition, columns);

            TableData table = vs.GetTableData();
            TableSectionData head = table.GetSectionData(SectionType.Header);
            // L'en-tete suit normalement les colonnes du corps ; sinon on complete.
            while (head.NumberOfColumns < columns && head.CanInsertColumn(head.LastColumnNumber + 1))
            {
                head.InsertColumn(head.LastColumnNumber + 1);
            }
            if (head.NumberOfColumns != columns)
            {
                throw new InvalidOperationException("en-tête de " + head.NumberOfColumns + " colonne(s) au lieu de " + columns + ".");
            }
            // En-tete remis a zero : on garde la ligne de titre.
            while (head.NumberOfRows > 1)
            {
                head.RemoveRow(head.LastRowNumber);
            }
            int titleRow = head.FirstRowNumber;
            head.SetCellText(titleRow, head.FirstColumnNumber, title);

            TableSectionData body = table.GetSectionData(SectionType.Body);
            for (int c = 0; c < columns; c++)
            {
                body.SetColumnWidth(body.FirstColumnNumber + c, BoqScheduleTable.WidthsMm[c] / 304.8);
            }

            foreach (ScheduleRow row in rows)
            {
                int r = head.LastRowNumber + 1;
                head.InsertRow(r);
                for (int c = 0; c < columns && c < row.Cells.Length; c++)
                {
                    int col = head.FirstColumnNumber + c;
                    head.SetCellText(r, col, row.Cells[c] ?? string.Empty);
                    Style(head, r, col, row.Kind, c);
                }
            }
            return vs;
        }

        /// <summary>Champs du corps (autant que de colonnes, le premier porte le filtre) ; corps sans en-tetes ni lignes.</summary>
        private static void PrepareDefinition(ScheduleDefinition def, int columns)
        {
            def.ClearFilters();
            while (def.GetFieldCount() > 0)
            {
                def.RemoveField(0);
            }
            IList<SchedulableField> available = def.GetSchedulableFields()
                .Where(f => f.FieldType == ScheduleFieldType.Instance && f.ParameterId != ElementId.InvalidElementId)
                .ToList();
            var commentsId = new ElementId(BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS);
            SchedulableField comments = available.FirstOrDefault(f => f.ParameterId == commentsId)
                ?? throw new InvalidOperationException("champ « Commentaires » introuvable pour les modèles génériques.");
            ScheduleField filterField = def.AddField(comments);
            foreach (SchedulableField f in available.Where(f => f.ParameterId != commentsId).Take(columns - 1))
            {
                def.AddField(f);
            }
            if (def.GetFieldCount() < columns)
            {
                throw new InvalidOperationException("pas assez de champs disponibles pour " + columns + " colonnes.");
            }
            def.AddFilter(new ScheduleFilter(filterField.FieldId, ScheduleFilterType.Equal, NoMatch));
            def.ShowHeaders = false;
            def.ShowTitle = true;
            def.IsItemized = true;
        }

        private static void Style(TableSectionData head, int r, int c, ScheduleRowKind kind, int column)
        {
            TableCellStyle style = head.GetTableCellStyle(r, c);
            var options = new TableCellStyleOverrideOptions();
            bool bold = kind == ScheduleRowKind.Header || kind == ScheduleRowKind.Lot || kind == ScheduleRowKind.Level ||
                        kind == ScheduleRowKind.Subtotal || kind == ScheduleRowKind.Total;
            if (bold)
            {
                style.IsFontBold = true;
                options.Bold = true;
            }
            Color background = null;
            switch (kind)
            {
                case ScheduleRowKind.Header: background = new Color(31, 78, 121); break;
                case ScheduleRowKind.Level: background = new Color(11, 31, 58); break;
                case ScheduleRowKind.Lot: background = new Color(234, 241, 251); break;
                case ScheduleRowKind.Total: background = new Color(226, 239, 218); break;
            }
            if (background != null)
            {
                style.BackgroundColor = background;
                options.BackgroundColor = true;
            }
            if (kind == ScheduleRowKind.Header || kind == ScheduleRowKind.Level)
            {
                style.TextColor = new Color(255, 255, 255);
                options.FontColor = true;
            }
            if (column >= 3 && column <= 5 && kind != ScheduleRowKind.Header && kind != ScheduleRowKind.Note)
            {
                style.FontHorizontalAlignment = HorizontalAlignmentStyle.Right;
                options.HorizontalAlignment = true;
            }
            style.SetCellStyleOverrideOptions(options);
            head.SetCellStyle(r, c, style);
        }
    }
}
