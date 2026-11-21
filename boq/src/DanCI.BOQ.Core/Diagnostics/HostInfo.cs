namespace DanCI.BOQ.Core.Diagnostics
{
    /// <summary>
    /// Description de l'application hote (Revit), sans dependance a l'API Revit :
    /// la couche Addin la remplit, le Core la formate et la teste.
    /// </summary>
    public sealed class HostInfo
    {
        /// <summary>Annee majeure, ex. "2026" (ControlledApplication.VersionNumber).</summary>
        public string VersionNumber { get; set; }

        /// <summary>Version de mise a jour, ex. "2026.5" (SubVersionNumber).</summary>
        public string SubVersionNumber { get; set; }

        /// <summary>Numero de build, ex. "26.5.0.xx" (VersionBuild).</summary>
        public string VersionBuild { get; set; }

        /// <summary>Nom commercial, ex. "Autodesk Revit 2026" (VersionName).</summary>
        public string VersionName { get; set; }

        /// <summary>Langue de l'interface Revit.</summary>
        public string Language { get; set; }
    }
}
