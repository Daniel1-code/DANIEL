namespace DanCI.Structural.App
{
    /// <summary>
    /// Identite et versions du produit. Quatre numeros independants sont suivis : savoir avec
    /// quelle version du moteur un calcul a ete produit fait partie de la tracabilite.
    /// </summary>
    public static class ProductInfo
    {
        public const string Name = "DanCI Structural Studio";
        public const string Tagline = "Structural Design & Reinforcement Automation for Autodesk Revit";

        public const string ApplicationVersion = "3.14.0";
        public const string CalculationEngineVersion = "1.14.0";
        public const string EurocodeLibraryVersion = "1.14.0";
        public const string DesignDataSchemaVersion = "1";
    }
}
