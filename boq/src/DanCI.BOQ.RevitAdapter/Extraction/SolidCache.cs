using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>Solides de la geometrie des elements (coordonnees du modele), lus une seule fois par analyse.</summary>
    internal sealed class SolidCache
    {
        private readonly Dictionary<long, List<Solid>> cache = new Dictionary<long, List<Solid>>();

        public List<Solid> Of(Element e)
        {
            if (cache.TryGetValue(e.Id.Value, out List<Solid> cached))
            {
                return cached;
            }
            var list = new List<Solid>();
            var options = new Options { DetailLevel = ViewDetailLevel.Fine, ComputeReferences = false, IncludeNonVisibleObjects = false };
            Collect(e.get_Geometry(options), list);
            cache[e.Id.Value] = list;
            return list;
        }

        private static void Collect(GeometryElement geometry, List<Solid> list)
        {
            if (geometry == null)
            {
                return;
            }
            foreach (GeometryObject obj in geometry)
            {
                if (obj is Solid s && s.Volume > 1e-9 && s.Faces.Size > 0)
                {
                    list.Add(s);
                }
                else if (obj is GeometryInstance gi)
                {
                    Collect(gi.GetInstanceGeometry(), list);
                }
            }
        }
    }
}
