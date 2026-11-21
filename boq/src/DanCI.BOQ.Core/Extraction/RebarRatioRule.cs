using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// Armatures ESTIMEES par ratio pour les elements en beton qui ne portent AUCUNE armature
    /// modelisee. Decision par element : un element qui porte au moins une barre modelisee est
    /// metre sur ses barres seules (voir limites : modelisation partielle).
    /// </summary>
    public static class RebarRatioRule
    {
        public const string MethodCode = "RATIO_KG_PER_M3";

        public static List<ElementQuantity> Apply(
            IEnumerable<ElementQuantity> concrete,
            ISet<string> hostsWithRebar,
            IReadOnlyList<RebarRatio> ratios)
        {
            var result = new List<ElementQuantity>();
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            foreach (ElementQuantity c in concrete.Where(q => q.Kind == QuantityKinds.ConcreteVolume))
            {
                if (hostsWithRebar != null && hostsWithRebar.Contains(HostKey(c.SourceDocument, c.ElementId)))
                {
                    continue;
                }
                RebarRatio ratio = ratios?.FirstOrDefault(r => r.Category != null && !r.Category.IsEmpty && r.Category.IsMatch(c.Category));

                var q = new ElementQuantity
                {
                    SourceDocument = c.SourceDocument,
                    ElementId = c.ElementId,
                    UniqueId = c.UniqueId,
                    Category = c.Category,
                    CategoryLabel = c.CategoryLabel,
                    Family = c.Family,
                    TypeName = c.TypeName,
                    Material = c.Material,
                    Level = c.Level,
                    Kind = QuantityKinds.Reinforcement,
                    Unit = BoqUnit.Kilogram,
                    MethodCode = MethodCode,
                    ExcludedByUser = c.ExcludedByUser,
                    Parameters = new Dictionary<string, string>(c.Parameters ?? new Dictionary<string, string>())
                };
                q.Parameters["HoteCategorie"] = c.Category;

                if (ratio == null)
                {
                    q.Value = 0;
                    q.Reliability = Reliability.Missing;
                    q.MethodDescription = "Aucune armature modélisée et aucun ratio pour la catégorie " + c.Category + ".";
                    q.Assumption = "Armatures manquantes : définir un ratio pour " + c.Category + " dans extraction.json, ou modéliser les armatures.";
                }
                else
                {
                    q.Value = c.Value * (double)ratio.KgPerM3;
                    q.Reliability = Reliability.Estimated;
                    q.MethodDescription = "Ratio × volume de béton de l'élément (aucune armature modélisée).";
                    q.Assumption = "ESTIMÉ (ratio) : " + ratio.KgPerM3.ToString("0.##", fr) + " kg/m³ (" +
                                   (ratio.Designation ?? c.Category) + ") × " + c.Value.ToString("0.###", fr) + " m³" +
                                   (ratio.NeedsValidation ? " — ratio à valider" : string.Empty) +
                                   (string.IsNullOrWhiteSpace(ratio.Source) ? string.Empty : " — source : " + ratio.Source);
                    q.Parameters["RatioKgM3"] = ratio.KgPerM3.ToString(CultureInfo.InvariantCulture);
                }
                result.Add(q);
            }
            return result;
        }

        public static string HostKey(string sourceDocument, long elementId) =>
            (sourceDocument ?? string.Empty) + "|" + elementId.ToString(CultureInfo.InvariantCulture);
    }
}
