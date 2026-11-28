namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Statut d'un element vis-a-vis de la phase metree, ecrit dans le parametre StatutPhase.</summary>
    public static class PhaseStatus
    {
        public const string Parameter = "StatutPhase";

        /// <summary>Cree dans la phase metree : ouvrage neuf a construire.</summary>
        public const string New = "Nouveau";

        /// <summary>Demoli dans la phase metree : lot Demolitions (si l'option est choisie).</summary>
        public const string Demolished = "Demoli";

        /// <summary>Parametre pose sur une Part : ElementId de l'element d'origine.</summary>
        public const string SourceElementParameter = "SourceElementId";
    }
}
