using DanCI.Structural.Core.Elements;

namespace DanCI.Structural.Revit.Geometry
{
    /// <summary>Une volee d'escalier lue par le plugin : ses donnees de calcul et son repere.</summary>
    public sealed class RevitStair
    {
        public StairData Data { get; set; }
        public RevitElementFrame Frame { get; set; }

        /// <summary>
        /// L'element selectionne peut-il recevoir des armatures ?
        ///
        /// Un escalier Revit porte toute la geometrie utile au calcul, mais rien ne dit
        /// qu'il accepte des objets Rebar. La question est posee a l'API au moment de la
        /// lecture plutot que supposee : <see cref="RebarHostMessage"/> porte la reponse.
        /// </summary>
        public bool CanHostRebar { get; set; }

        /// <summary>Ce que l'API a repondu, en clair, quand elle a refuse l'element.</summary>
        public string RebarHostMessage { get; set; }
    }
}
