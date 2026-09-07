using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.ExtensibleStorage;

namespace DanCI.Structural.Revit.Storage
{
    /// <summary>
    /// Stocke la trace d'un calcul dans l'element Revit lui-meme, via l'Extensible Storage.
    /// Le modele devient ainsi auto-portant : on sait, pour chaque element, avec quelle
    /// version du moteur et quelles donnees son ferraillage a ete produit, et quelles
    /// armatures en sont issues. C'est le prerequis de la mise a jour du dimensionnement.
    /// </summary>
    public static class DesignDataStore
    {
        /// <summary>Version du schema de donnees. A incrementer si les champs changent.</summary>
        public const string SchemaVersion = "1";

        private static readonly Guid SchemaGuid =
            new Guid("6D6F1B1E-1C2A-4A2B-9E6C-6C5A1D0B7F31");

        private const string SchemaName = "DanCIStructuralDesign";
        private const string FieldModule = "Module";
        private const string FieldEngineVersion = "EngineVersion";
        private const string FieldCodeLabel = "CodeLabel";
        private const string FieldInputHash = "InputHash";
        private const string FieldSummary = "Summary";
        private const string FieldTimestamp = "TimestampUtc";
        private const string FieldRebarIds = "RebarIds";

        private static Schema GetOrCreateSchema()
        {
            Schema existing = Schema.Lookup(SchemaGuid);
            if (existing != null) return existing;

            var builder = new SchemaBuilder(SchemaGuid);
            builder.SetSchemaName(SchemaName);
            builder.SetDocumentation("DanCI Structural Studio - trace du dimensionnement.");
            builder.SetVendorId("DANCI");
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Vendor);

            builder.AddSimpleField(FieldModule, typeof(string));
            builder.AddSimpleField(FieldEngineVersion, typeof(string));
            builder.AddSimpleField(FieldCodeLabel, typeof(string));
            builder.AddSimpleField(FieldInputHash, typeof(string));
            builder.AddSimpleField(FieldSummary, typeof(string));
            builder.AddSimpleField(FieldTimestamp, typeof(string));
            builder.AddSimpleField(FieldRebarIds, typeof(string));

            return builder.Finish();
        }

        /// <summary>
        /// Lit la trace attachee a un element. Renvoie null si l'element n'a jamais ete
        /// dimensionne par le plugin, ou si la donnee est illisible.
        /// </summary>
        public static DesignRecord Read(Element element)
        {
            if (element == null) return null;
            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema == null) return null;

                Entity entity = element.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;

                var record = new DesignRecord
                {
                    Module = entity.Get<string>(FieldModule),
                    EngineVersion = entity.Get<string>(FieldEngineVersion),
                    CodeLabel = entity.Get<string>(FieldCodeLabel),
                    InputHash = entity.Get<string>(FieldInputHash),
                    Summary = entity.Get<string>(FieldSummary),
                    TimestampUtc = entity.Get<string>(FieldTimestamp)
                };

                string ids = entity.Get<string>(FieldRebarIds);
                if (!string.IsNullOrEmpty(ids))
                {
                    foreach (string part in ids.Split(','))
                    {
                        long value;
                        if (long.TryParse(part, out value)) record.RebarIds.Add(new ElementId(value));
                    }
                }
                return record;
            }
            catch (Exception)
            {
                // Une donnee illisible ne doit jamais empecher un nouveau calcul.
                return null;
            }
        }

        /// <summary>
        /// Ecrit la trace sur l'element. Doit etre appele dans une transaction ouverte.
        /// Renvoie false si l'ecriture echoue, sans jamais lever d'exception.
        /// </summary>
        public static bool Write(Element element, DesignRecord record)
        {
            if (element == null || record == null) return false;
            try
            {
                Schema schema = GetOrCreateSchema();
                var entity = new Entity(schema);
                entity.Set(FieldModule, record.Module ?? string.Empty);
                entity.Set(FieldEngineVersion, record.EngineVersion ?? string.Empty);
                entity.Set(FieldCodeLabel, record.CodeLabel ?? string.Empty);
                entity.Set(FieldInputHash, record.InputHash ?? string.Empty);
                entity.Set(FieldSummary, record.Summary ?? string.Empty);
                entity.Set(FieldTimestamp, record.TimestampUtc ?? string.Empty);
                entity.Set(FieldRebarIds,
                    string.Join(",", record.RebarIds.Select(id => id.Value.ToString())));

                element.SetEntity(entity);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>Supprime la trace d'un element.</summary>
        public static void Clear(Element element)
        {
            try
            {
                Schema schema = Schema.Lookup(SchemaGuid);
                if (schema != null) element.DeleteEntity(schema);
            }
            catch (Exception)
            {
                // Sans consequence : la trace sera simplement remplacee.
            }
        }

        /// <summary>
        /// Armatures precedemment creees par le plugin et encore presentes dans le modele.
        /// </summary>
        public static List<ElementId> ExistingReinforcement(Document document, DesignRecord record)
        {
            var alive = new List<ElementId>();
            if (record == null) return alive;

            foreach (ElementId id in record.RebarIds)
            {
                if (document.GetElement(id) != null) alive.Add(id);
            }
            return alive;
        }
    }
}
