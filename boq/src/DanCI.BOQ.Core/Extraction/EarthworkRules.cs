using System;
using System.Globalization;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Classe un materiau de finition d'apres son nom ; null si ce n'est pas une finition reconnue.</summary>
    public static class FinishClassifier
    {
        public static string KindOf(string materialName, FinishSettings s)
        {
            if (string.IsNullOrWhiteSpace(materialName) || s == null) return null;
            if (Match(s.Waterproofing, materialName)) return QuantityKinds.Waterproofing;
            if (Match(s.Roofing, materialName)) return QuantityKinds.Roofing;
            if (Match(s.FloorFinish, materialName)) return QuantityKinds.FloorFinish;
            if (Match(s.Paint, materialName)) return QuantityKinds.Paint;
            if (Match(s.Plaster, materialName)) return QuantityKinds.Plaster;
            return null;
        }

        private static bool Match(DanCI.BOQ.Core.Rules.PatternList p, string name) => p != null && !p.IsEmpty && p.IsMatch(name);
    }

    /// <summary>
    /// Volume d'une fouille en pleine masse sous une fondation rectangulaire : tronc de pyramide
    /// (prismatoide) de fond (L + 2s) x (l + 2s) et de hauteur h, elargi en tete de 2 k h si les
    /// parois sont talutees. V = h/3 (A1 + A2 + racine(A1 A2)).
    /// </summary>
    public static class ExcavationRule
    {
        public static double PitVolume(double lengthM, double widthM, double depthM, double marginM, double slopeHperV)
        {
            if (depthM <= 0 || lengthM <= 0 || widthM <= 0) return 0;
            double lb = lengthM + 2 * marginM, wb = widthM + 2 * marginM;
            double lt = lb + 2 * slopeHperV * depthM, wt = wb + 2 * slopeHperV * depthM;
            double a1 = lb * wb, a2 = lt * wt;
            return depthM / 3.0 * (a1 + a2 + Math.Sqrt(a1 * a2));
        }

        public static string Describe(double lengthM, double widthM, double depthM, ExcavationSettings s)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            return "ESTIMÉ : fouille de (" + lengthM.ToString("0.00", fr) + " + 2 × " + s.WorkingMarginM.ToString("0.00", fr) + ") × (" +
                   widthM.ToString("0.00", fr) + " + 2 × " + s.WorkingMarginM.ToString("0.00", fr) + ") m sur " + depthM.ToString("0.00", fr) +
                   " m (du terrain naturel « " + s.NaturalGroundLevel + " » au fond" + (s.IncludeBlinding ? " du béton de propreté" : " de la fondation") + ")" +
                   (s.SlopeHorizontalPerVertical > 0 ? ", talus " + s.SlopeHorizontalPerVertical.ToString("0.##", fr) + " H/1 V" : ", parois verticales") +
                   ". Les fouilles ne sont pas modélisées ; fouilles voisines qui se recoupent comptées deux fois.";
        }
    }

    /// <summary>
    /// Beton de proprete : surface de la face inferieure elargie du debord d, x epaisseur.
    /// Surface elargie d'un contour d'aire A et de perimetre P : A + P d + 4 d² (exact pour un
    /// rectangle, a ses angles vifs ; legerement par exces pour un contour courbe).
    /// </summary>
    public static class BlindingRule
    {
        public static double Volume(double bottomAreaM2, double perimeterM, BlindingSettings s)
        {
            if (bottomAreaM2 <= 0 || s == null || s.ThicknessM <= 0) return 0;
            double d = Math.Max(0, s.OverhangM);
            return (bottomAreaM2 + perimeterM * d + 4 * d * d) * s.ThicknessM;
        }
    }
}
