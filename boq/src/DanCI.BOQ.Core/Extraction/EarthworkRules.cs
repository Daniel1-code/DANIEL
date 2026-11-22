using System;
using System.Collections.Generic;
using System.Linq;
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

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Bilan des terres, element par element (chaque quantite reste tracable) :
    ///  - remblai d'une fondation = sa fouille - son beton - son beton de proprete (jamais negatif) ;
    ///  - evacuation = (deblai non remblaye) x foisonnement, pour les fouilles, la pleine masse
    ///    et, en option, la terre vegetale decapee.
    /// Derive de quantites deja extraites : rien n'est mesure deux fois.
    /// </summary>
    /// <summary>Fouille en rigole sous mur : calcul pur, testable.</summary>
    public static class WallTrench
    {
        public const string Work = "FouilleMur";
        public const string BuriedParameter = "VolumeEnterreM3";

        /// <summary>Largeur de la rigole : epaisseur du mur + 2 surlargeurs, au moins la largeur minimale.</summary>
        public static double Width(double wallThicknessM, WallTrenchSettings s) =>
            Math.Max(s.MinWidthM, wallThicknessM + 2 * Math.Max(0, s.SideMarginM));

        /// <summary>Longueur utile : trace du mur moins une demi-largeur a chaque extremite jointe a un autre mur (pas de double comptage aux croisements).</summary>
        public static double Length(double curveLengthM, double widthM, int joinedEnds) =>
            Math.Max(0, curveLengthM - Math.Max(0, Math.Min(2, joinedEnds)) * widthM / 2);
    }

    public static class EarthworkBalance
    {
        public static void Apply(List<ElementQuantity> quantities, GeneralEarthworkSettings s, List<BoqIssue> notes)
        {
            s = s ?? new GeneralEarthworkSettings();
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            double f = s.BulkingFactor > 0 ? s.BulkingFactor : 1.0;
            var added = new List<ElementQuantity>();
            foreach (var g in quantities.Where(q => q.Kind == QuantityKinds.Excavation)
                         .GroupBy(q => (q.SourceDocument ?? string.Empty, q.ElementId, q.Parameters != null && q.Parameters.TryGetValue("Ouvrage", out string o) ? o : string.Empty)).ToList())
            {
                ElementQuantity pit = g.First();
                double cut = g.Sum(q => q.Value);
                var sameElement = quantities.Where(q => (q.SourceDocument ?? string.Empty) == g.Key.Item1 && q.ElementId == g.Key.Item2).ToList();
                // Fouille sous mur : le beton du MUR (toute sa hauteur) n'est pas enterre ; seul compte le volume enterre estime.
                bool wallTrench = pit.Parameters != null && pit.Parameters.TryGetValue("Ouvrage", out string work) && work == WallTrench.Work;
                double concrete = wallTrench ? 0 : sameElement.Where(q => q.Kind == QuantityKinds.ConcreteVolume && q.Unit == BoqUnit.CubicMeter).Sum(q => q.Value);
                double blinding = wallTrench ? 0 : sameElement.Where(q => q.Kind == QuantityKinds.BlindingConcrete && q.Unit == BoqUnit.CubicMeter).Sum(q => q.Value);
                // Fouille sous mur : volume enterre estime (mur / soubassement) porte par la quantite.
                double buried = g.Sum(q => q.Parameters != null && q.Parameters.TryGetValue(WallTrench.BuriedParameter, out string bv) &&
                                           double.TryParse(bv, NumberStyles.Float, CultureInfo.InvariantCulture, out double d) ? d : 0);
                double backfill = Math.Max(0, cut - concrete - blinding - buried);
                if (s.Backfill && backfill > 0)
                {
                    added.Add(Derived(pit, QuantityKinds.Backfill, backfill, "BACKFILL_PIT_MINUS_BURIED",
                        "Fouille (" + cut.ToString("0.###", fr) + " m³) − béton de la fondation (" + concrete.ToString("0.###", fr) + ") − béton de propreté (" +
                        blinding.ToString("0.###", fr) + ").", pit.Reliability, pit.Assumption));
                }
                double spoil = (s.Backfill ? cut - backfill : cut) * f;
                if (s.SpoilRemoval && spoil > 0)
                {
                    added.Add(Derived(pit, QuantityKinds.SpoilRemoval, spoil, "SPOIL_PIT",
                        "(Fouille − remblai) × foisonnement " + f.ToString("0.##", fr) + ".", Reliability.Estimated,
                        "ESTIMÉ : coefficient de foisonnement " + f.ToString("0.##", fr) + " (terrassementsGeneraux.coefficientFoisonnement)."));
                }
            }
            if (s.SpoilRemoval)
            {
                foreach (ElementQuantity bulk in quantities.Where(q => q.Kind == QuantityKinds.BulkExcavation))
                {
                    added.Add(Derived(bulk, QuantityKinds.SpoilRemoval, bulk.Value * f, "SPOIL_BULK",
                        "Pleine masse × foisonnement " + f.ToString("0.##", fr) + ".", Reliability.Estimated,
                        "ESTIMÉ : coefficient de foisonnement " + f.ToString("0.##", fr) + "."));
                }
                if (s.RemoveTopsoil && s.StrippingThicknessM > 0)
                {
                    foreach (ElementQuantity strip in quantities.Where(q => q.Kind == QuantityKinds.Stripping))
                    {
                        added.Add(Derived(strip, QuantityKinds.SpoilRemoval, strip.Value * s.StrippingThicknessM * f, "SPOIL_TOPSOIL",
                            "Décapage × " + (s.StrippingThicknessM * 100).ToString("0.#", fr) + " cm × foisonnement " + f.ToString("0.##", fr) + ".",
                            Reliability.Estimated, "ESTIMÉ : épaisseur de terre végétale et foisonnement à valider."));
                    }
                }
            }
            quantities.AddRange(added);
            if (added.Count > 0)
            {
                notes?.Add(new BoqIssue(IssueSeverity.Info, "BILAN_DES_TERRES",
                    "Bilan des terres : " + added.Count(q => q.Kind == QuantityKinds.Backfill) + " remblai(s), " +
                    added.Count(q => q.Kind == QuantityKinds.SpoilRemoval) + " évacuation(s), calculés à partir des fouilles, de la pleine masse et du décapage."));
            }
        }

        /// <summary>Surface elargie d'un contour d'aire A et de perimetre P, debord d : A + P d + 4 d².</summary>
        public static double EnlargedArea(double areaM2, double perimeterM, double marginM)
        {
            double d = Math.Max(0, marginM);
            return areaM2 <= 0 ? 0 : areaM2 + perimeterM * d + 4 * d * d;
        }

        private static ElementQuantity Derived(ElementQuantity src, string kind, double value, string method, string description, Reliability reliability, string assumption) =>
            new ElementQuantity
            {
                SourceDocument = src.SourceDocument, ElementId = src.ElementId, UniqueId = src.UniqueId, Category = src.Category,
                CategoryLabel = src.CategoryLabel, Family = src.Family, TypeName = src.TypeName, Material = src.Material, Level = src.Level,
                Kind = kind, Unit = BoqUnit.CubicMeter, Value = Math.Round(value, 4), MethodCode = method, MethodDescription = description,
                Reliability = reliability, Assumption = reliability == Reliability.Measured ? null : assumption,
                Parameters = new Dictionary<string, string>(src.Parameters ?? new Dictionary<string, string>())
            };
    }
}
