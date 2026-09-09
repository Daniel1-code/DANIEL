using System.Collections.Generic;
using System.Linq;

namespace DanCI.Structural.Documentation.Dashboard
{
    /// <summary>Un lot d'elements a colorer de la meme facon.</summary>
    public sealed class ColourGroup
    {
        public ControlBand Band { get; set; }
        public ControlColour Colour { get; set; }
        public IList<string> ElementIds { get; set; }

        public ColourGroup()
        {
            ElementIds = new List<string>();
        }
    }

    /// <summary>Ce qu'il y a a peindre, et ce qui n'a pas pu l'etre.</summary>
    public sealed class ColourPlan
    {
        public IList<ColourGroup> Groups { get; set; }

        /// <summary>
        /// Elements sans identifiant : calcules, mais non adressables dans le modele.
        /// Ils sont comptes, pas oublies — un element absent de la vue coloree doit
        /// s'expliquer, sinon la couleur ment par omission.
        /// </summary>
        public int UnaddressableCount { get; set; }

        public ColourPlan()
        {
            Groups = new List<ColourGroup>();
        }

        public int PaintedCount
        {
            get { return Groups.Sum(g => g.ElementIds.Count); }
        }

        /// <summary>La legende du plan : seulement les bandes reellement employees.</summary>
        public IEnumerable<ControlColour> Legend
        {
            get { return Groups.Select(g => g.Colour); }
        }
    }

    /// <summary>
    /// CE QU'IL FAUT PEINDRE, ET DE QUELLE COULEUR. Rien de plus : la pose dans une vue
    /// Revit est du ressort de la couche Revit, et c'est la seule partie qui ne se valide
    /// pas ici.
    ///
    /// Les elements sont regroupes PAR BANDE, parce que c'est ainsi qu'une vue se colore :
    /// un remplacement graphique par bande, applique a beaucoup d'elements, plutot qu'un
    /// remplacement par element. Une bande sans element ne produit aucun groupe — inutile
    /// d'encombrer une vue d'un remplacement qui ne s'applique a rien.
    ///
    /// L'ORDRE DES GROUPES EST CELUI DE LA LEGENDE, du gris au rouge. Une legende qui ne
    /// suit pas l'ordre de gravite se lit deux fois.
    /// </summary>
    public static class ViewColourPlan
    {
        public static ColourPlan Build(IEnumerable<DesignedElement> elements)
        {
            var plan = new ColourPlan();
            if (elements == null) return plan;

            var byBand = new Dictionary<ControlBand, ColourGroup>();

            foreach (DesignedElement element in elements)
            {
                if (element == null) continue;

                if (string.IsNullOrWhiteSpace(element.ElementId))
                {
                    plan.UnaddressableCount++;
                    continue;
                }

                ControlBand band = ControlColours.BandOf(element);

                ColourGroup group;
                if (!byBand.TryGetValue(band, out group))
                {
                    group = new ColourGroup { Band = band, Colour = ControlColours.Of(band) };
                    byBand[band] = group;
                }
                group.ElementIds.Add(element.ElementId);
            }

            // L'ordre de l'enumeration ControlBand est celui de la gravite croissante.
            foreach (ControlBand band in ControlColours.Legend.Select(c => c.Band))
            {
                ColourGroup group;
                if (byBand.TryGetValue(band, out group)) plan.Groups.Add(group);
            }

            return plan;
        }
    }
}
