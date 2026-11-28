using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Extraction
{
    /// <summary>Contenu de profils_acier.json : masses lineiques par designation de profile.</summary>
    public sealed class SteelProfileTable
    {
        [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
        [JsonPropertyName("source")] public string Source { get; set; }

        /// <summary>Designation normalisee (IPE200, HEA200, HEB200, UPN200...) -&gt; kg/m.</summary>
        [JsonPropertyName("masses")] public Dictionary<string, decimal> KgPerMeter { get; set; } = new Dictionary<string, decimal>();

        public bool TryFind(string typeName, out string designation, out decimal kgPerMeter)
        {
            designation = SteelProfileName.Normalize(typeName);
            kgPerMeter = 0;
            return designation != null && KgPerMeter != null && KgPerMeter.TryGetValue(designation, out kgPerMeter);
        }
    }

    /// <summary>
    /// Reconnaissance de la designation d'un profile dans un nom de type Revit : « IPE 200 »,
    /// « IPE200 », « HE200A » / « HEA 200 », « UPN-200 ». Rien d'autre n'est devine.
    /// </summary>
    public static class SteelProfileName
    {
        private static readonly Regex Ipe = new Regex(@"(?<![A-Z])(?<s>IPE|IPN|UPN|UPE|HEA|HEB|HEM)[\s\-_]*(?<h>\d{2,4})(?!\d)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        private static readonly Regex HeSuffix = new Regex(@"(?<![A-Z])HE[\s\-_]*(?<h>\d{2,4})[\s\-_]*(?<s>[ABM])(?![A-Z])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static string Normalize(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }
            Match m = HeSuffix.Match(typeName);
            if (m.Success)
            {
                return "HE" + m.Groups["s"].Value.ToUpperInvariant() + m.Groups["h"].Value;
            }
            m = Ipe.Match(typeName);
            return m.Success ? m.Groups["s"].Value.ToUpperInvariant() + m.Groups["h"].Value : null;
        }
    }

    /// <summary>Masse d'un element de charpente et la facon dont elle a ete obtenue.</summary>
    public sealed class SteelMass
    {
        public double Kg { get; set; }
        public string MethodCode { get; set; }
        public string MethodDescription { get; set; }
        public Reliability Reliability { get; set; }
        public string Assumption { get; set; }
    }

    /// <summary>
    /// Masse d'un profile, par ordre de preference :
    ///  1. longueur de coupe x masse lineique lue dans le TYPE Revit (poids nominal) ;
    ///  2. longueur de coupe x masse lineique de la table profils_acier.json ;
    ///  3. volume geometrique x masse volumique — ESTIME (la geometrie des familles omet
    ///     souvent les conges : masse legerement sous-estimee).
    /// Sans rien de tout cela : MANQUANT.
    /// </summary>
    public static class SteelMassRule
    {
        public static SteelMass Resolve(string typeName, double? typeKgPerMeter, double? cutLengthM, double? volumeM3,
            SteelProfileTable table, SteelSettings settings)
        {
            var fr = CultureInfo.GetCultureInfo("fr-FR");
            if (cutLengthM.HasValue && cutLengthM.Value > 0)
            {
                if (typeKgPerMeter.HasValue && typeKgPerMeter.Value > 0)
                {
                    return new SteelMass
                    {
                        Kg = cutLengthM.Value * typeKgPerMeter.Value,
                        MethodCode = "CUT_LENGTH_X_TYPE_MASS",
                        MethodDescription = "Longueur de coupe " + cutLengthM.Value.ToString("0.###", fr) + " m × " +
                                            typeKgPerMeter.Value.ToString("0.##", fr) + " kg/m (poids nominal du type).",
                        Reliability = Reliability.Measured
                    };
                }
                if (table != null && table.TryFind(typeName, out string designation, out decimal kgm))
                {
                    return new SteelMass
                    {
                        Kg = cutLengthM.Value * (double)kgm,
                        MethodCode = "CUT_LENGTH_X_TABLE_MASS",
                        MethodDescription = "Longueur de coupe " + cutLengthM.Value.ToString("0.###", fr) + " m × " +
                                            kgm.ToString("0.##", fr) + " kg/m (" + designation + ", table profils_acier.json).",
                        Reliability = Reliability.Measured
                    };
                }
            }
            if (volumeM3.HasValue && volumeM3.Value > 0)
            {
                decimal density = settings?.DensityKgPerM3 ?? 7850m;
                return new SteelMass
                {
                    Kg = volumeM3.Value * (double)density,
                    MethodCode = "SOLID_VOLUME_X_DENSITY",
                    MethodDescription = "Volume géométrique × " + density.ToString("0", fr) + " kg/m³.",
                    Reliability = Reliability.Estimated,
                    Assumption = "Masse linéique inconnue (ni poids nominal du type, ni profil reconnu dans la table) : " +
                                 "volume de la géométrie × " + density.ToString("0", fr) + " kg/m³. " +
                                 "Sous-estimation possible si la famille omet les congés."
                };
            }
            return new SteelMass
            {
                Kg = 0,
                MethodCode = "NONE",
                MethodDescription = "Ni longueur ni volume lisibles.",
                Reliability = Reliability.Missing,
                Assumption = "Masse de charpente manquante : profil sans masse linéique ni géométrie lisible."
            };
        }
    }
}
