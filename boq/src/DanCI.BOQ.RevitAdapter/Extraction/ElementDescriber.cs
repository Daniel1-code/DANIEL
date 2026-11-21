using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.RevitAdapter.Extraction
{
    /// <summary>
    /// Remplit les champs communs d'une quantite (identite, categorie, famille, type, niveau,
    /// phases, parametres DanCI) et decide si un element doit etre ignore.
    /// </summary>
    internal sealed class ElementDescriber
    {
        public const string ExcludeParameter = "DanCI_BOQ_Exclure";
        private static readonly string[] CopiedParameters = { "DanCI_BOQ_Lot", "DanCI_BOQ_Code" };

        private readonly Document doc;
        private readonly ExtractionSettings settings;
        private readonly string sourceDocument;
        private readonly Dictionary<long, string> names = new Dictionary<long, string>();

        private readonly ISet<long> scope;

        /// <param name="scope">ElementId admis (vue active, selection) ; null = modele entier.</param>
        public ElementDescriber(Document doc, ExtractionSettings settings, string sourceDocument, ISet<long> scope = null)
        {
            this.doc = doc;
            this.settings = settings;
            this.sourceDocument = sourceDocument ?? string.Empty;
            this.scope = scope;
        }

        public Document Document => doc;

        public bool InScope(Element e) => scope == null || (e != null && scope.Contains(e.Id.Value));

        /// <summary>Option de conception secondaire : jamais metree, jamais voisine (seul le principal existe).</summary>
        public static bool IsSecondaryOption(Element e)
        {
            DesignOption option = e.DesignOption;
            return option != null && !option.IsPrimary;
        }

        /// <summary>Element a ignorer : option de conception secondaire, ou hors de la portee choisie.</summary>
        public bool ShouldSkip(Element e, out string reason)
        {
            reason = null;
            if (IsSecondaryOption(e))
            {
                reason = "Option de conception secondaire";
                return true;
            }
            if (!InScope(e))
            {
                reason = "Hors de la portée choisie";
                return true;
            }
            return false;
        }

        public ElementQuantity Describe(Element e)
        {
            ElementType type = doc.GetElement(e.GetTypeId()) as ElementType;
            var q = new ElementQuantity
            {
                SourceDocument = sourceDocument,
                ElementId = e.Id.Value,
                UniqueId = e.UniqueId,
                Category = CategoryName(e.Category),
                CategoryLabel = e.Category?.Name,
                Family = (e as FamilyInstance)?.Symbol?.FamilyName ?? type?.FamilyName,
                TypeName = type?.Name,
                Level = LevelName(e),
                ExcludedByUser = IsExcluded(e, type),
                Parameters = new Dictionary<string, string>(StringComparer.Ordinal)
            };

            BoundingBoxXYZ box = e.get_BoundingBox(null);
            if (box != null)
            {
                // Boite englobante au mm : sert a detecter les elements en double (audit).
                q.Parameters["Position"] = Mm(box.Min.X) + "," + Mm(box.Min.Y) + "," + Mm(box.Min.Z) + ";" +
                                           Mm(box.Max.X) + "," + Mm(box.Max.Y) + "," + Mm(box.Max.Z);
            }
            q.Parameters["PhaseCreation"] = Name(e.CreatedPhaseId);
            q.Parameters["PhaseDemolition"] = Name(e.DemolishedPhaseId);
            foreach (string p in CopiedParameters)
            {
                string value = ReadString(e, p) ?? (type != null ? ReadString(type, p) : null);
                if (value != null)
                {
                    q.Parameters[p] = value;
                }
            }
            return q;
        }

        private static string Mm(double feet) =>
            System.Math.Round(RevitUnits.ToMillimeters(feet)).ToString(CultureInfo.InvariantCulture);

        public static string CategoryName(Category category)
        {
            if (category == null)
            {
                return string.Empty;
            }
            long id = category.Id.Value;
            if (id < int.MinValue || id > int.MaxValue)
            {
                return "CAT_" + id.ToString(CultureInfo.InvariantCulture);
            }
            var bic = (BuiltInCategory)(int)id;
            return Enum.IsDefined(typeof(BuiltInCategory), bic) ? bic.ToString() : "CAT_" + id.ToString(CultureInfo.InvariantCulture);
        }

        public string Name(ElementId id)
        {
            if (id == null || id == ElementId.InvalidElementId)
            {
                return string.Empty;
            }
            if (!names.TryGetValue(id.Value, out string name))
            {
                name = doc.GetElement(id)?.Name ?? string.Empty;
                names[id.Value] = name;
            }
            return name;
        }

        private string LevelName(Element e)
        {
            if (e.LevelId != null && e.LevelId != ElementId.InvalidElementId)
            {
                return Name(e.LevelId);
            }
            // Poutres, poteaux, escaliers : le niveau est porte par un parametre.
            BuiltInParameter[] candidates =
            {
                BuiltInParameter.INSTANCE_REFERENCE_LEVEL_PARAM,
                BuiltInParameter.FAMILY_BASE_LEVEL_PARAM,
                BuiltInParameter.STAIRS_BASE_LEVEL_PARAM,
                BuiltInParameter.SCHEDULE_LEVEL_PARAM
            };
            foreach (BuiltInParameter bip in candidates)
            {
                Parameter p = e.get_Parameter(bip);
                if (p != null && p.StorageType == StorageType.ElementId)
                {
                    ElementId id = p.AsElementId();
                    if (id != null && id != ElementId.InvalidElementId)
                    {
                        return Name(id);
                    }
                }
            }
            return string.Empty;
        }

        private bool IsExcluded(Element e, ElementType type)
        {
            Parameter p = e.LookupParameter(ExcludeParameter) ?? type?.LookupParameter(ExcludeParameter);
            if (p == null || !p.HasValue)
            {
                return false;
            }
            switch (p.StorageType)
            {
                case StorageType.Integer:
                    return p.AsInteger() == 1; // parametre Oui/Non
                case StorageType.String:
                    string s = (p.AsString() ?? string.Empty).Trim();
                    return settings.ExcludeValues.Any(v => string.Equals(v, s, StringComparison.OrdinalIgnoreCase));
                default:
                    return false;
            }
        }

        private static string ReadString(Element e, string name)
        {
            Parameter p = e.LookupParameter(name);
            if (p == null || !p.HasValue)
            {
                return null;
            }
            return p.StorageType == StorageType.String ? p.AsString() : p.AsValueString();
        }

        /// <summary>Longueur lue dans le premier parametre renseigne de la liste (instance puis type), en mm.</summary>
        public double? ReadLengthMm(Element e, params BuiltInParameter[] candidates)
        {
            ElementType type = doc.GetElement(e.GetTypeId()) as ElementType;
            foreach (BuiltInParameter bip in candidates)
            {
                foreach (Element source in new[] { e, (Element)type })
                {
                    Parameter p = source?.get_Parameter(bip);
                    if (p != null && p.HasValue && p.StorageType == StorageType.Double)
                    {
                        double v = p.AsDouble();
                        if (v > 1e-9)
                        {
                            return RevitUnits.ToMillimeters(v);
                        }
                    }
                }
            }
            return null;
        }
    }
}
