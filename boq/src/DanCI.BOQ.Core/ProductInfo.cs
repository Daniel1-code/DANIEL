using System.Linq;
using System.Reflection;

namespace DanCI.BOQ.Core
{
    /// <summary>
    /// Identite du produit. Les numeros de version sont suivis separement : savoir avec quelle
    /// version du moteur et de quel schema de regles un BOQ a ete produit fait partie de la
    /// tracabilite (un devis doit pouvoir etre reproduit).
    /// </summary>
    public static class ProductInfo
    {
        public const string Name = "DanCI — Métré & BOQ";
        public const string Publisher = "DanCI (Dan Consult Intelligente), Kinshasa, RDC";
        public const string Author = "AMISI RIDVAN DANIEL, ingénieur civil et structures";

        public const string ApplicationVersion = "0.44.3";

        /// <summary>Version du moteur de metre. Change des qu'une quantite peut changer pour un meme modele.</summary>
        public const string QuantityEngineVersion = "0.6.0";

        /// <summary>Version du schema des fichiers JSON (regles, prix, coefficients, projet).</summary>
        public const string ConfigurationSchemaVersion = "1";

        /// <summary>
        /// Version de Revit pour laquelle l'assembly a ete compilee (2025, 2026, 2027),
        /// lue dans les metadonnees ecrites par Directory.Build.props.
        /// </summary>
        public static string TargetRevitVersion => ReadMetadata("DanCI.RevitVersion") ?? "inconnue";

        /// <summary>Configuration de compilation (R2025, R2026, R2027).</summary>
        public static string BuildConfiguration => ReadMetadata("DanCI.BuildConfiguration") ?? "inconnue";

        private static string ReadMetadata(string key)
        {
            return typeof(ProductInfo).Assembly
                .GetCustomAttributes<AssemblyMetadataAttribute>()
                .Where(a => a.Key == key)
                .Select(a => a.Value)
                .FirstOrDefault();
        }
    }
}
