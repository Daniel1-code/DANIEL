using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using DanCI.Structural.Core.Units;

namespace DanCI.Structural.Revit.Bars
{
    /// <summary>
    /// Fournit les types de barres et les types de crochets necessaires. Si le diametre
    /// demande n'existe pas dans le projet, un type est duplique puis ajuste : le plugin
    /// n'impose donc aucune bibliotheque particuliere.
    /// </summary>
    public class RebarTypeProvider
    {
        private readonly Document _document;
        private readonly Dictionary<int, RebarBarType> _cache = new Dictionary<int, RebarBarType>();
        private readonly List<string> _notes = new List<string>();

        public RebarTypeProvider(Document document)
        {
            _document = document;
        }

        public IEnumerable<string> Notes { get { return _notes; } }

        /// <summary>Le projet contient-il au moins un type de barre d'armature ?</summary>
        public bool HasAnyBarType
        {
            get { return CollectBarTypes().Count > 0; }
        }

        private List<RebarBarType> CollectBarTypes()
        {
            return new FilteredElementCollector(_document)
                .OfClass(typeof(RebarBarType))
                .Cast<RebarBarType>()
                .ToList();
        }

        /// <summary>
        /// Renvoie le type de barre correspondant au diametre demande (mm), en le creant
        /// par duplication si necessaire. Doit etre appele dans une transaction ouverte.
        /// </summary>
        public RebarBarType GetBarType(double diameterMm)
        {
            int key = (int)Math.Round(diameterMm * 10.0);
            RebarBarType cached;
            if (_cache.TryGetValue(key, out cached)) return cached;

            List<RebarBarType> types = CollectBarTypes();
            if (types.Count == 0) return null;

            double targetFeet = UnitSystem.MmToFeet(diameterMm);
            RebarBarType closest = null;
            double bestDelta = double.MaxValue;
            foreach (RebarBarType type in types)
            {
                double delta = Math.Abs(GetDiameterFeet(type) - targetFeet);
                if (delta < bestDelta)
                {
                    bestDelta = delta;
                    closest = type;
                }
            }

            if (UnitSystem.FeetToMm(bestDelta) <= 0.2)
            {
                _cache[key] = closest;
                return closest;
            }

            RebarBarType created = TryCreateBarType(closest, diameterMm);
            if (created != null)
            {
                _notes.Add(string.Format("Type de barre \"{0}\" cree automatiquement (diametre {1:0} mm).",
                    created.Name, diameterMm));
                _cache[key] = created;
                return created;
            }

            _notes.Add(string.Format(
                "Aucun type de barre HA{0:0} dans le projet : le type \"{1}\" (diametre {2:0.0} mm) " +
                "a ete utilise a la place.", diameterMm, closest.Name, UnitSystem.FeetToMm(GetDiameterFeet(closest))));
            _cache[key] = closest;
            return closest;
        }

        private RebarBarType TryCreateBarType(RebarBarType template, double diameterMm)
        {
            try
            {
                string name = UniqueName(string.Format("HA{0:0}", diameterMm));
                var duplicate = template.Duplicate(name) as RebarBarType;
                if (duplicate == null) return null;

                double feet = UnitSystem.MmToFeet(diameterMm);
                duplicate.BarNominalDiameter = feet;
                try
                {
                    duplicate.BarModelDiameter = feet;
                }
                catch (Exception)
                {
                    // Certains types pilotent le diametre modele par le diametre nominal.
                }
                return duplicate;
            }
            catch (Exception)
            {
                return null;
            }
        }

        private string UniqueName(string baseName)
        {
            var existing = new HashSet<string>(
                new FilteredElementCollector(_document)
                    .OfClass(typeof(RebarBarType))
                    .Select(e => e.Name));
            if (!existing.Contains(baseName)) return baseName;
            for (int i = 2; i < 100; i++)
            {
                string candidate = baseName + "_" + i;
                if (!existing.Contains(candidate)) return candidate;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N").Substring(0, 6);
        }

        private static double GetDiameterFeet(RebarBarType type)
        {
            try
            {
                return type.BarNominalDiameter;
            }
            catch (Exception)
            {
                Parameter parameter = type.get_Parameter(BuiltInParameter.REBAR_BAR_DIAMETER);
                return parameter != null ? parameter.AsDouble() : 0.0;
            }
        }

        private RebarHookType _stirrupHook;
        private bool _stirrupHookResolved;

        /// <summary>
        /// Crochet a utiliser pour les cadres et les epingles : 135 degres en priorite,
        /// 90 degres a defaut, aucun crochet si le projet n'en contient pas.
        /// </summary>
        public RebarHookType GetStirrupHook()
        {
            if (_stirrupHookResolved) return _stirrupHook;
            _stirrupHookResolved = true;

            var hooks = new FilteredElementCollector(_document)
                .OfClass(typeof(RebarHookType))
                .Cast<RebarHookType>()
                .ToList();

            RebarHookType at135 = null;
            RebarHookType at90 = null;
            foreach (RebarHookType hook in hooks)
            {
                Parameter angle = hook.get_Parameter(BuiltInParameter.REBAR_HOOK_ANGLE);
                if (angle == null || !angle.HasValue) continue;
                double degrees = UnitSystem.RadiansToDegrees(angle.AsDouble());
                if (Math.Abs(degrees - 135.0) < 5.0 && at135 == null) at135 = hook;
                if (Math.Abs(degrees - 90.0) < 5.0 && at90 == null) at90 = hook;
            }

            _stirrupHook = at135 ?? at90;
            if (_stirrupHook == null)
            {
                _notes.Add("Aucun type de crochet trouve dans le projet : les cadres sont crees " +
                           "sans crochet. Chargez un type de crochet a 135 degres pour respecter " +
                           "les dispositions constructives.");
            }
            else if (at135 == null)
            {
                _notes.Add("Aucun crochet a 135 degres dans le projet : un crochet a 90 degres " +
                           "a ete utilise pour les cadres.");
            }
            return _stirrupHook;
        }
    }
}
