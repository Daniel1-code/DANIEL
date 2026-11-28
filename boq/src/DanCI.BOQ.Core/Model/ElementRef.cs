using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Reference d'un element pour la selection dans Revit : ElementId dans son document, et,
    /// pour un element de lien RVT, l'instance du lien qui le porte.
    /// </summary>
    public readonly struct ElementRef : IEquatable<ElementRef>
    {
        public ElementRef(long elementId, long? linkInstanceId = null)
        {
            ElementId = elementId;
            LinkInstanceId = linkInstanceId;
        }

        public long ElementId { get; }

        /// <summary>Null pour le modele hote.</summary>
        public long? LinkInstanceId { get; }

        public bool IsLinked => LinkInstanceId.HasValue;

        /// <summary>Parametre pose par l'extraction sur les quantites issues d'un lien.</summary>
        public const string LinkInstanceParameter = "LienInstanceId";

        public static ElementRef Of(ElementQuantity q)
        {
            if (q.Parameters != null && q.Parameters.TryGetValue(LinkInstanceParameter, out string s) &&
                long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out long link))
            {
                return new ElementRef(q.ElementId, link);
            }
            return new ElementRef(q.ElementId);
        }

        public static IReadOnlyCollection<ElementRef> Of(IEnumerable<ElementQuantity> quantities) =>
            quantities.Select(Of).Distinct().ToList();

        public bool Equals(ElementRef other) => ElementId == other.ElementId && LinkInstanceId == other.LinkInstanceId;
        public override bool Equals(object obj) => obj is ElementRef r && Equals(r);
        public override int GetHashCode() => (ElementId.GetHashCode() * 397) ^ LinkInstanceId.GetHashCode();
        public override string ToString() => IsLinked ? ElementId + "@lien " + LinkInstanceId : ElementId.ToString(CultureInfo.InvariantCulture);
    }
}
