using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;
using DanCI.Structural.Documentation.Dashboard;

namespace DanCI.Structural.Revit.Selection
{
    /// <summary>Un element du modele et le module auquel il revient.</summary>
    public sealed class BatchCandidate
    {
        public Element Element { get; set; }
        public ElementKind Kind { get; set; }
    }

    /// <summary>Un element ecarte, et pourquoi.</summary>
    public sealed class BatchRefusal
    {
        public string Name { get; set; }
        public string Reason { get; set; }
    }

    /// <summary>Ce qu'une passe de collecte a trouve.</summary>
    public sealed class BatchScope
    {
        public IList<BatchCandidate> Candidates { get; set; }
        public IList<BatchRefusal> Refused { get; set; }

        public BatchScope()
        {
            Candidates = new List<BatchCandidate>();
            Refused = new List<BatchRefusal>();
        }
    }

    /// <summary>
    /// Parcourt un modele — ou une vue — et range chaque element structurel dans le module
    /// qui lui revient.
    ///
    /// LA DECISION EST PRISE HORS DE REVIT. Ce collecteur ne fait que traduire : il lit la
    /// categorie et la forme de fondation, et laisse <see cref="ElementClassifier"/>
    /// trancher. C'est voulu : la regle de classement est ce qui peut se tromper
    /// gravement, et elle doit donc etre testable sans Revit.
    ///
    /// LA PORTEE PAR DEFAUT EST LA VUE ACTIVE, pas le modele entier. Sur un projet reel,
    /// « tout le modele » comprend les etages qu'on ne regarde pas, les variantes et les
    /// elements de reference : un lot qu'on n'a pas choisi n'est pas un lot qu'on peut
    /// verifier.
    /// </summary>
    public static class BatchCollector
    {
        /// <summary>Elements visibles dans une vue donnee.</summary>
        public static BatchScope CollectFromView(Document document, View view)
        {
            if (document == null || view == null) return new BatchScope();
            return Collect(new FilteredElementCollector(document, view.Id));
        }

        /// <summary>Tout le modele. A n'employer qu'en connaissance de cause.</summary>
        public static BatchScope CollectFromModel(Document document)
        {
            if (document == null) return new BatchScope();
            return Collect(new FilteredElementCollector(document));
        }

        private static BatchScope Collect(FilteredElementCollector collector)
        {
            var scope = new BatchScope();

            foreach (Element element in collector.WhereElementIsNotElementType())
            {
                if (element == null || element.Category == null) continue;

                StructuralCategory category = CategoryOf(element);
                if (category == StructuralCategory.Unknown) continue;

                Classification classification =
                    ElementClassifier.Classify(category, FoundationFormOf(element));

                if (classification.Succeeded)
                {
                    scope.Candidates.Add(new BatchCandidate
                    {
                        Element = element,
                        Kind = classification.Kind
                    });
                }
                else
                {
                    scope.Refused.Add(new BatchRefusal
                    {
                        Name = Describe(element),
                        Reason = classification.Reason
                    });
                }
            }

            return scope;
        }

        /// <summary>
        /// Traduit la categorie Revit en categorie structurale. Les elements NON
        /// structurels sont ignores silencieusement : un poteau architectural ou un mur de
        /// cloison n'a rien a faire dans un lot de calcul, et le signaler noierait les
        /// refus qui, eux, comptent.
        /// </summary>
        public static StructuralCategory CategoryOf(Element element)
        {
            if (element == null || element.Category == null) return StructuralCategory.Unknown;

            long id = element.Category.Id.Value;
            if (id == (long)BuiltInCategory.OST_StructuralColumns)
                return StructuralCategory.StructuralColumns;
            if (id == (long)BuiltInCategory.OST_StructuralFraming)
                return StructuralCategory.StructuralFraming;
            if (id == (long)BuiltInCategory.OST_Floors)
                return StructuralCategory.Floors;
            if (id == (long)BuiltInCategory.OST_Walls)
                return StructuralCategory.Walls;
            if (id == (long)BuiltInCategory.OST_StructuralFoundation)
                return StructuralCategory.StructuralFoundation;
            if (id == (long)BuiltInCategory.OST_Stairs)
                return StructuralCategory.Stairs;

            return StructuralCategory.Unknown;
        }

        /// <summary>
        /// Distingue les trois fondations que Revit range ensemble. La distinction se lit
        /// sur le TYPE .NET de l'element, qui est ce que Revit expose de plus sur : une
        /// fondation de mur est une WallFoundation, un radier un Floor, une semelle isolee
        /// une instance de famille.
        /// </summary>
        public static FoundationForm FoundationFormOf(Element element)
        {
            if (element == null) return FoundationForm.Unknown;
            if (CategoryOf(element) != StructuralCategory.StructuralFoundation)
            {
                return FoundationForm.Unknown;
            }

            if (element is WallFoundation) return FoundationForm.UnderWall;
            if (element is Floor) return FoundationForm.Slab;
            if (element is FamilyInstance) return FoundationForm.Isolated;
            return FoundationForm.Unknown;
        }

        private static string Describe(Element element)
        {
            try
            {
                return string.Format("{0} [{1}]", element.Name, element.Id.Value);
            }
            catch (Exception)
            {
                return "Element";
            }
        }
    }
}
