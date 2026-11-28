using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Reconnaissance des materiaux beton, par ordre de fiabilite decroissante :
    ///  1. actif structurel de classe Beton (independant de la langue et du nom) ;
    ///  2. classe de materiau « Béton » / « Concrete » ;
    ///  3. motif sur le nom (extraction.json).
    /// La raison est conservee dans la quantite (parametre ReconnaissanceBeton).
    /// </summary>
    internal sealed class MaterialClassifier
    {
        private readonly Document doc;
        private readonly ExtractionSettings settings;
        private readonly Dictionary<long, string> cache = new Dictionary<long, string>();

        public MaterialClassifier(Document doc, ExtractionSettings settings)
        {
            this.doc = doc;
            this.settings = settings;
        }

        /// <summary>Raison de la reconnaissance, ou null si le materiau n'est pas du beton.</summary>
        public string ConcreteReason(ElementId materialId)
        {
            if (materialId == null || materialId == ElementId.InvalidElementId)
            {
                return null;
            }
            if (cache.TryGetValue(materialId.Value, out string cached))
            {
                return cached;
            }
            string reason = Classify(doc.GetElement(materialId) as Material);
            cache[materialId.Value] = reason;
            return reason;
        }

        private string Classify(Material m)
        {
            if (m == null)
            {
                return null;
            }
            if (m.StructuralAssetId != null && m.StructuralAssetId != ElementId.InvalidElementId &&
                doc.GetElement(m.StructuralAssetId) is PropertySetElement pse)
            {
                StructuralAsset asset = pse.GetStructuralAsset();
                if (asset != null && asset.StructuralAssetClass == StructuralAssetClass.Concrete)
                {
                    return "ActifStructurel";
                }
            }
            string materialClass = m.MaterialClass ?? string.Empty;
            if (materialClass.Equals("Béton", StringComparison.OrdinalIgnoreCase) ||
                materialClass.Equals("Beton", StringComparison.OrdinalIgnoreCase) ||
                materialClass.Equals("Concrete", StringComparison.OrdinalIgnoreCase))
            {
                return "ClasseMateriau";
            }
            if (settings.ConcreteMaterialNames != null && !settings.ConcreteMaterialNames.IsEmpty &&
                settings.ConcreteMaterialNames.IsMatch(m.Name))
            {
                return "NomMateriau";
            }
            return null;
        }
    }
}
