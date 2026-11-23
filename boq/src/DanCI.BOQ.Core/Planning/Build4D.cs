using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>Un element a construire progressivement dans la simulation 4D (forme simplifiee : prismes).</summary>
    public sealed class Build4DItem
    {
        public long Id { get; set; }
        /// <summary>Ordre de chantier de la categorie (fondations 0, poteaux/murs 1, poutres 2, dalles 3, toiture 4, autres 5).</summary>
        public int Rank { get; set; }
        /// <summary>Cote du bas de l'element (unites du modele).</summary>
        public double Zb { get; set; }
        /// <summary>Forme : un ou plusieurs prismes (mur courbe : un par segment).</summary>
        public List<Prism> Shape { get; set; } = new List<Prism>();
        /// <summary>Monte par rangs de 20 cm (murs, poteaux, voiles, equipements) : la derniere tranche posee le long de l'element.</summary>
        public bool Courses { get; set; }
    }

    /// <summary>
    /// Construction progressive de la simulation 4D (calcul pur, sans Revit).
    /// 1. Ordre de chantier DANS une tache : groupes du bas vers le haut (cote du bas par tranches),
    ///    puis par categorie (fondations, poteaux et murs, poutres, dalles, toiture, le reste) ; les
    ///    elements d'un meme groupe montent ensemble (comme des macons qui levent tous les murs d'un
    ///    niveau rang par rang). Chaque groupe recoit une part de la duree proportionnelle a son nombre d'elements.
    /// 2. Forme partielle d'un element : un mur monte par assises de ~20 cm, la derniere posee le
    ///    long du mur (brique apres brique) ; un poteau monte ; un element plat (dalle) se coule d'un
    ///    bout a l'autre selon sa plus grande longueur.
    /// </summary>
    public static class Build4D
    {
        /// <summary>Tranche de cote pour regrouper les elements d'une meme hauteur (unites du modele : pieds = 1,50 m).</summary>
        public const double BandFeet = 4.92;
        /// <summary>Hauteur d'une assise de maconnerie (pieds = 0,20 m).</summary>
        public const double CourseFeet = 0.656;
        /// <summary>Joint entre assises (pieds = 1 cm) : les rangs se voient.</summary>
        public const double JointFeet = 0.033;
        /// <summary>En dessous de cette hauteur, l'element est « plat » et se coule en longueur (pieds = 0,60 m).</summary>
        public const double FlatFeet = 1.97;

        public static int Rank(string category)
        {
            switch (category)
            {
                case "OST_StructuralFoundation": return 0;
                case "OST_StructuralColumns":
                case "OST_Columns":
                case "OST_Walls": return 1;
                case "OST_StructuralFraming": return 2;
                case "OST_Floors":
                case "OST_EdgeSlab":
                case "OST_Stairs":
                case "OST_StairsRuns":
                case "OST_StairsLandings": return 3;
                case "OST_Roofs": return 4;
                default: return 5;
            }
        }

        /// <summary>Duree minimale d'un groupe, en part de la tache : les groupes se chevauchent, rien ne surgit.</summary>
        public const double MinGroupWindow = 0.4;

        /// <summary>
        /// Avancement (0..1) de chaque element d'une tache dont l'avancement global est <paramref name="taskProgress"/>.
        /// Groupes dans l'ordre du chantier, chacun sur une fenetre d'au moins 40 % de la tache, les
        /// fenetres se chevauchant : un petit groupe ne se construit jamais en un instant.
        /// </summary>
        public static Dictionary<long, double> Schedule(IEnumerable<Build4DItem> items, double taskProgress)
        {
            var list = (items ?? Enumerable.Empty<Build4DItem>()).ToList();
            var result = new Dictionary<long, double>();
            if (list.Count == 0) return result;
            double p = Math.Max(0, Math.Min(1, taskProgress));
            var groups = list.GroupBy(i => (Band: (int)Math.Floor(i.Zb / BandFeet), i.Rank))
                             .OrderBy(g => g.Key.Band).ThenBy(g => g.Key.Rank)
                             .ToList();
            // Debut de chaque groupe : proportionnel aux elements qui le precedent ; meme largeur de fenetre pour tous.
            var cum = new List<double>();
            double acc = 0;
            foreach (var g in groups)
            {
                cum.Add(acc);
                acc += g.Count() / (double)list.Count;
            }
            double maxShare = groups.Max(g => g.Count() / (double)list.Count);
            double window = groups.Count == 1 ? 1 : Math.Min(1, Math.Max(MinGroupWindow, maxShare));
            double lastStart = cum[cum.Count - 1];
            for (int k = 0; k < groups.Count; k++)
            {
                double start = groups.Count == 1 || lastStart <= 0 ? 0 : cum[k] / lastStart * (1 - window);
                double gp = Math.Max(0, Math.Min(1, (p - start) / window));
                foreach (var i in groups[k]) result[i.Id] = gp;
            }
            return result;
        }

        /// <summary>Nombre minimal d'images pour construire une tache dans le Studio 4D (aucun element ne surgit).</summary>
        public const int MinFramesPerTask = 16;

        /// <summary>Comment l'avancement affiche suit la date pour une image.</summary>
        public enum Follow
        {
            /// <summary>Lecture : un pas, monte d'au plus 1/<see cref="MinFramesPerTask"/>.</summary>
            Advance,
            /// <summary>Meme date (evenement en double, rotation) : ne bouge pas (sauf si la date recule).</summary>
            Hold,
            /// <summary>Date deplacee a la main : recalage direct.</summary>
            Snap
        }

        public static double Next(double shown, double target, Follow mode)
        {
            target = Math.Max(0, Math.Min(1, target));
            switch (mode)
            {
                case Follow.Advance: return Ease(shown, target, true);
                case Follow.Hold: return Math.Min(shown, target);
                default: return target;
            }
        }

        /// <summary>
        /// Avancement AFFICHE d'une tache : suit l'avancement reel sans jamais sauter de plus de
        /// 1/<see cref="MinFramesPerTask"/> par image en lecture ; recalage direct (sans animation)
        /// quand l'utilisateur deplace la date a la main ou recule.
        /// </summary>
        public static double Ease(double shown, double target, bool playing)
        {
            target = Math.Max(0, Math.Min(1, target));
            if (!playing || target < shown) return target;
            return Math.Min(target, shown + 1.0 / MinFramesPerTask);
        }

        /// <summary>Forme partielle d'un element a l'avancement <paramref name="progress"/> (0..1) : prismes a dessiner.</summary>
        public static List<Prism> Partial(Build4DItem item, double progress)
        {
            var result = new List<Prism>();
            double p = Math.Max(0, Math.Min(1, progress));
            if (item == null || p <= 0) return result;
            foreach (Prism shape in item.Shape)
            {
                double h = shape.Zt - shape.Zb;
                if (h <= 1e-9) continue;
                if (h < FlatFeet)
                {
                    // Element plat (dalle, poutre basse) : coule d'un bout a l'autre.
                    result.Add(Along(shape, p, shape.Zb, shape.Zt));
                }
                else if (item.Courses)
                {
                    int n = Math.Max(1, (int)Math.Round(h / CourseFeet));
                    double hc = h / n, total = p * n;
                    int full = Math.Min(n, (int)Math.Floor(total + 1e-9));
                    for (int k = 0; k < full; k++)
                    {
                        result.Add(Slice(shape, shape.Zb + k * hc, shape.Zb + (k + 1) * hc - Math.Min(JointFeet, hc * 0.3)));
                    }
                    double frac = total - full;
                    if (full < n && frac > 1e-6)
                    {
                        // Assise en cours : posee le long du mur, brique apres brique.
                        result.Add(Along(shape, frac, shape.Zb + full * hc, shape.Zb + (full + 1) * hc - Math.Min(JointFeet, hc * 0.3)));
                    }
                }
                else
                {
                    result.Add(Slice(shape, shape.Zb, shape.Zb + h * p));
                }
            }
            return result;
        }

        private static Prism Slice(Prism s, double zb, double zt) => new Prism
        {
            Bottom = (double[])s.Bottom.Clone(), Top = (double[])s.Bottom.Clone(), Zb = zb, Zt = zt
        };

        /// <summary>Portion « frac » du prisme selon sa plus grande longueur en plan, entre zb et zt.</summary>
        private static Prism Along(Prism s, double frac, double zb, double zt)
        {
            double[] q = s.Bottom;
            double L01 = Dist(q, 0, 1), L12 = Dist(q, 1, 2);
            // a-b et d-c suivent la plus grande longueur
            int a = L01 >= L12 ? 0 : 1, b = (a + 1) % 4, c = (a + 2) % 4, d = (a + 3) % 4;
            double X(int i) => q[2 * i];
            double Y(int i) => q[2 * i + 1];
            var r = new double[8];
            void Put(int slot, double x, double y) { r[2 * slot] = x; r[2 * slot + 1] = y; }
            Put(a, X(a), Y(a));
            Put(b, X(a) + (X(b) - X(a)) * frac, Y(a) + (Y(b) - Y(a)) * frac);
            Put(c, X(d) + (X(c) - X(d)) * frac, Y(d) + (Y(c) - Y(d)) * frac);
            Put(d, X(d), Y(d));
            return new Prism { Bottom = r, Top = (double[])r.Clone(), Zb = zb, Zt = zt };
        }

        private static double Dist(double[] q, int i, int j)
        {
            double dx = q[2 * j] - q[2 * i], dy = q[2 * j + 1] - q[2 * i + 1];
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Prisme d'une boite alignee sur les axes (elements sans trace : poteaux, dalles, equipements).</summary>
        public static Prism Box(double minX, double minY, double minZ, double maxX, double maxY, double maxZ)
        {
            var q = new[] { minX, minY, maxX, minY, maxX, maxY, minX, maxY };
            return new Prism { Bottom = q, Top = (double[])q.Clone(), Zb = minZ, Zt = maxZ };
        }
    }

    /// <summary>Etat d'une image 4D : elements construits, colores (taches en cours), en construction par tache.</summary>
    public sealed class Build4DFrame
    {
        public HashSet<long> Built { get; } = new HashSet<long>();
        /// <summary>Element -> couleur de la tache en cours qui le travaille.</summary>
        public Dictionary<long, string> Running { get; } = new Dictionary<long, string>();
        /// <summary>Taches en cours -> elements qu'ELLES construisent (montee progressive).</summary>
        public List<(PlanTask Task, double Progress, List<long> Elements)> Building { get; } = new List<(PlanTask, double, List<long>)>();
        public HashSet<long> All { get; } = new HashSet<long>();
    }

    public static class Build4DState
    {
        /// <summary>
        /// Lots qui ne CONSTRUISENT pas les elements qu'ils mesurent : terrassements (fouilles sous les
        /// murs, mesurees SUR les murs), finitions (enduits, peinture SUR les murs), demolitions.
        /// </summary>
        public static readonly HashSet<string> NonBuilderLots = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "01", "08", "DEM", "00" };

        /// <summary>
        /// Tache qui CONSTRUIT chaque element : la premiere tache d'un lot « constructeur » (fondations,
        /// beton arme, charpente, maconnerie, toiture, menuiseries, reseaux) qui le contient ; a defaut,
        /// la premiere tache qui le contient. Un mur est donc monte par la maconnerie, jamais par le
        /// terrassement (fouilles sous les murs) ni par l'enduit.
        /// </summary>
        public static Dictionary<long, string> Owners(IEnumerable<PlanTask> tasks)
        {
            var owners = new Dictionary<long, string>();
            var ordered = (tasks ?? Enumerable.Empty<PlanTask>()).Select((t, i) => (t, i)).OrderBy(x => x.t.StartDay).ThenBy(x => x.i).Select(x => x.t).ToList();
            foreach (bool builders in new[] { true, false })
            {
                foreach (PlanTask t in ordered.Where(x => NonBuilderLots.Contains(x.Lot ?? string.Empty) != builders))
                {
                    foreach (var r in t.Elements.Where(x => !x.IsLinked))
                    {
                        if (!owners.ContainsKey(r.ElementId)) owners[r.ElementId] = t.Id;
                    }
                }
            }
            return owners;
        }

        /// <summary>
        /// Image 4D pour un avancement AFFICHE par tache. Un element n'est construit qu'une fois, par
        /// sa tache « proprietaire » (ex. un mur : la maconnerie) ; les taches suivantes (enduit,
        /// peinture) ne font que le colorer une fois construit : rien de deja construit ne disparait.
        /// </summary>
        public static Build4DFrame Compute(IEnumerable<PlanTask> tasks, IReadOnlyDictionary<string, double> progress, IReadOnlyDictionary<long, string> owners)
        {
            var f = new Build4DFrame();
            var list = (tasks ?? Enumerable.Empty<PlanTask>()).ToList();
            double P(string id) => progress != null && progress.TryGetValue(id, out double v) ? v : 0;
            foreach (PlanTask t in list)
            {
                double now = P(t.Id);
                var own = new List<long>();
                foreach (var r in t.Elements.Where(x => !x.IsLinked))
                {
                    long id = r.ElementId;
                    f.All.Add(id);
                    string o = owners != null && owners.TryGetValue(id, out string ow) ? ow : t.Id;
                    if (o == t.Id)
                    {
                        if (now >= 1) f.Built.Add(id);
                        else if (now > 0) own.Add(id);
                    }
                    else if (now > 0 && now < 1 && P(o) >= 1 && !f.Running.ContainsKey(id))
                    {
                        f.Running[id] = t.Color; // tache ulterieure : colore l'element deja construit
                    }
                }
                if (now > 0 && now < 1)
                {
                    foreach (long id in own) if (!f.Running.ContainsKey(id)) f.Running[id] = t.Color;
                    if (own.Count > 0) f.Building.Add((t, now, own));
                }
            }
            f.Built.ExceptWith(f.Running.Keys);
            return f;
        }
    }
}
