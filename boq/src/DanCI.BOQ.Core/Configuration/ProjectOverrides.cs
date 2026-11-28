using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Core.Configuration
{
    /// <summary>Applique a la configuration de l'installation les reglages propres au projet.</summary>
    public static class ProjectOverrides
    {
        /// <summary>Libelle des constats issus des parametres du projet.</summary>
        public const string Source = "Paramètres du projet";

        /// <summary>
        /// Devise, TVA, majorations et taux du projet a la place de ceux de l'installation, puis
        /// revalidation : un reglage de projet invalide est une ERREUR, comme dans le fichier.
        /// </summary>
        public static void ApplyEstimate(LoadResult load, ProjectEstimate estimate)
        {
            if (estimate == null || load?.Configuration == null)
            {
                return;
            }
            load.Configuration.Settings = estimate.ApplyTo(load.Configuration.Settings);
            // Les constats de CONTENU du fichier de l'installation sont remplaces ; un fichier illisible reste signale.
            load.Issues.RemoveAll(i => i.File == BoqConfiguration.SettingsFileName && i.Code != "JSON_INVALIDE" && i.Code != "JSON_VIDE" && i.Code != "FICHIER_ABSENT");
            foreach (BoqIssue issue in ConfigurationValidator.Validate(load.Configuration).Where(i => i.File == BoqConfiguration.SettingsFileName))
            {
                load.Issues.Add(new BoqIssue(issue.Severity, issue.Code, issue.Message + " (" + Source + ")") { File = Source, ArticleCode = issue.ArticleCode });
            }
        }
    }
}
