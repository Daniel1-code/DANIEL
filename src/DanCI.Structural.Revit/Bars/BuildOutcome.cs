using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DanCI.Structural.Revit.Bars
{
    /// <summary>Bilan de la generation des armatures pour un ou plusieurs elements.</summary>
    public sealed class BuildOutcome
    {
        public int ElementsProcessed { get; set; }
        public int LongitudinalSets { get; set; }
        public int LongitudinalBars { get; set; }
        public int StirrupSets { get; set; }
        public int CrossTieSets { get; set; }

        public List<ElementId> Created { get; private set; }
        public List<string> Messages { get; private set; }
        public List<string> Errors { get; private set; }

        public BuildOutcome()
        {
            Created = new List<ElementId>();
            Messages = new List<string>();
            Errors = new List<string>();
        }

        public void Merge(BuildOutcome other)
        {
            ElementsProcessed += other.ElementsProcessed;
            LongitudinalSets += other.LongitudinalSets;
            LongitudinalBars += other.LongitudinalBars;
            StirrupSets += other.StirrupSets;
            CrossTieSets += other.CrossTieSets;
            Created.AddRange(other.Created);
            Messages.AddRange(other.Messages);
            Errors.AddRange(other.Errors);
        }
    }

    /// <summary>
    /// Absorbe les avertissements Revit (armatures en attente depassant de l'element, par
    /// exemple) afin que la generation ne soit pas interrompue par une boite de dialogue.
    /// </summary>
    public sealed class WarningSwallower : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor accessor)
        {
            foreach (FailureMessageAccessor failure in accessor.GetFailureMessages())
            {
                if (failure.GetSeverity() == FailureSeverity.Warning)
                {
                    accessor.DeleteWarning(failure);
                }
            }
            return FailureProcessingResult.Continue;
        }
    }
}
