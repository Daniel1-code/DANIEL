using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Pieux : reconnaissance, marquage des quantites, coffrage retire (coules dans le forage).</summary>
    public static class PileRules
    {
        public const string Parameter = "Pieu";
        public const string Yes = "oui";

        public static bool IsPile(string category, string family, string typeName, PileSettings settings)
        {
            if (category != "OST_StructuralFoundation" || settings?.Patterns == null || settings.Patterns.IsEmpty)
            {
                return false;
            }
            bool Excluded(string name) => !string.IsNullOrEmpty(name) && settings.Exclude != null && !settings.Exclude.IsEmpty && settings.Exclude.IsMatch(name);
            if (Excluded(family) || Excluded(typeName))
            {
                return false;
            }
            return (!string.IsNullOrEmpty(family) && settings.Patterns.IsMatch(family)) ||
                   (!string.IsNullOrEmpty(typeName) && settings.Patterns.IsMatch(typeName));
        }

        /// <summary>
        /// Marque Pieu=oui sur les quantites des pieux (volume, armatures...) pour les articles de pieux,
        /// et RETIRE leur coffrage : un pieu fore est coule contre le terrain. Renvoie le nombre retire.
        /// </summary>
        public static int Apply(List<ElementQuantity> quantities, PileSettings settings, List<BoqIssue> notes)
        {
            int removed = 0;
            var piles = new HashSet<(string, long)>();
            foreach (ElementQuantity q in quantities)
            {
                if (IsPile(q.Category, q.Family, q.TypeName, settings))
                {
                    q.Parameters[Parameter] = Yes;
                    piles.Add((q.SourceDocument ?? string.Empty, q.ElementId));
                }
            }
            foreach (ElementQuantity q in quantities.Where(q => q.Kind == QuantityKinds.Reinforcement && q.Parameters.TryGetValue("HoteId", out _)).ToList())
            {
                // Armatures dont l'hote est un pieu : meme marquage (article d'armatures des pieux possible).
                if (long.TryParse(q.Parameters["HoteId"], NumberStyles.Integer, CultureInfo.InvariantCulture, out long host) &&
                    piles.Contains((q.SourceDocument ?? string.Empty, host)))
                {
                    q.Parameters[Parameter] = Yes;
                }
            }
            removed = quantities.RemoveAll(q => q.Kind == QuantityKinds.Formwork && q.Parameters.TryGetValue(Parameter, out string v) && v == Yes);
            // Ni fouille en puits ni beton de proprete sous un pieu : le forage est l'article 02.07.
            int pits = quantities.RemoveAll(q => (q.Kind == QuantityKinds.Excavation || q.Kind == QuantityKinds.BlindingConcrete) &&
                                                q.Parameters.TryGetValue(Parameter, out string v) && v == Yes);
            if (pits > 0)
            {
                notes?.Add(new BoqIssue(IssueSeverity.Info, "PIEUX_SANS_FOUILLE",
                    pits + " fouille(s) / béton(s) de propreté de pieux retiré(s) : le forage est compté à l'article des pieux."));
            }
            if (removed > 0)
            {
                notes?.Add(new BoqIssue(IssueSeverity.Info, "PIEUX_SANS_COFFRAGE",
                    removed + " quantité(s) de coffrage de pieux retirée(s) : un pieu foré est coulé contre le terrain."));
            }
            return removed;
        }
    }

    /// <summary>Herissonnage et polyane sous un dallage sur terre-plein, a partir de sa surface.</summary>
    public static class SlabOnGradeRule
    {
        public static IEnumerable<(string kind, BoqUnit unit, double value, Reliability reliability, string method, string assumption)> Quantities(
            double areaM2, bool byLowestLevel, SlabOnGradeSettings settings)
        {
            settings = settings ?? new SlabOnGradeSettings();
            if (areaM2 <= 0)
            {
                yield break;
            }
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            string detection = byLowestLevel
                ? "sol du niveau le plus bas supposé posé sur terre-plein (dallages.niveauLePlusBas)"
                : "type de sol reconnu comme dallage sur terre-plein (coffrage.typesSurTerrePlein)";
            if (settings.Polythene)
            {
                yield return (QuantityKinds.Polythene, BoqUnit.SquareMeter, areaM2,
                    byLowestLevel ? Reliability.Estimated : Reliability.Measured,
                    "Surface du dallage (aire calculée par Revit) — " + detection + ".",
                    byLowestLevel ? "ESTIMÉ : " + detection + "." : null);
            }
            if (settings.Hardcore && settings.HardcoreThicknessM > 0)
            {
                yield return (QuantityKinds.Hardcore, BoqUnit.CubicMeter, areaM2 * settings.HardcoreThicknessM, Reliability.Estimated,
                    "Surface du dallage × épaisseur de hérisson.",
                    "ESTIMÉ : épaisseur de hérisson " + (settings.HardcoreThicknessM * 100).ToString("0.#", fr) + " cm (dallages.herissonnageEpaisseurM)" +
                    (byLowestLevel ? " ; " + detection : string.Empty) + ".");
            }
        }
    }
}

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Plinthes : perimetre de la piece moins la largeur des portes qui s'y ouvrent.</summary>
    public static class SkirtingRule
    {
        public const string Parameter = "Ouvrage";
        public const string Value = "Plinthe";

        public static double Length(double perimeterM, IEnumerable<double> doorWidthsM) =>
            Math.Max(0, perimeterM - (doorWidthsM ?? Enumerable.Empty<double>()).Where(w => w > 0).Sum());
    }
}

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>
    /// VRD : les sols exterieurs (type reconnu) et les clotures (garde-corps de type cloture) sont
    /// marques VRD=oui pour leurs articles du lot 11 ; les couches de finition d'un sol exterieur
    /// sont retirees (sa surface est deja l'article de voirie, pas de double compte).
    /// </summary>
    public static class VrdRules
    {
        public const string Parameter = "VRD";
        public const string Yes = "oui";

        public static bool IsExteriorFloor(string category, string typeName, VrdSettings s) =>
            category == "OST_Floors" && Match(s?.ExteriorFloorTypes, typeName);

        public static bool IsFence(string category, string typeName, VrdSettings s) =>
            category == "OST_StairsRailing" && Match(s?.FenceTypes, typeName);

        public static int Apply(List<ElementQuantity> quantities, VrdSettings s, List<BoqIssue> notes)
        {
            int tagged = 0;
            foreach (ElementQuantity q in quantities)
            {
                if (IsExteriorFloor(q.Category, q.TypeName, s) || IsFence(q.Category, q.TypeName, s))
                {
                    q.Parameters[Parameter] = Yes;
                    tagged++;
                }
            }
            int removed = quantities.RemoveAll(q => q.Parameters.TryGetValue(Parameter, out string v) && v == Yes &&
                (q.Kind == QuantityKinds.FloorFinish || q.Kind == QuantityKinds.Paint || q.Kind == QuantityKinds.Plaster ||
                 q.Kind == QuantityKinds.Polythene || q.Kind == QuantityKinds.Hardcore));
            if (removed > 0)
            {
                notes?.Add(new BoqIssue(IssueSeverity.Info, "VRD_FINITIONS_RETIREES",
                    removed + " couche(s) de finition / sous-couche de sols extérieurs retirée(s) : la surface de voirie est comptée à l'article VRD."));
            }
            return tagged;
        }

        private static bool Match(DanCI.BOQ.Core.Rules.PatternList p, string name) =>
            p != null && !p.IsEmpty && !string.IsNullOrEmpty(name) && p.IsMatch(name);
    }
}
