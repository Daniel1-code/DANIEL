using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Periode d'execution d'un lot : mois de debut (1 = premier mois) et duree en mois.</summary>
    public sealed class LotPeriod
    {
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("debutMois")] public int StartMonth { get; set; } = 1;
        [JsonPropertyName("dureeMois")] public int DurationMonths { get; set; } = 1;
    }

    /// <summary>Planning financier du projet (saisi dans Parametres du projet > Echeancier).</summary>
    public sealed class ProjectSchedule
    {
        [JsonPropertyName("dureeMois")] public int TotalMonths { get; set; } = 12;
        [JsonPropertyName("demarrage")] public DateTime? StartDate { get; set; }
        [JsonPropertyName("lots")] public List<LotPeriod> Lots { get; set; } = new List<LotPeriod>();
    }

    public sealed class ScheduleMonth
    {
        public int Index { get; set; }
        public string Label { get; set; }
        public decimal Works { get; set; }
        public decimal Ttc { get; set; }
        public decimal CumulativeTtc { get; set; }
        public decimal CumulativeShare { get; set; }
    }

    public sealed class ScheduleLotRow
    {
        public string Lot { get; set; }
        public string Designation { get; set; }
        public decimal Total { get; set; }
        public int Start { get; set; }
        public int Duration { get; set; }
        public decimal[] Months { get; set; }
        public bool DefaultPeriod { get; set; }
    }

    public sealed class ScheduleResult
    {
        public int TotalMonths { get; set; }
        public string Currency { get; set; }
        public List<ScheduleMonth> Months { get; } = new List<ScheduleMonth>();
        public List<ScheduleLotRow> Lots { get; } = new List<ScheduleLotRow>();
        public List<string> Notes { get; } = new List<string>();
        /// <summary>Mois (1..n) qui recoit l'ecart d'arrondi du TTC : le dernier qui a des travaux.</summary>
        public int AbsorbingMonth { get; set; }

        public decimal PeakTtc => Months.Count == 0 ? 0m : Months.Max(m => m.Ttc);
    }

    /// <summary>
    /// Echeancier : le montant HT de chaque lot est reparti uniformement sur ses mois ; chaque
    /// mois porte sa part des majorations et de la TVA (proportionnelle aux travaux). Les arrondis
    /// sont repris sur le dernier mois : la somme des mois = le TTC du devis, au centime.
    /// </summary>
    public static class ScheduleCalculator
    {
        public static int LastActiveMonth(decimal[] works)
        {
            for (int i = works.Length - 1; i >= 0; i--)
            {
                if (works[i] != 0m) return i;
            }
            return works.Length - 1;
        }

        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        public static ScheduleResult Build(BoqResult boq, ProjectSchedule schedule)
        {
            if (boq == null) throw new ArgumentNullException(nameof(boq));
            schedule = schedule ?? new ProjectSchedule();
            int months = Math.Max(1, Math.Min(120, schedule.TotalMonths));
            var result = new ScheduleResult { TotalMonths = months, Currency = boq.Currency.ToString() };
            var byLot = (schedule.Lots ?? new List<LotPeriod>()).Where(p => p?.Lot != null)
                .GroupBy(p => p.Lot.Trim(), StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

            foreach (BoqLotResult lot in boq.Lots)
            {
                bool found = byLot.TryGetValue(lot.Code, out LotPeriod p);
                int start = found ? Math.Max(1, p.StartMonth) : 1;
                int duration = found ? Math.Max(1, p.DurationMonths) : months;
                if (start > months)
                {
                    result.Notes.Add("Lot " + lot.Code + " : début au mois " + start + ", après la fin du planning (" + months + " mois) : ramené au dernier mois.");
                    start = months;
                }
                if (start + duration - 1 > months)
                {
                    result.Notes.Add("Lot " + lot.Code + " : se termine après le mois " + months + " : durée réduite à " + (months - start + 1) + " mois.");
                    duration = months - start + 1;
                }
                var row = new ScheduleLotRow
                {
                    Lot = lot.Code, Designation = lot.Designation, Total = lot.Total, Start = start, Duration = duration,
                    Months = new decimal[months], DefaultPeriod = !found
                };
                decimal per = Math.Round(lot.Total / duration, 2, MidpointRounding.AwayFromZero);
                for (int m = 0; m < duration; m++)
                {
                    row.Months[start - 1 + m] = m == duration - 1 ? lot.Total - per * (duration - 1) : per;
                }
                result.Lots.Add(row);
            }
            if (result.Lots.Any(l => l.DefaultPeriod))
            {
                result.Notes.Add("Lots sans période saisie (" + string.Join(", ", result.Lots.Where(l => l.DefaultPeriod).Select(l => l.Lot)) +
                                 ") : répartis uniformément sur toute la durée. Saisissez leur période dans Paramètres du projet > Échéancier.");
            }

            // TTC du mois = travaux du mois x (TTC / travaux), arrondi ; le DERNIER MOIS AVEC TRAVAUX absorbe
            // l'ecart d'arrondi (jamais un mois vide a -0,01).
            decimal ratio = boq.WorksTotal == 0m ? 0m : boq.TotalIncludingVat / boq.WorksTotal;
            decimal[] works = Enumerable.Range(0, months).Select(m => result.Lots.Sum(l => l.Months[m])).ToArray();
            int absorb = LastActiveMonth(works);
            result.AbsorbingMonth = absorb + 1;
            decimal[] ttc = works.Select(w => Math.Round(w * ratio, 2, MidpointRounding.AwayFromZero)).ToArray();
            ttc[absorb] = boq.TotalIncludingVat - ttc.Where((_, i) => i != absorb).Sum();
            decimal cumulative = 0m;
            for (int m = 0; m < months; m++)
            {
                cumulative += ttc[m];
                result.Months.Add(new ScheduleMonth
                {
                    Index = m + 1,
                    Label = schedule.StartDate.HasValue ? schedule.StartDate.Value.AddMonths(m).ToString("MMM yyyy", Fr) : "Mois " + (m + 1),
                    Works = works[m],
                    Ttc = ttc[m],
                    CumulativeTtc = cumulative,
                    CumulativeShare = boq.TotalIncludingVat == 0m ? 0m : cumulative / boq.TotalIncludingVat
                });
            }
            if (!boq.IsComplete)
            {
                result.Notes.Add("Devis INCOMPLET (lignes sans prix) : l'échéancier sous-estime les besoins.");
            }
            return result;
        }
    }
}
