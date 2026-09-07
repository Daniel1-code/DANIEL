using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace DanCI.Structural.Revit.Storage
{
    /// <summary>
    /// Trace d'un calcul attache a un element du modele : avec quel moteur, sous quelle
    /// norme, a partir de quelles donnees, et quelles armatures en sont issues.
    /// </summary>
    public sealed class DesignRecord
    {
        /// <summary>Module ayant produit le calcul, par exemple "DanCI Column Design".</summary>
        public string Module { get; set; }

        /// <summary>Version du moteur de calcul.</summary>
        public string EngineVersion { get; set; }

        /// <summary>Norme et Annexe Nationale appliquees.</summary>
        public string CodeLabel { get; set; }

        /// <summary>Empreinte des donnees d'entree, pour detecter un modele modifie.</summary>
        public string InputHash { get; set; }

        /// <summary>Resume du ferraillage retenu, lisible directement dans Revit.</summary>
        public string Summary { get; set; }

        /// <summary>Date du calcul, au format ISO 8601 UTC.</summary>
        public string TimestampUtc { get; set; }

        /// <summary>Armatures creees par ce calcul, pour pouvoir les remplacer.</summary>
        public List<ElementId> RebarIds { get; private set; }

        public DesignRecord()
        {
            RebarIds = new List<ElementId>();
            TimestampUtc = DateTime.UtcNow.ToString("o");
        }

        public DateTime? Timestamp
        {
            get
            {
                DateTime parsed;
                return DateTime.TryParse(TimestampUtc, null,
                    System.Globalization.DateTimeStyles.RoundtripKind, out parsed)
                    ? parsed : (DateTime?)null;
            }
        }
    }
}
