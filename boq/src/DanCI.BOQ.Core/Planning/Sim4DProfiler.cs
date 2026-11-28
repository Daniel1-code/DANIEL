using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace DanCI.BOQ.Core.Planning
{
    /// <summary>
    /// Releve du temps passe par Revit a chaque etape d'une image du Studio 4D (preparation, masquage,
    /// couleurs, rotation, cadrage, dessin, affichage...) : dit EXACTEMENT ou part le temps sur un modele donne.
    /// </summary>
    public sealed class Sim4DProfiler
    {
        private readonly Stopwatch clock = new Stopwatch();
        private long last;
        private readonly Dictionary<string, double> recent = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly Dictionary<string, double> total = new Dictionary<string, double>(StringComparer.Ordinal);
        private readonly List<string> order = new List<string>();
        private readonly Dictionary<string, double> frame = new Dictionary<string, double>(StringComparer.Ordinal);

        public int Frames { get; private set; }

        /// <summary>Debut d'une image.</summary>
        public void Start()
        {
            frame.Clear();
            clock.Restart();
            last = 0;
        }

        /// <summary>Fin d'une etape : le temps depuis la marque precedente lui est attribue.</summary>
        public void Mark(string step)
        {
            long now = clock.ElapsedTicks;
            double ms = (now - last) * 1000.0 / Stopwatch.Frequency;
            last = now;
            frame[step] = (frame.TryGetValue(step, out double v) ? v : 0) + ms;
        }

        /// <summary>Fin de l'image : moyennes glissantes (dernieres images) et cumul (rapport).</summary>
        public void End()
        {
            Frames++;
            foreach (var kv in frame)
            {
                if (!order.Contains(kv.Key)) order.Add(kv.Key);
                recent[kv.Key] = recent.TryGetValue(kv.Key, out double r) ? r * 0.8 + kv.Value * 0.2 : kv.Value;
                total[kv.Key] = (total.TryGetValue(kv.Key, out double t) ? t : 0) + kv.Value;
            }
            foreach (string k in order.Where(k => !frame.ContainsKey(k))) recent[k] = recent[k] * 0.8;
        }

        public double LastFrameMs => frame.Values.Sum();

        /// <summary>Les etapes les plus couteuses (moyenne glissante), ex. « rotation 180 · masquage 90 · préparation 40 ms ».</summary>
        public string Summary(int top = 3)
        {
            var list = recent.Where(kv => kv.Value >= 1).OrderByDescending(kv => kv.Value).Take(top).ToList();
            if (list.Count == 0) return "Revit : < 1 ms";
            return "Revit : " + string.Join(" · ", list.Select(kv => kv.Key + " " + kv.Value.ToString("0", CultureInfo.InvariantCulture))) + " ms";
        }

        /// <summary>Moyenne par image de chaque etape depuis le debut de la lecture (ordre des etapes).</summary>
        public List<KeyValuePair<string, double>> Averages() =>
            order.Select(k => new KeyValuePair<string, double>(k, Frames == 0 ? 0 : total[k] / Frames)).ToList();

        public void Reset()
        {
            Frames = 0;
            recent.Clear();
            total.Clear();
            order.Clear();
            frame.Clear();
        }
    }
}
