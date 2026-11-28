using System.Collections.Generic;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Une quantite mesuree sur UN element, avec tout ce qu'il faut pour la retrouver et la
    /// defendre : identifiant Revit, categorie, type, materiau, niveau, methode, fiabilite.
    /// C'est l'unite de tracabilite du BOQ : chaque ligne du devis est une somme de ces objets.
    /// Produit par l'adaptateur Revit ; ne contient aucun type Revit.
    /// </summary>
    public sealed class ElementQuantity
    {
        /// <summary>
        /// Document d'origine : vide pour le modele hote, nom du lien RVT sinon. Avec ElementId,
        /// forme la cle unique de l'element (deux liens peuvent contenir le meme ElementId).
        /// </summary>
        public string SourceDocument { get; set; } = string.Empty;

        /// <summary>ElementId.Value (64 bits depuis Revit 2024).</summary>
        public long ElementId { get; set; }

        /// <summary>UniqueId Revit, stable entre sessions.</summary>
        public string UniqueId { get; set; }

        /// <summary>
        /// Categorie sous forme de nom BuiltInCategory (ex. OST_StructuralColumns) : independant
        /// de la langue de Revit. Les regles s'ecrivent avec ce nom.
        /// </summary>
        public string Category { get; set; }

        /// <summary>Nom de categorie affiche (langue de Revit), pour le detail du metre.</summary>
        public string CategoryLabel { get; set; }

        public string Family { get; set; }
        public string TypeName { get; set; }
        public string Material { get; set; }
        public string Level { get; set; }

        /// <summary>Nature de la quantite (voir <see cref="QuantityKinds"/>).</summary>
        public string Kind { get; set; }

        /// <summary>Valeur dans l'unite <see cref="Unit"/> (deja convertie depuis les unites internes Revit).</summary>
        public double Value { get; set; }

        public BoqUnit Unit { get; set; }

        /// <summary>Code de methode de mesure (ex. SOLID_VOLUME_BY_MATERIAL, RATIO_KG_PER_M3).</summary>
        public string MethodCode { get; set; }

        /// <summary>Description lisible de la methode, reprise dans le detail du metre.</summary>
        public string MethodDescription { get; set; }

        public Reliability Reliability { get; set; } = Reliability.Measured;

        /// <summary>Hypothese retenue si la quantite est estimee (ex. « ratio 90 kg/m³, poteaux »). Obligatoire si Estimated.</summary>
        public string Assumption { get; set; }

        /// <summary>Element marque DanCI_BOQ_Exclure = Oui.</summary>
        public bool ExcludedByUser { get; set; }

        /// <summary>
        /// Parametres lus sur l'element ou son type, utilisables par les regles. L'extraction ajoute
        /// des pseudo-parametres : HoteCategorie (categorie de l'hote d'une armature), Phase...
        /// </summary>
        public IDictionary<string, string> Parameters { get; set; } = new Dictionary<string, string>();

        /// <summary>Cle de tri deterministe : document, element, nature.</summary>
        internal string SortKey =>
            (SourceDocument ?? string.Empty) + "\u0001" + ElementId.ToString("D20") + "\u0001" +
            (Kind ?? string.Empty) + "\u0001" + (Material ?? string.Empty);
    }
}
