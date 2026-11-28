using System;
using System.Collections.Generic;
using DanCI.BOQ.Core.Planning;

namespace DanCI.BOQ.Export
{
    /// <summary>Capture de la vue 3D pendant la simulation 4D (JPEG, date incrustee).</summary>
    public sealed class Sim4DSnapshot
    {
        public string Caption { get; set; }
        public DateTime Date { get; set; }
        public double Progress { get; set; }
        public byte[] Jpeg { get; set; }
    }

    /// <summary>Tout ce que le rapport de simulation 4D contient (calcule par le Studio et Revit).</summary>
    public sealed class Sim4DReport
    {
        /// <summary>Contexte du projet (identite, logo, theme, devise) : celui des exports.</summary>
        public ExportContext Context { get; set; }
        public WorkPlan Plan { get; set; }
        public List<Sim4DSnapshot> Snapshots { get; set; } = new List<Sim4DSnapshot>();

        /// <summary>Reglages de lecture (pas, montee, rotation, couleurs, volumes...).</summary>
        public List<KeyValuePair<string, string>> Settings { get; set; } = new List<KeyValuePair<string, string>>();

        /// <summary>Deroulement : images dessinees, cadence moyenne, duree de lecture.</summary>
        public int Frames { get; set; }
        public double AverageFrameMs { get; set; }
        public TimeSpan PlayTime { get; set; }
        public bool Completed { get; set; }
        public int SimulatedElements { get; set; }

        /// <summary>Temps moyen par image de chaque etape dans Revit (ms) : ou part le temps.</summary>
        public List<KeyValuePair<string, double>> Timings { get; set; } = new List<KeyValuePair<string, double>>();

        /// <summary>Video enregistree pendant la simulation (chemin), ou null.</summary>
        public string VideoPath { get; set; }

        /// <summary>Points d'attention (formes illisibles, elements non affichables, conflits de dates...).</summary>
        public List<string> Warnings { get; set; } = new List<string>();

        public DateTime GeneratedAt { get; set; } = DateTime.Now;
    }
}
