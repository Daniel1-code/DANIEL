using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace ArmaturesPoteaux.RevitOps
{
    /// <summary>Bilan de la generation des armatures pour un ou plusieurs poteaux.</summary>
    public class BuildOutcome
    {
        public int ColumnsProcessed { get; set; }
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
            ColumnsProcessed += other.ColumnsProcessed;
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
    /// Absorbe les avertissements Revit (armatures depassant du poteau pour les attentes,
    /// par exemple) afin que la generation ne soit pas interrompue par une boite de dialogue.
    /// </summary>
    public class WarningSwallower : IFailuresPreprocessor
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
