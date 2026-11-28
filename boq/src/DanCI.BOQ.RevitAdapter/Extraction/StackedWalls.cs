using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Murs empiles : le parent n'a pas de geometrie propre, ses membres sont des murs. Que la
    /// collecte renvoie ou non les membres, on les insere juste apres leur parent ; l'appelant
    /// ecarte les doublons par ElementId. Chaque membre est donc compte exactement une fois.
    /// </summary>
    internal static class StackedWalls
    {
        public static IEnumerable<Element> Expand(Document doc, IEnumerable<Element> elements)
        {
            foreach (Element e in elements)
            {
                yield return e;
                if (e is Wall wall && wall.IsStackedWall)
                {
                    foreach (ElementId id in wall.GetStackedWallMemberIds())
                    {
                        Element member = doc.GetElement(id);
                        if (member != null)
                        {
                            yield return member;
                        }
                    }
                }
            }
        }
    }
}
