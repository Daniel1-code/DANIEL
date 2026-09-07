using System;
using System.Collections.Generic;
using DanCI.Structural.Core.Loads;

namespace DanCI.Structural.Engine.Pipeline
{
    /// <summary>
    /// Execute un module de dimensionnement sur une serie d'elements. C'est le point d'entree
    /// unique du calcul : l'interface, le mode batch et les tests passent tous par ici, aucun
    /// d'eux n'orchestre le calcul lui-meme.
    /// </summary>
    public static class DesignPipeline
    {
        public static List<TResult> Run<TElement, TSettings, TResult>(
            IDesignModule<TElement, TSettings, TResult> module,
            IEnumerable<TElement> elements,
            TSettings settings,
            IReadOnlyList<LoadCombination> combinations,
            Action<TElement, Exception> onError = null)
        {
            var results = new List<TResult>();
            foreach (TElement element in elements)
            {
                try
                {
                    results.Add(module.Design(element, settings, combinations));
                }
                catch (Exception ex)
                {
                    if (onError == null) throw;
                    onError(element, ex);
                }
            }
            return results;
        }
    }
}
