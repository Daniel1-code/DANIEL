using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Temps unitaire d'un article du BOQ (main-d'oeuvre).</summary>
    public sealed class UnitTimeRow
    {
        public string Code { get; set; }
        public string ArticleCode { get; set; }
        public string LotCode { get; set; }
        public string LotDesignation { get; set; }
        public string Designation { get; set; }
        public BoqUnit Unit { get; set; }
        public decimal Quantity { get; set; }
        /// <summary>Rendement d'une equipe (unites par jour), planning.json.</summary>
        public double PerDay { get; set; }
        /// <summary>Effectif de l'equipe (ouvriers).</summary>
        public int Workers { get; set; }
        public double HoursPerDay { get; set; }
        /// <summary>Temps unitaire : heures-homme par unite = heures/jour x effectif / rendement.</summary>
        public double UnitHours { get; set; }
        /// <summary>Heures-homme de l'article = temps unitaire x quantite.</summary>
        public double TotalHours { get; set; }
        /// <summary>Jours d'equipe = quantite / rendement.</summary>
        public double CrewDays { get; set; }
        /// <summary>Main-d'oeuvre du PU (decomposition de la bibliotheque), si saisie.</summary>
        public decimal? LabourPerUnit { get; set; }
        /// <summary>Taux horaire implicite = main-d'oeuvre du PU / temps unitaire (controle de coherence).</summary>
        public decimal? ImpliedHourlyRate { get; set; }
        /// <summary>Origine du rendement (motif d'articles, note) ou « rendement par defaut ».</summary>
        public string Basis { get; set; }
        public bool IsDefaultRate { get; set; }
    }

    /// <summary>
    /// Temps unitaires (heures-homme par unite) de chaque article du BOQ, a partir des rendements du
    /// planning : temps unitaire = heures par jour x effectif de l'equipe / rendement journalier. Heures
    /// totales, jours d'equipe, totaux par lot ; taux horaire implicite si la main-d'oeuvre du PU est
    /// decomposee. ESTIMATION : les rendements et effectifs sont des hypotheses (config\planning.json).
    /// </summary>
    public static class UnitTimes
    {
        /// <summary>Effectifs types par lot (ouvriers par equipe) quand planning.json n'en donne pas.</summary>
        public static int DefaultCrew(string lot)
        {
            switch (lot)
            {
                case "00": return 3;
                case "DEM": return 3;
                case "01": return 4;
                case "02": return 5;
                case "03": return 6;
                case "04": return 4;
                case "05": return 3;
                case "06": return 4;
                case "07": return 2;
                case "08": return 3;
                case "09": return 2;
                case "10": return 2;
                case "11": return 4;
                case "12": return 2;
                default: return 3;
            }
        }

        public static List<UnitTimeRow> Build(BoqResult boq, PlanningSettings settings, PriceLibrary library = null)
        {
            settings = settings ?? new PlanningSettings();
            var rows = new List<UnitTimeRow>();
            if (boq == null) return rows;
            double hpd = settings.HoursPerDay > 0 ? settings.HoursPerDay : 8;
            foreach (BoqLotResult lot in boq.Lots)
            {
                foreach (BoqLine line in lot.Lines.Where(l => !l.IsUnclassified && l.Quantity != 0m))
                {
                    string article = line.ArticleCode ?? line.Code;
                    ProductionRate rate = settings.RateFor(article, line.Unit);
                    bool def = !(rate?.PerDay > 0);
                    double perDay = def ? 10 : rate.PerDay;
                    int workers = rate?.Workers > 0 ? rate.Workers.Value
                        : settings.CrewSizes != null && settings.CrewSizes.TryGetValue(lot.Code, out int cs) && cs > 0 ? cs : DefaultCrew(lot.Code);
                    double unitHours = hpd * workers / perDay;
                    var row = new UnitTimeRow
                    {
                        Code = line.Code, ArticleCode = article, LotCode = lot.Code, LotDesignation = lot.Designation, Designation = line.Designation,
                        Unit = line.Unit, Quantity = line.Quantity, PerDay = perDay, Workers = workers, HoursPerDay = hpd,
                        UnitHours = unitHours, TotalHours = unitHours * (double)line.Quantity, CrewDays = (double)line.Quantity / perDay,
                        IsDefaultRate = def,
                        Basis = def ? "rendement par défaut (10 " + line.Unit.Symbol() + "/j) : aucun rendement dans planning.json"
                                    : "planning.json : " + rate.Articles + " en " + rate.Unit + (string.IsNullOrWhiteSpace(rate.Note) ? string.Empty : " (" + rate.Note + ")")
                    };
                    PriceEntry general = library?.Prices?.FirstOrDefault(p => p.Code == article && !p.IsTypeSpecific);
                    if (general?.Breakdown != null && general.Breakdown.Labour > 0m)
                    {
                        row.LabourPerUnit = general.Breakdown.Labour;
                        if (unitHours > 0) row.ImpliedHourlyRate = Math.Round(general.Breakdown.Labour / (decimal)unitHours, 2);
                    }
                    rows.Add(row);
                }
            }
            return rows;
        }

        /// <summary>Heures-homme par lot (ordre du BOQ).</summary>
        public static List<(string Lot, string Designation, double Hours, double CrewDays)> ByLot(IEnumerable<UnitTimeRow> rows) =>
            (rows ?? Enumerable.Empty<UnitTimeRow>()).GroupBy(r => (r.LotCode, r.LotDesignation))
                .Select(g => (g.Key.LotCode, g.Key.LotDesignation, g.Sum(r => r.TotalHours), g.Sum(r => r.CrewDays))).ToList();
    }
}
