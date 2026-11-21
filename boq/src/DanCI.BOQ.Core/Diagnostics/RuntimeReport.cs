using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace DanCI.BOQ.Core.Diagnostics
{
    /// <summary>
    /// Rapport d'environnement : version de Revit, runtime .NET reellement charge, version de
    /// l'add-in et coherence entre les deux. Ecrit au demarrage dans le journal et affiche par
    /// la commande A propos.
    ///
    /// Pourquoi : Autodesk fait evoluer le runtime de certaines mises a jour 2025/2026 vers
    /// .NET 10. Un add-in qui ne charge plus apres une mise a jour se diagnostique d'abord en
    /// lisant ces deux lignes : quelle version de Revit, quel runtime.
    /// </summary>
    public static class RuntimeReport
    {
        public static IReadOnlyList<string> Build(HostInfo host)
        {
            var lines = new List<string>
            {
                ProductInfo.Name + " " + ProductInfo.ApplicationVersion,
                "Compilé pour      : Revit " + ProductInfo.TargetRevitVersion +
                    " (configuration " + ProductInfo.BuildConfiguration + ")",
                "Moteur de métré   : " + ProductInfo.QuantityEngineVersion,
                "Schéma JSON       : " + ProductInfo.ConfigurationSchemaVersion,
            };

            if (host == null)
            {
                lines.Add("Revit             : (hors Revit)");
            }
            else
            {
                lines.Add("Revit             : " + Or(host.VersionName, "?") +
                          " — version " + Or(host.SubVersionNumber, Or(host.VersionNumber, "?")) +
                          " — build " + Or(host.VersionBuild, "?"));
                if (!string.IsNullOrEmpty(host.Language))
                {
                    lines.Add("Langue Revit      : " + host.Language);
                }
            }

            lines.Add("Runtime .NET      : " + RuntimeInformation.FrameworkDescription);
            lines.Add("Environment.Version : " + Environment.Version);
            lines.Add("Système           : " + RuntimeInformation.OSDescription + " (" +
                      RuntimeInformation.ProcessArchitecture + ")");

            string warning = MismatchWarning(host);
            if (warning != null)
            {
                lines.Add("ATTENTION         : " + warning);
            }
            return lines;
        }

        /// <summary>
        /// Signale une assembly compilee pour une autre annee que le Revit qui la charge
        /// (ex. build 2026 copie dans le dossier 2027). Renvoie null si tout est coherent.
        /// </summary>
        public static string MismatchWarning(HostInfo host)
        {
            if (host == null || string.IsNullOrEmpty(host.VersionNumber))
            {
                return null;
            }
            string target = ProductInfo.TargetRevitVersion;
            if (string.Equals(host.VersionNumber.Trim(), target, StringComparison.Ordinal))
            {
                return null;
            }
            return "cet add-in a été compilé pour Revit " + target +
                   " mais est chargé par Revit " + host.VersionNumber.Trim() +
                   ". Installez le build correspondant à cette version de Revit.";
        }

        private static string Or(string value, string fallback)
        {
            return string.IsNullOrWhiteSpace(value) ? fallback : value;
        }
    }
}
