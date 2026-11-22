using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Rendement d'une equipe : quantite realisee par jour ouvre, pour les articles dont le code correspond.</summary>
    public sealed class ProductionRate
    {
        [JsonPropertyName("articles")] public string Articles { get; set; }
        [JsonPropertyName("unite")] public string Unit { get; set; }
        [JsonPropertyName("parJour")] public double PerDay { get; set; }
        [JsonPropertyName("note")] public string Note { get; set; }
    }

    /// <summary>Equipes affectees a un lot (travaillent en parallele sur le meme niveau).</summary>
    public sealed class LotCrew
    {
        [JsonPropertyName("lot")] public string Lot { get; set; }
        [JsonPropertyName("equipes")] public int Crews { get; set; } = 1;
    }

    /// <summary>planning.json : hypotheses de la planification (toutes a valider par le conducteur de travaux).</summary>
    public sealed class PlanningSettings
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        /// <summary>5 (lundi-vendredi) ou 6 (lundi-samedi).</summary>
        [JsonPropertyName("joursOuvresParSemaine")] public int WorkingDaysPerWeek { get; set; } = 6;
        [JsonPropertyName("installationChantierJours")] public int SiteSetupDays { get; set; } = 10;
        /// <summary>Attente entre la fin du gros oeuvre d'un niveau et le debut du suivant (decoffrage, etaiement), jours ouvres.</summary>
        [JsonPropertyName("delaiEntreNiveauxJours")] public int LevelLagDays { get; set; } = 7;
        /// <summary>Attente apres le gros oeuvre d'un niveau avant la maconnerie de ce niveau.</summary>
        [JsonPropertyName("delaiMaconnerieJours")] public int MasonryLagDays { get; set; } = 3;
        /// <summary>Duree minimale d'une tache.</summary>
        [JsonPropertyName("dureeMinJours")] public int MinDays { get; set; } = 1;
        [JsonPropertyName("feries")] public List<DateTime> Holidays { get; set; } = new List<DateTime>();
        [JsonPropertyName("equipes")] public List<LotCrew> Crews { get; set; } = new List<LotCrew>();
        [JsonPropertyName("rendements")] public List<ProductionRate> Rates { get; set; } = DefaultRates();

        public int CrewsFor(string lot) => Math.Max(1, Crews?.FirstOrDefault(c => c.Lot == lot)?.Crews ?? 1);

        /// <summary>Rendements indicatifs d'une equipe courante (chantier de batiment, Afrique centrale) : A ADAPTER.</summary>
        public static List<ProductionRate> DefaultRates() => new List<ProductionRate>
        {
            new ProductionRate { Articles = "01.*", Unit = "m3", PerDay = 12, Note = "terrassement manuel / petit engin" },
            new ProductionRate { Articles = "01.*", Unit = "m2", PerDay = 150 },
            new ProductionRate { Articles = "02.*", Unit = "m3", PerDay = 6, Note = "béton coulé (bétonnière)" },
            new ProductionRate { Articles = "03.*", Unit = "m3", PerDay = 6, Note = "béton coulé (bétonnière)" },
            new ProductionRate { Articles = "02.*", Unit = "m2", PerDay = 18, Note = "coffrage" },
            new ProductionRate { Articles = "03.*", Unit = "m2", PerDay = 18, Note = "coffrage" },
            new ProductionRate { Articles = "02.*", Unit = "kg", PerDay = 350, Note = "armatures façonnées et posées" },
            new ProductionRate { Articles = "03.*", Unit = "kg", PerDay = 350, Note = "armatures façonnées et posées" },
            new ProductionRate { Articles = "02.*", Unit = "ml", PerDay = 8, Note = "pieux, forages" },
            new ProductionRate { Articles = "03.*", Unit = "ml", PerDay = 8, Note = "pieux, forages" },
            new ProductionRate { Articles = "04.*", Unit = "kg", PerDay = 600 },
            new ProductionRate { Articles = "05.*", Unit = "m2", PerDay = 10, Note = "maçonnerie de blocs" },
            new ProductionRate { Articles = "06.*", Unit = "m2", PerDay = 40 },
            new ProductionRate { Articles = "06.*", Unit = "ml", PerDay = 25 },
            new ProductionRate { Articles = "07.*", Unit = "U", PerDay = 4 },
            new ProductionRate { Articles = "07.*", Unit = "ml", PerDay = 15 },
            new ProductionRate { Articles = "07.*", Unit = "m2", PerDay = 12 },
            new ProductionRate { Articles = "08.01", Unit = "m2", PerDay = 25, Note = "enduit" },
            new ProductionRate { Articles = "08.02", Unit = "m2", PerDay = 60, Note = "peinture" },
            new ProductionRate { Articles = "08.*", Unit = "m2", PerDay = 15, Note = "carrelage, revêtements" },
            new ProductionRate { Articles = "08.*", Unit = "ml", PerDay = 40 },
            new ProductionRate { Articles = "09.*", Unit = "ml", PerDay = 30 },
            new ProductionRate { Articles = "09.*", Unit = "U", PerDay = 4 },
            new ProductionRate { Articles = "10.*", Unit = "ml", PerDay = 60 },
            new ProductionRate { Articles = "10.*", Unit = "U", PerDay = 12 },
            new ProductionRate { Articles = "11.*", Unit = "m2", PerDay = 40 },
            new ProductionRate { Articles = "11.*", Unit = "m3", PerDay = 8 },
            new ProductionRate { Articles = "11.*", Unit = "ml", PerDay = 20 },
            new ProductionRate { Articles = "11.*", Unit = "U", PerDay = 6 },
            new ProductionRate { Articles = "11.*", Unit = "kg", PerDay = 350 },
            new ProductionRate { Articles = "12.*", Unit = "ml", PerDay = 25 },
            new ProductionRate { Articles = "12.*", Unit = "U", PerDay = 6 },
            new ProductionRate { Articles = "*", Unit = "m3", PerDay = 5 },
            new ProductionRate { Articles = "*", Unit = "m2", PerDay = 15 },
            new ProductionRate { Articles = "*", Unit = "ml", PerDay = 20 },
            new ProductionRate { Articles = "*", Unit = "kg", PerDay = 300 },
            new ProductionRate { Articles = "*", Unit = "U", PerDay = 5 },
            new ProductionRate { Articles = "*", Unit = "*", PerDay = 10 }
        };

        public ProductionRate RateFor(string articleCode, BoqUnit unit)
        {
            string u = unit.Symbol().Replace("²", "2").Replace("³", "3");
            return (Rates ?? DefaultRates()).FirstOrDefault(r =>
                new TextPattern(UnitPattern(r.Articles)).IsMatch(articleCode) &&
                (r.Unit == "*" || string.Equals(r.Unit, u, StringComparison.OrdinalIgnoreCase)));
        }

        private static string UnitPattern(string p) => string.IsNullOrWhiteSpace(p) ? "*" : p;
    }

    /// <summary>Une tache du planning : un lot sur un niveau (ou une tache generale).</summary>
    public sealed class PlanTask
    {
        public string Id { get; set; }
        public string Lot { get; set; }
        public string LotDesignation { get; set; }
        public string Level { get; set; }
        public string Name { get; set; }
        public int DurationDays { get; set; }
        /// <summary>Debut et fin en jours ouvres depuis le demarrage (fin exclusive).</summary>
        public int StartDay { get; set; }
        public int EndDay => StartDay + DurationDays;
        public DateTime Start { get; set; }
        public DateTime Finish { get; set; }
        public List<string> Predecessors { get; } = new List<string>();
        public int FloatDays { get; set; }
        public bool IsCritical => FloatDays == 0;
        public decimal Amount { get; set; }
        /// <summary>Calcul de la duree : quantites / rendements, en clair.</summary>
        public string Basis { get; set; }
        public string Color { get; set; }
    }

    public sealed class WorkPlan
    {
        public DateTime StartDate { get; set; }
        public DateTime FinishDate { get; set; }
        public int TotalDays { get; set; }
        public int WorkingDaysPerWeek { get; set; }
        public List<PlanTask> Tasks { get; } = new List<PlanTask>();
        public List<string> Notes { get; } = new List<string>();
        public int CalendarDays => (FinishDate - StartDate).Days + 1;
    }

    /// <summary>
    /// Planification des travaux du niveau bas vers le niveau haut, a partir des quantites du BOQ
    /// reparties par niveau et de rendements d'equipe (planning.json). Enchainements realistes :
    /// installation, terrassements, fondations ; puis, niveau par niveau, gros oeuvre (le niveau
    /// suivant apres le delai de decoffrage), maconnerie, reseaux, menuiseries, finitions ; toiture
    /// apres le dernier niveau ; VRD apres le gros oeuvre. Une equipe ne travaille que sur un niveau
    /// a la fois (meme lot : niveau n+1 apres le niveau n). Durees ESTIMEES ; chemin critique calcule.
    /// </summary>
    public static class WorkPlanner
    {
        private static readonly CultureInfo Fr = CultureInfo.GetCultureInfo("fr-FR");

        /// <summary>Lots du « tronc commun » (une seule tache, pas par niveau).</summary>
        private static readonly string[] SiteWide = { "DEM", "00", "01", "02", "11" };

        public static WorkPlan Build(BoqResult boq, IReadOnlyList<string> levelOrder, string groundLevel, PlanningSettings settings, DateTime start)
        {
            settings = settings ?? new PlanningSettings();
            var plan = new WorkPlan { StartDate = start.Date, WorkingDaysPerWeek = settings.WorkingDaysPerWeek == 5 ? 5 : 6 };
            if (boq == null) return plan;
            List<LevelBlock> blocks = LevelBreakdown.Build(boq, levelOrder, null, null, null, groundLevel);
            List<string> levels = blocks.Where(b => !b.IsWithoutLevel).Select(b => b.Level).ToList();
            var tasks = new Dictionary<string, PlanTask>(StringComparer.Ordinal);

            PlanTask Add(string id, string lot, string designation, string level, IEnumerable<LevelArticle> articles, int? fixedDays = null)
            {
                var list = articles?.ToList() ?? new List<LevelArticle>();
                var basis = new List<string>();
                double days = 0;
                foreach (LevelArticle a in list)
                {
                    ProductionRate r = settings.RateFor(a.Code, a.Unit);
                    double perDay = r?.PerDay > 0 ? r.PerDay : 10;
                    double d = (double)a.Quantity / perDay;
                    days += d;
                    basis.Add(a.Code + " : " + a.Quantity.ToString("N1", Fr) + " " + a.Unit.Symbol() + " ÷ " + perDay.ToString("0.##", Fr) + "/j = " + d.ToString("0.0", Fr) + " j");
                }
                int crews = settings.CrewsFor(lot);
                int duration = fixedDays ?? Math.Max(settings.MinDays, (int)Math.Ceiling(days / crews - 1e-9));
                var t = new PlanTask
                {
                    Id = id,
                    Lot = lot,
                    LotDesignation = designation,
                    Level = level,
                    Name = (level == null ? "Lot " + lot : "Lot " + lot + " — " + level) + " · " + designation,
                    DurationDays = duration,
                    Amount = list.Sum(a => a.Amount ?? 0m),
                    Basis = fixedDays.HasValue ? "Durée forfaitaire (planning.json)." :
                        string.Join(" ; ", basis) + (crews > 1 ? " — " + crews + " équipes : ÷ " + crews : string.Empty) + " → " + duration + " j ouvrés (ESTIMÉ).",
                    Color = DanCI.BOQ.Core.Dashboard.Palette.ForLot(lot)
                };
                tasks[id] = t;
                plan.Tasks.Add(t);
                return t;
            }

            IEnumerable<LevelArticle> ArticlesOf(string lot, string level = null) => blocks
                .Where(b => level == null || b.Level == level)
                .SelectMany(b => b.Lots.Where(l => l.Code == lot).SelectMany(l => l.Articles));

            string Designation(string lot) => boq.Lots.FirstOrDefault(l => l.Code == lot)?.Designation ?? "Lot " + lot;
            bool Has(string lot, string level = null) => ArticlesOf(lot, level).Any();
            void Link(PlanTask t, PlanTask p) { if (t != null && p != null && !t.Predecessors.Contains(p.Id)) t.Predecessors.Add(p.Id); }

            // 1. Tronc commun : installation, demolitions, terrassements, fondations.
            PlanTask setup = Add("00", "00", Has("00") ? Designation("00") : "Installation de chantier", null, ArticlesOf("00"),
                Has("00") ? (int?)null : Math.Max(1, settings.SiteSetupDays));
            if (!Has("00")) setup.Basis = "Installation de chantier : " + setup.DurationDays + " j forfaitaires (planning.json, installationChantierJours).";
            PlanTask demolition = Has("DEM") ? Add("DEM", "DEM", Designation("DEM"), null, ArticlesOf("DEM")) : null;
            Link(demolition, setup);
            PlanTask earth = Has("01") ? Add("01", "01", Designation("01"), null, ArticlesOf("01")) : null;
            Link(earth, demolition ?? setup);
            PlanTask found = Has("02") ? Add("02", "02", Designation("02"), null, ArticlesOf("02")) : null;
            Link(found, earth ?? demolition ?? setup);
            PlanTask basePredecessor = found ?? earth ?? demolition ?? setup;

            // 2. Niveau par niveau, du bas vers le haut.
            string[] perLevelLots = { "03", "04", "05", "09", "10", "12", "07", "08" };
            var previousByLot = new Dictionary<string, PlanTask>(StringComparer.Ordinal);
            PlanTask previousStructure = null;
            PlanTask topStructure = null, lastMasonry = null;
            foreach (string level in levels)
            {
                var here = new Dictionary<string, PlanTask>(StringComparer.Ordinal);
                foreach (string lot in perLevelLots)
                {
                    if (!Has(lot, level)) continue;
                    PlanTask t = Add(lot + "@" + level, lot, Designation(lot), level, ArticlesOf(lot, level));
                    here[lot] = t;
                    // Meme equipe : un niveau apres l'autre.
                    if (previousByLot.TryGetValue(lot, out PlanTask sameLot)) Link(t, sameLot);
                    previousByLot[lot] = t;
                }
                here.TryGetValue("03", out PlanTask structure);
                here.TryGetValue("04", out PlanTask steel);
                PlanTask frame = structure ?? steel;
                if (structure != null)
                {
                    Link(structure, previousStructure ?? basePredecessor);
                    previousStructure = structure;
                    topStructure = structure;
                }
                if (steel != null) Link(steel, structure ?? previousStructure ?? basePredecessor);
                PlanTask shell = frame ?? previousStructure ?? basePredecessor;
                if (here.TryGetValue("05", out PlanTask masonry)) { Link(masonry, shell); lastMasonry = masonry; }
                PlanTask closed = masonry ?? shell;
                foreach (string net in new[] { "09", "10", "12" })
                {
                    if (here.TryGetValue(net, out PlanTask n)) Link(n, closed);
                }
                if (here.TryGetValue("08", out PlanTask finishes))
                {
                    Link(finishes, closed);
                    foreach (string net in new[] { "09", "10", "12" }) if (here.TryGetValue(net, out PlanTask n)) Link(finishes, n);
                }
                if (here.TryGetValue("07", out PlanTask joinery)) Link(joinery, closed);
            }
            // Gros oeuvre du niveau suivant : apres le delai de decoffrage (porte par le calcul des dates).

            // 3. Toiture, VRD, autres lots, non classes.
            if (Has("06"))
            {
                PlanTask roof = Add("06", "06", Designation("06"), null, ArticlesOf("06"));
                Link(roof, topStructure ?? basePredecessor);
                Link(roof, lastMasonry);
            }
            if (Has("11"))
            {
                PlanTask vrd = Add("11", "11", Designation("11"), groundLevel, ArticlesOf("11"));
                Link(vrd, lastMasonry ?? topStructure ?? basePredecessor);
            }
            foreach (BoqLotResult lot in boq.Lots.Where(l => l.Code != "NC" && !SiteWide.Contains(l.Code) && !perLevelLots.Contains(l.Code) && l.Code != "06"))
            {
                PlanTask other = Add(lot.Code, lot.Code, lot.Designation, null, ArticlesOf(lot.Code));
                Link(other, topStructure ?? basePredecessor);
            }
            // Lots par niveau sans niveau (forfaits) : a la fin du dernier niveau du meme lot.
            foreach (string lot in perLevelLots)
            {
                var noLevel = blocks.Where(b => b.IsWithoutLevel).SelectMany(b => b.Lots.Where(l => l.Code == lot).SelectMany(l => l.Articles)).ToList();
                if (noLevel.Count == 0) continue;
                PlanTask t = Add(lot + "@sans-niveau", lot, Designation(lot) + " (forfaits, sans niveau)", null, noLevel);
                Link(t, previousByLot.TryGetValue(lot, out PlanTask p) ? p : topStructure ?? basePredecessor);
            }
            if (boq.Lots.Any(l => l.Code == "NC" && l.Lines.Count > 0))
            {
                plan.Notes.Add("Les lignes non classées (lot NC) ne sont pas planifiées : classez-les pour qu'elles entrent dans le planning.");
            }

            Schedule(plan, tasks, settings);
            return plan;
        }

        /// <summary>Dates au plus tot (avec delais), au plus tard, marges et calendrier.</summary>
        private static void Schedule(WorkPlan plan, Dictionary<string, PlanTask> tasks, PlanningSettings s)
        {
            int Lag(PlanTask t, PlanTask p)
            {
                if (t.Lot == "03" && p.Lot == "03") return Math.Max(0, s.LevelLagDays);
                if (t.Lot == "05" && (p.Lot == "03" || p.Lot == "04") && t.Level == p.Level) return Math.Max(0, s.MasonryLagDays);
                return 0;
            }
            // Ordre topologique (les predecesseurs sont toujours crees avant : ordre de creation suffit, verifie).
            List<PlanTask> order = plan.Tasks.ToList();
            foreach (PlanTask t in order)
            {
                t.StartDay = t.Predecessors.Select(id => tasks[id]).Select(p => p.EndDay + Lag(t, p)).DefaultIfEmpty(0).Max();
            }
            int end = order.Count == 0 ? 0 : order.Max(t => t.EndDay);
            var latestFinish = order.ToDictionary(t => t.Id, t => end);
            for (int i = order.Count - 1; i >= 0; i--)
            {
                PlanTask t = order[i];
                int lf = latestFinish[t.Id];
                int ls = lf - t.DurationDays;
                t.FloatDays = Math.Max(0, ls - t.StartDay);
                foreach (string id in t.Predecessors)
                {
                    PlanTask p = tasks[id];
                    latestFinish[id] = Math.Min(latestFinish[id], ls - Lag(t, p));
                }
            }
            var calendar = new WorkCalendar(plan.StartDate, plan.WorkingDaysPerWeek, s.Holidays);
            foreach (PlanTask t in order)
            {
                t.Start = calendar.DateOf(t.StartDay);
                t.Finish = calendar.DateOf(t.EndDay - 1);
            }
            plan.TotalDays = end;
            plan.FinishDate = order.Count == 0 ? plan.StartDate : calendar.DateOf(end - 1);
        }
    }

    /// <summary>Calendrier des jours ouvres : dimanche chome (et samedi en semaine de 5 jours), feries chomes.</summary>
    public sealed class WorkCalendar
    {
        private readonly DateTime start;
        private readonly int perWeek;
        private readonly HashSet<DateTime> holidays;
        private readonly List<DateTime> cache = new List<DateTime>();

        public WorkCalendar(DateTime start, int workingDaysPerWeek, IEnumerable<DateTime> holidays)
        {
            this.start = start.Date;
            perWeek = workingDaysPerWeek == 5 ? 5 : 6;
            this.holidays = new HashSet<DateTime>((holidays ?? Enumerable.Empty<DateTime>()).Select(d => d.Date));
        }

        public bool IsWorking(DateTime d) =>
            d.DayOfWeek != DayOfWeek.Sunday && !(perWeek == 5 && d.DayOfWeek == DayOfWeek.Saturday) && !holidays.Contains(d.Date);

        /// <summary>Date du n-ieme jour ouvre (0 = premier jour ouvre a partir du demarrage).</summary>
        public DateTime DateOf(int workingDay)
        {
            workingDay = Math.Max(0, workingDay);
            DateTime d = cache.Count == 0 ? start.AddDays(-1) : cache[cache.Count - 1];
            while (cache.Count <= workingDay)
            {
                d = d.AddDays(1);
                if (IsWorking(d)) cache.Add(d);
            }
            return cache[workingDay];
        }
    }
}

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Ligne du diagramme de Gantt : en-tete de section (niveau) ou tache.</summary>
    public sealed class GanttRow
    {
        public bool IsHeader { get; set; }
        public string Title { get; set; }
        public PlanTask Task { get; set; }
    }

    /// <summary>
    /// Ordre de lecture du Gantt : preparation et infrastructure, puis chaque niveau du bas vers le
    /// haut, puis toiture, VRD et lots generaux. Meme ordre dans la fenetre, Excel et le PDF.
    /// </summary>
    public static class GanttRows
    {
        private static readonly string[] Preparation = { "00", "DEM", "01", "02" };

        public static List<GanttRow> Build(WorkPlan plan)
        {
            var rows = new List<GanttRow>();
            if (plan == null || plan.Tasks.Count == 0) return rows;
            void Section(string title, IEnumerable<PlanTask> tasks)
            {
                var list = tasks.ToList();
                if (list.Count == 0) return;
                rows.Add(new GanttRow { IsHeader = true, Title = title });
                rows.AddRange(list.OrderBy(t => t.StartDay).ThenBy(t => t.Lot, StringComparer.Ordinal).Select(t => new GanttRow { Task = t, Title = t.Name }));
            }
            var used = new HashSet<PlanTask>();
            IEnumerable<PlanTask> Take(Func<PlanTask, bool> f) { var l = plan.Tasks.Where(t => !used.Contains(t) && f(t)).ToList(); foreach (var t in l) used.Add(t); return l; }

            Section("PRÉPARATION ET INFRASTRUCTURE", Take(t => Preparation.Contains(t.Lot)));
            foreach (string level in plan.Tasks.Where(t => t.Level != null && t.Lot != "11").OrderBy(t => t.StartDay).Select(t => t.Level).Distinct().ToList())
            {
                Section("NIVEAU " + level.ToUpperInvariant(), Take(t => t.Level == level && t.Lot != "11"));
            }
            Section("TOITURE, EXTÉRIEURS ET LOTS GÉNÉRAUX", Take(t => true));
            return rows;
        }
    }
}
