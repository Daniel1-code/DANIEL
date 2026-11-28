using System;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Role d'une face d'un element en beton vis-a-vis du coffrage.</summary>
    public enum FormworkFaceRole
    {
        /// <summary>Face laterale (verticale ou raide) : coffree.</summary>
        Side,
        /// <summary>Sous-face (regarde vers le bas) : coffree.</summary>
        Soffit,
        /// <summary>Dessus peu incline : non coffre.</summary>
        Top,
        /// <summary>Contre le sol (sous-face ou flanc de fondation selon les options) : non coffree.</summary>
        AgainstSoil
    }

    /// <summary>
    /// Classe une face d'apres sa normale exterieure et la nature de l'element. La DEDUCTION des
    /// contacts avec les autres elements est faite a part, sur la geometrie (adaptateur Revit).
    /// </summary>
    public static class FormworkFaceRule
    {
        public static FormworkFaceRole Classify(double normalZ, bool isFoundation, bool isSlabOnGrade, FormworkSettings s)
        {
            if (s == null) throw new ArgumentNullException(nameof(s));
            double topLimit = Math.Cos(s.MaxUncoveredTopSlopeDeg * Math.PI / 180.0);

            if (normalZ >= topLimit)
            {
                return FormworkFaceRole.Top;
            }
            if (normalZ <= -topLimit)
            {
                if ((isFoundation && s.ExcludeFoundationBottoms) || isSlabOnGrade)
                {
                    return FormworkFaceRole.AgainstSoil;
                }
                return FormworkFaceRole.Soffit;
            }
            if (isFoundation && s.FoundationsCastAgainstSoil)
            {
                return FormworkFaceRole.AgainstSoil;
            }
            return normalZ < 0 ? FormworkFaceRole.Soffit : FormworkFaceRole.Side;
        }

        public static bool IsFormed(FormworkFaceRole role) => role == FormworkFaceRole.Side || role == FormworkFaceRole.Soffit;
    }
}
