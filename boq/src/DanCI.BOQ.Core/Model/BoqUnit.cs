using System;
using System.Collections.Generic;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Unites de metre. Une quantite n'est JAMAIS convertie d'une unite a l'autre par le moteur :
    /// une quantite en m2 rattachee a un article en m3 est une erreur de regle, signalee, et non
    /// une conversion silencieuse.
    /// </summary>
    [System.Text.Json.Serialization.JsonConverter(typeof(DanCI.BOQ.Core.Configuration.BoqUnitJsonConverter))]
    public enum BoqUnit
    {
        /// <summary>Longueur, metre lineaire (ml).</summary>
        LinearMeter,
        /// <summary>Surface (m²).</summary>
        SquareMeter,
        /// <summary>Volume (m³).</summary>
        CubicMeter,
        /// <summary>Masse (kg).</summary>
        Kilogram,
        /// <summary>Masse (t).</summary>
        Tonne,
        /// <summary>Unite, comptage (U).</summary>
        Each,
        /// <summary>Forfait (Ff).</summary>
        LumpSum
    }

    public static class BoqUnits
    {
        private static readonly Dictionary<string, BoqUnit> Aliases =
            new Dictionary<string, BoqUnit>(StringComparer.OrdinalIgnoreCase)
            {
                { "ml", BoqUnit.LinearMeter }, { "m", BoqUnit.LinearMeter },
                { "m2", BoqUnit.SquareMeter }, { "m²", BoqUnit.SquareMeter },
                { "m3", BoqUnit.CubicMeter }, { "m³", BoqUnit.CubicMeter },
                { "kg", BoqUnit.Kilogram },
                { "t", BoqUnit.Tonne },
                { "u", BoqUnit.Each }, { "unité", BoqUnit.Each }, { "unite", BoqUnit.Each },
                { "ff", BoqUnit.LumpSum }, { "forfait", BoqUnit.LumpSum }, { "ens", BoqUnit.LumpSum }
            };

        /// <summary>Symbole affiche dans le BOQ.</summary>
        public static string Symbol(this BoqUnit unit)
        {
            switch (unit)
            {
                case BoqUnit.LinearMeter: return "ml";
                case BoqUnit.SquareMeter: return "m²";
                case BoqUnit.CubicMeter: return "m³";
                case BoqUnit.Kilogram: return "kg";
                case BoqUnit.Tonne: return "t";
                case BoqUnit.Each: return "U";
                case BoqUnit.LumpSum: return "Ff";
                default: throw new ArgumentOutOfRangeException(nameof(unit), unit, null);
            }
        }

        public static bool TryParse(string text, out BoqUnit unit)
        {
            unit = default;
            return text != null && Aliases.TryGetValue(text.Trim(), out unit);
        }

        public static BoqUnit Parse(string text)
        {
            if (TryParse(text, out BoqUnit unit))
            {
                return unit;
            }
            throw new FormatException(
                "Unité inconnue : « " + text + " ». Unités admises : ml, m², m³, kg, t, U, Ff.");
        }
    }
}
