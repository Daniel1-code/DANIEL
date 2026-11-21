using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Autodesk.Revit.ApplicationServices;
using Autodesk.Revit.DB;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.Addin.Analysis
{
    /// <summary>
    /// Ecrit DanCI_BOQ_Code et DanCI_BOQ_Lot sur les elements metres. SEULE operation de
    /// l'extension qui modifie le modele : un groupe de transactions nomme, assimile en UNE
    /// annulation (Ctrl+Z), et lance uniquement a la demande explicite de l'utilisateur.
    ///
    /// Parametres partages a GUID FIXES (memes parametres dans tous les projets DanCI) :
    ///  - DanCI_BOQ_Code, DanCI_BOQ_Lot (texte, instance) : ecrits ici ;
    ///  - DanCI_BOQ_Exclure (Oui/Non, instance) : a cocher par l'utilisateur pour exclure un element ;
    ///  - DanCI_BOQ_Revision (texte, Informations sur le projet) : indice de revision du devis.
    /// Le fichier de parametres partages de l'utilisateur est restaure apres l'operation.
    /// </summary>
    internal static class ModelParameterWriter
    {
        public const string GroupName = "DanCI BOQ";

        private sealed class ParamDef
        {
            public string Name;
            public Guid Guid;
            public ForgeTypeId Spec;
            public string Description;
            public bool ProjectInfo;
        }

        private static readonly ParamDef Code = new ParamDef
        {
            Name = "DanCI_BOQ_Code", Guid = new Guid("cc3d0b30-b46b-47b9-80c8-41bb646cf62a"), Spec = SpecTypeId.String.Text,
            Description = "Code(s) des articles du BOQ DanCI auxquels l'élément contribue (écrit par l'extension)."
        };

        private static readonly ParamDef Lot = new ParamDef
        {
            Name = "DanCI_BOQ_Lot", Guid = new Guid("a7338b6b-11a8-48ef-9046-b2c346da5d33"), Spec = SpecTypeId.String.Text,
            Description = "Lot(s) du BOQ DanCI de l'élément (écrit par l'extension)."
        };

        private static readonly ParamDef Exclude = new ParamDef
        {
            Name = "DanCI_BOQ_Exclure", Guid = new Guid("e88fc127-a3db-4bb0-8ac3-d6ffe9fc3aa2"), Spec = SpecTypeId.Boolean.YesNo,
            Description = "Cocher pour exclure l'élément du métré DanCI."
        };

        private static readonly ParamDef Revision = new ParamDef
        {
            Name = "DanCI_BOQ_Revision", Guid = new Guid("743b4982-19ea-4079-a89a-9b07fdd65818"), Spec = SpecTypeId.String.Text,
            Description = "Indice de révision du BOQ DanCI (page de garde).", ProjectInfo = true
        };

        /// <summary>Categories auxquelles DanCI_BOQ_Exclure est toujours lie.</summary>
        private static readonly BuiltInCategory[] ModelCategories =
        {
            BuiltInCategory.OST_StructuralFoundation, BuiltInCategory.OST_StructuralColumns, BuiltInCategory.OST_StructuralFraming,
            BuiltInCategory.OST_Floors, BuiltInCategory.OST_Walls, BuiltInCategory.OST_Stairs, BuiltInCategory.OST_StairsRuns,
            BuiltInCategory.OST_StairsLandings, BuiltInCategory.OST_Roofs, BuiltInCategory.OST_Doors, BuiltInCategory.OST_Windows,
            BuiltInCategory.OST_Rebar, BuiltInCategory.OST_GenericModel, BuiltInCategory.OST_Parts
        };

        public sealed class WriteResult
        {
            public int Written;
            public int Unchanged;
            public int SkippedLinked;
            public int SkippedOwnedByOthers;
            public int Failed;
            public string Error;
        }

        /// <summary>Elements du modele hote a ecrire : ElementId -&gt; (codes, lots).</summary>
        public static Dictionary<long, (SortedSet<string> Codes, SortedSet<string> Lots)> Plan(BoqResult boq, out int linked)
        {
            var plan = new Dictionary<long, (SortedSet<string>, SortedSet<string>)>();
            linked = 0;
            foreach (BoqLotResult lot in boq.Lots)
            {
                foreach (BoqLine line in lot.Lines)
                {
                    foreach (LineContribution c in line.Contributions)
                    {
                        if (ElementRef.Of(c.Element).IsLinked)
                        {
                            linked++;
                            continue;
                        }
                        if (!plan.TryGetValue(c.Element.ElementId, out var entry))
                        {
                            entry = (new SortedSet<string>(StringComparer.Ordinal), new SortedSet<string>(StringComparer.Ordinal));
                            plan[c.Element.ElementId] = entry;
                        }
                        entry.Item1.Add(line.Code);
                        entry.Item2.Add(lot.Code);
                    }
                }
            }
            return plan;
        }

        public static WriteResult Write(Application app, Document doc, BoqResult boq)
        {
            var result = new WriteResult();
            var plan = Plan(boq, out result.SkippedLinked);

            string conflict = NameConflict(doc);
            if (conflict != null)
            {
                result.Error = conflict;
                return result;
            }

            var elements = plan.Keys.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).ToList();
            var categories = new HashSet<long>(elements.Where(e => e.Category != null).Select(e => e.Category.Id.Value));

            using (var group = new TransactionGroup(doc, "DanCI BOQ — écriture des codes BOQ"))
            {
                group.Start();

                using (var t = new Transaction(doc, "DanCI BOQ — paramètres partagés"))
                {
                    t.Start();
                    EnsureParameters(app, doc, categories);
                    t.Commit();
                }

                using (var t = new Transaction(doc, "DanCI BOQ — codes et lots"))
                {
                    t.Start();
                    foreach (Element e in elements)
                    {
                        if (doc.IsWorkshared && WorksharingUtils.GetCheckoutStatus(doc, e.Id) == CheckoutStatus.OwnedByOtherUser)
                        {
                            result.SkippedOwnedByOthers++;
                            continue;
                        }
                        var entry = plan[e.Id.Value];
                        string codes = string.Join("; ", entry.Codes);
                        string lots = string.Join("; ", entry.Lots);
                        Parameter pc = e.get_Parameter(Code.Guid);
                        Parameter pl = e.get_Parameter(Lot.Guid);
                        if (pc == null || pl == null || pc.IsReadOnly || pl.IsReadOnly)
                        {
                            result.Failed++;
                            continue;
                        }
                        if (pc.AsString() == codes && pl.AsString() == lots)
                        {
                            result.Unchanged++;
                            continue;
                        }
                        if (pc.Set(codes) && pl.Set(lots))
                        {
                            result.Written++;
                        }
                        else
                        {
                            result.Failed++;
                        }
                    }
                    t.Commit();
                }

                group.Assimilate();
            }
            BoqLog.Info("Codes BOQ écrits : " + result.Written + " élément(s), " + result.Unchanged + " inchangé(s), " +
                        result.Failed + " échec(s), " + result.SkippedOwnedByOthers + " emprunté(s) par d'autres, " +
                        result.SkippedLinked + " contribution(s) de liens non modifiables.");
            return result;
        }

        /// <summary>Un parametre de projet NON partage du meme nom empecherait la liaison : refus explicite.</summary>
        private static string NameConflict(Document doc)
        {
            var ours = new[] { Code, Lot, Exclude, Revision };
            DefinitionBindingMapIterator it = doc.ParameterBindings.ForwardIterator();
            while (it.MoveNext())
            {
                Definition d = it.Key;
                ParamDef match = ours.FirstOrDefault(o => o.Name == d.Name);
                if (match == null)
                {
                    continue;
                }
                bool sameShared = d is InternalDefinition internalDef &&
                                  doc.GetElement(internalDef.Id) is SharedParameterElement spe && spe.GuidValue == match.Guid;
                if (!sameShared)
                {
                    return "Un paramètre de projet « " + d.Name + " » existe déjà mais n'est pas le paramètre partagé DanCI " +
                           "(GUID différent). Supprimez-le ou renommez-le (Gérer → Paramètres du projet), puis recommencez. Rien n'a été modifié.";
                }
            }
            return null;
        }

        private static void EnsureParameters(Application app, Document doc, HashSet<long> usedCategories)
        {
            string original = app.SharedParametersFilename;
            string ours = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DanCI", "BOQ", "DanCI_BOQ_parametres_partages.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(ours));
            if (!File.Exists(ours))
            {
                File.WriteAllText(ours, string.Empty);
            }
            try
            {
                app.SharedParametersFilename = ours;
                DefinitionFile file = app.OpenSharedParameterFile();
                if (file == null)
                {
                    throw new InvalidOperationException("Fichier de paramètres partagés DanCI illisible : " + ours);
                }
                DefinitionGroup group = file.Groups.get_Item(GroupName) ?? file.Groups.Create(GroupName);

                var model = new HashSet<long>(usedCategories);
                foreach (BuiltInCategory bic in ModelCategories)
                {
                    model.Add((long)bic);
                }

                Bind(app, doc, group, Code, usedCategories);
                Bind(app, doc, group, Lot, usedCategories);
                Bind(app, doc, group, Exclude, model);
                Bind(app, doc, group, Revision, new HashSet<long> { (long)BuiltInCategory.OST_ProjectInformation });
            }
            finally
            {
                try
                {
                    app.SharedParametersFilename = original ?? string.Empty;
                }
                catch (Exception ex)
                {
                    BoqLog.Warn("Fichier de paramètres partagés de l'utilisateur non restauré : " + ex.Message);
                }
            }
        }

        private static void Bind(Application app, Document doc, DefinitionGroup group, ParamDef p, HashSet<long> categoryIds)
        {
            Definition def = group.Definitions.get_Item(p.Name) ?? group.Definitions.Create(
                new ExternalDefinitionCreationOptions(p.Name, p.Spec) { GUID = p.Guid, Description = p.Description, UserModifiable = true });
            if (def is ExternalDefinition ext && ext.GUID != p.Guid)
            {
                throw new InvalidOperationException("Le fichier de paramètres partagés DanCI contient « " + p.Name +
                                                    " » avec un autre GUID : supprimez " + "%AppData%\\DanCI\\BOQ\\DanCI_BOQ_parametres_partages.txt et recommencez.");
            }

            CategorySet set = app.Create.NewCategorySet();
            var existing = (SharedParameterElement.Lookup(doc, p.Guid) != null)
                ? doc.ParameterBindings.get_Item(def) as InstanceBinding
                : null;
            if (existing?.Categories != null)
            {
                foreach (Category c in existing.Categories)
                {
                    set.Insert(c);
                }
            }
            foreach (long id in categoryIds)
            {
                Category c = Category.GetCategory(doc, new ElementId(id));
                if (c != null && c.AllowsBoundParameters && !set.Contains(c))
                {
                    set.Insert(c);
                }
            }
            InstanceBinding binding = app.Create.NewInstanceBinding(set);
            if (existing == null)
            {
                doc.ParameterBindings.Insert(def, binding, GroupTypeId.Data);
            }
            else
            {
                doc.ParameterBindings.ReInsert(def, binding, GroupTypeId.Data);
            }
        }
    }
}
