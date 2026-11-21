using System;
using System.Collections.Generic;
using System.Threading;

namespace DanCI.BOQ.Core.Extraction
{
    public enum ScopeMode
    {
        /// <summary>Modele hote entier.</summary>
        Modele,
        /// <summary>Elements visibles dans la vue active.</summary>
        VueActive,
        /// <summary>Elements selectionnes (et armatures qu'ils portent).</summary>
        Selection
    }

    /// <summary>Portee d'une analyse, choisie par l'utilisateur.</summary>
    public sealed class AnalysisScope
    {
        public ScopeMode Mode { get; set; } = ScopeMode.Modele;

        /// <summary>Niveaux retenus (noms) ; vide = tous. Un element sans niveau est garde.</summary>
        public List<string> Levels { get; set; } = new List<string>();

        public string Describe()
        {
            string mode = Mode == ScopeMode.Modele ? "modèle entier" : Mode == ScopeMode.VueActive ? "vue active" : "sélection";
            return Levels == null || Levels.Count == 0 ? mode : mode + ", niveaux : " + string.Join(", ", Levels);
        }
    }

    /// <summary>
    /// Avancement d'une longue operation et demande d'annulation. L'extraction appelle Report a
    /// intervalles reguliers et s'arrete proprement (OperationCanceledException) si l'utilisateur
    /// annule : un metre partiel n'est jamais presente comme complet.
    /// </summary>
    public interface IProgressSink
    {
        void Report(string stage, int done, int total);

        CancellationToken Cancellation { get; }
    }

    /// <summary>Progression muette (diagnostic, tests).</summary>
    public sealed class NoProgress : IProgressSink
    {
        public static readonly NoProgress Instance = new NoProgress();

        public void Report(string stage, int done, int total)
        {
        }

        public CancellationToken Cancellation => CancellationToken.None;
    }

    public static class ProgressExtensions
    {
        /// <summary>Signale l'avancement tous les <paramref name="every"/> elements et verifie l'annulation.</summary>
        public static void Step(this IProgressSink sink, string stage, int done, int total, int every = 25)
        {
            if (sink == null)
            {
                return;
            }
            if (done % every == 0 || done == total)
            {
                sink.Report(stage, done, total);
                sink.Cancellation.ThrowIfCancellationRequested();
            }
        }
    }
}
