namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Nature d'une quantite. Un meme element produit plusieurs quantites (un poteau donne un
    /// volume de beton, une surface de coffrage, une masse d'armatures) : les regles de mapping
    /// les distinguent par leur nature, jamais par leur seule categorie Revit.
    /// Les regles peuvent utiliser d'autres valeurs ; celles-ci sont celles que produit
    /// l'adaptateur Revit (un avertissement signale une nature inconnue dans une regle).
    /// </summary>
    public static class QuantityKinds
    {
        public const string Excavation = "Terrassement";
        public const string BlindingConcrete = "BetonProprete";
        public const string ConcreteVolume = "VolumeBeton";
        public const string Formwork = "Coffrage";
        public const string Reinforcement = "Armature";
        public const string StructuralSteel = "AcierCharpente";
        public const string SteelConnections = "AssemblagesAcier";
        public const string Masonry = "Maconnerie";
        public const string Plaster = "Enduit";
        public const string Paint = "Peinture";
        public const string FloorFinish = "RevetementSol";
        public const string Waterproofing = "Etancheite";
        public const string Roofing = "Couverture";
        public const string Count = "Comptage";
        public const string Length = "Longueur";
        public const string Volume = "Volume";

        public static readonly string[] All =
        {
            Excavation, BlindingConcrete, ConcreteVolume, Formwork, Reinforcement, StructuralSteel,
            SteelConnections, Masonry, Plaster, Paint, FloorFinish, Waterproofing, Roofing, Count,
            Length, Volume
        };
    }
}
