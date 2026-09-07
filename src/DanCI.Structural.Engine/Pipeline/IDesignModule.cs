using System.Collections.Generic;
using DanCI.Structural.Core.Loads;

namespace DanCI.Structural.Engine.Pipeline
{
    /// <summary>
    /// Contrat commun a tous les modules de dimensionnement. Un module recoit un element,
    /// ses reglages et ses combinaisons, et rend un resultat verifiable. Il ne connait ni
    /// Revit ni l'interface utilisateur, ce qui le rend testable isolement.
    /// </summary>
    public interface IDesignModule<TElement, TSettings, TResult>
    {
        /// <summary>Nom du module, tel qu'il apparait dans les rapports.</summary>
        string Name { get; }

        TResult Design(TElement element, TSettings settings, IReadOnlyList<LoadCombination> combinations);
    }
}
