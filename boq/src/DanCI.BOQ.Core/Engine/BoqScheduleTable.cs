using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Engine
{
    public enum ScheduleRowKind
    {
        Header,
        Level,
        Lot,
        Article,
        Subtotal,
        Total,
        Note
    }

    /// <summary>Une ligne du tableau ecrit dans la nomenclature Revit.</summary>
    public sealed class ScheduleRow
    {
        public ScheduleRowKind Kind { get; set; }
        public string[] Cells { get; set; }
    }

    /// <summary>
    /// Contenu des nomenclatures « DanCI — BOQ » et « DanCI — BOQ par niveau » du projet Revit :
    /// texte deja formate (francais), 7 colonnes. Instantane du BOQ calcule : aucune quantite
    /// recalculee ici.
    /// </summary>
    public static class BoqScheduleTable
    {
        public static readonly string[] Columns = { "N°", "Désignation", "Unité", "Quantité", "PU", "Montant", "Statut" };

        /// <summary>Largeurs des colonnes (mm sur la feuille).</summary>
        public static readonly double[] WidthsMm = { 18, 95, 12, 24, 22, 28, 22 };

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static List<ScheduleRow> Boq(BoqResult boq, DateTime generatedAt)
        {
            var rows = new List<ScheduleRow> { Row(ScheduleRowKind.Header, Columns) };
            string cur = boq.Currency.ToString();
            foreach (BoqLotResult lot in boq.Lots)
            {
                rows.Add(Row(ScheduleRowKind.Lot, "LOT " + lot.Code, lot.Designation, "", "", "", Money(lot.Total) + (lot.IsComplete ? string.Empty : " *"), ""));
                foreach (BoqLine l in lot.Lines)
                {
                    rows.Add(Row(ScheduleRowKind.Article, l.Code, l.Designation, l.Unit.Symbol(), Qty(l.Quantity, l.Unit),
                        l.UnitPrice.HasValue ? Money(l.UnitPrice.Value) : "—", l.Amount.HasValue ? Money(l.Amount.Value) : "sans prix", Status(l.Reliability)));
                }
                rows.Add(Row(ScheduleRowKind.Subtotal, "", "Sous-total lot " + lot.Code, "", "", "", Money(lot.Total), ""));
            }
            rows.Add(Row(ScheduleRowKind.Total, "", "TOTAL TRAVAUX HT", "", "", "", Money(boq.WorksTotal) + " " + cur, ""));
            foreach (MarkupLine m in boq.Markups)
            {
                rows.Add(Row(ScheduleRowKind.Subtotal, m.Code, m.Designation + " (" + m.Percent.ToString("0.##", Fr) + " %)", "", "", "", Money(m.Amount), ""));
            }
            rows.Add(Row(ScheduleRowKind.Total, "", "TOTAL HT", "", "", "", Money(boq.TotalExcludingVat) + " " + cur, ""));
            rows.Add(Row(ScheduleRowKind.Subtotal, "", "TVA " + boq.VatPercent.ToString("0.##", Fr) + " %", "", "", "", Money(boq.Vat), ""));
            rows.Add(Row(ScheduleRowKind.Total, "", "TOTAL TTC", "", "", "", Money(boq.TotalIncludingVat) + " " + cur, ""));
            rows.Add(Note(boq, generatedAt));
            return rows;
        }

        public static List<ScheduleRow> ByLevel(BoqResult boq, IEnumerable<string> levelOrder, DateTime generatedAt, string groundLevel = null)
        {
            var rows = new List<ScheduleRow> { Row(ScheduleRowKind.Header, Columns) };
            string cur = boq.Currency.ToString();
            foreach (LevelBlock b in LevelBreakdown.Build(boq, levelOrder, null, null, null, groundLevel))
            {
                rows.Add(Row(ScheduleRowKind.Level, "NIVEAU", b.Level, "", "", "", Money(b.Total) + (b.IsComplete ? string.Empty : " *"), b.ElementCount + " élém."));
                foreach (LevelLot lot in b.Lots)
                {
                    rows.Add(Row(ScheduleRowKind.Lot, "LOT " + lot.Code, lot.Designation, "", "", "", Money(lot.Total), ""));
                    foreach (LevelArticle a in lot.Articles)
                    {
                        rows.Add(Row(ScheduleRowKind.Article, a.Code, a.Designation, a.Unit.Symbol(), Qty(a.Quantity, a.Unit),
                            a.UnitPrice.HasValue ? Money(a.UnitPrice.Value) : "—", a.Amount.HasValue ? Money(a.Amount.Value) : "sans prix", Status(a.Reliability)));
                    }
                }
                rows.Add(Row(ScheduleRowKind.Subtotal, "", "Total niveau " + b.Level, "", "", "", Money(b.Total) + " " + cur, ""));
            }
            rows.Add(Row(ScheduleRowKind.Total, "", "TOTAL TRAVAUX HT (tous niveaux)", "", "", "", Money(boq.WorksTotal) + " " + cur, ""));
            rows.Add(Note(boq, generatedAt));
            return rows;
        }

        private static ScheduleRow Note(BoqResult boq, DateTime at) => Row(ScheduleRowKind.Note, "",
            "DanCI — Métré & BOQ, " + at.ToString("dd/MM/yyyy HH:mm", Fr) + ". Instantané du BOQ : relancez « Nomenclatures BOQ » après une nouvelle analyse." +
            (boq.IsComplete ? string.Empty : " * montant incomplet (lignes sans prix)."), "", "", "", "", "");

        private static ScheduleRow Row(ScheduleRowKind kind, params string[] cells) => new ScheduleRow { Kind = kind, Cells = cells };

        private static string Money(decimal v) => v.ToString("N2", Fr);

        private static string Qty(decimal v, BoqUnit u) => v.ToString(u == BoqUnit.Kilogram || u == BoqUnit.Each ? "N0" : "N2", Fr);

        private static string Status(Reliability r)
        {
            switch (r)
            {
                case Reliability.Measured: return "mesuré";
                case Reliability.Manual: return "forfait";
                case Reliability.Estimated: return "estimé";
                default: return "manquant";
            }
        }
    }
}
