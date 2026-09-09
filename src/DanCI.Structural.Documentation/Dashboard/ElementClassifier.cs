using System.Collections.Generic;

namespace DanCI.Structural.Documentation.Dashboard
{
    /// <summary>
    /// Categorie structurale d'un element, nommee comme dans Revit mais SANS en dependre.
    /// La couche Revit fait la traduction par le NOM de la categorie integree, ce qui
    /// evite d'inscrire des identifiants numeriques dans le moteur.
    /// </summary>
    public enum StructuralCategory
    {
        Unknown,
        StructuralColumns,
        StructuralFraming,
        Floors,
        Walls,
        StructuralFoundation,
        Stairs
    }

    /// <summary>
    /// Forme d'une fondation, que la categorie seule ne distingue pas : Revit range la
    /// semelle isolee, la semelle filante et le radier sous la meme categorie.
    /// </summary>
    public enum FoundationForm
    {
        /// <summary>Non determinee. Le classement refusera de trancher.</summary>
        Unknown,

        /// <summary>Semelle isolee : une instance de famille ponctuelle.</summary>
        Isolated,

        /// <summary>Semelle filante : une fondation de mur, portee par un voile.</summary>
        UnderWall,

        /// <summary>Radier ou dalle de fondation.</summary>
        Slab
    }

    /// <summary>Resultat d'un classement, avec sa raison quand il echoue.</summary>
    public sealed class Classification
    {
        public bool Succeeded { get; set; }
        public ElementKind Kind { get; set; }

        /// <summary>Pourquoi l'element n'a pas ete classe. Vide en cas de succes.</summary>
        public string Reason { get; set; }

        public static Classification As(ElementKind kind)
        {
            return new Classification { Succeeded = true, Kind = kind, Reason = string.Empty };
        }

        public static Classification No(string reason)
        {
            return new Classification { Succeeded = false, Reason = reason };
        }
    }

    /// <summary>
    /// A QUEL MODULE UN ELEMENT APPARTIENT-IL.
    ///
    /// C'est la seule question que le mode batch doit resoudre, et c'est la seule ou il
    /// peut se tromper gravement : calculer une longrine comme une poutre, ou une semelle
    /// filante comme une semelle isolee, produirait un resultat d'apparence normale et
    /// faux. C'est exactement ce que le moteur refuse de faire ailleurs.
    ///
    /// LE CLASSEUR NE DEVINE DONC RIEN. Il classe ce que la categorie designe sans
    /// ambiguite, et REFUSE le reste en disant pourquoi. Un element non classe n'est pas
    /// perdu : il est nomme dans la synthese, et l'ingenieur le lance a la main dans le
    /// module qui convient.
    ///
    /// DEUX AMBIGUITES CONNUES ET NON RESOLUES.
    ///
    /// - La LONGRINE est une ossature structurelle, exactement comme une poutre. Rien dans
    ///   la categorie ne les distingue, et le niveau ne suffit pas : une poutre de
    ///   soubassement n'est pas une longrine. Le batch les classe donc en poutres, ce qui
    ///   est le choix le moins pire — mais l'effort de liaison de l'EN 1998-5 ne sera pas
    ///   verifie. La synthese le rappelle.
    /// - Le PLANCHER INCLINE modelisant une paillasse d'escalier est un plancher. Le batch
    ///   le classe en dalle. Une dalle calculee a plat sur une portee inclinee sous-estime
    ///   le poids propre de plus de 40 % : la synthese demande donc de verifier qu'aucun
    ///   des planchers du lot n'est une paillasse.
    /// </summary>
    public static class ElementClassifier
    {
        public static Classification Classify(StructuralCategory category)
        {
            return Classify(category, FoundationForm.Unknown);
        }

        public static Classification Classify(StructuralCategory category,
                                              FoundationForm foundation)
        {
            switch (category)
            {
                case StructuralCategory.StructuralColumns:
                    return Classification.As(ElementKind.Column);

                case StructuralCategory.StructuralFraming:
                    return Classification.As(ElementKind.Beam);

                case StructuralCategory.Floors:
                    return Classification.As(ElementKind.Slab);

                case StructuralCategory.Walls:
                    return Classification.As(ElementKind.Wall);

                case StructuralCategory.Stairs:
                    return Classification.As(ElementKind.Stair);

                case StructuralCategory.StructuralFoundation:
                    return ClassifyFoundation(foundation);

                default:
                    return Classification.No(
                        "Categorie non structurale ou non reconnue : le batch ne devine pas "
                        + "a quel module elle appartient.");
            }
        }

        /// <summary>
        /// Revit range la semelle isolee, la semelle filante et le radier sous la meme
        /// categorie. Les confondre serait une faute de calcul, pas un detail de
        /// classement : une semelle filante ne poinconne pas, une semelle isolee si.
        /// </summary>
        private static Classification ClassifyFoundation(FoundationForm foundation)
        {
            switch (foundation)
            {
                case FoundationForm.Isolated:
                    return Classification.As(ElementKind.IsolatedFooting);

                case FoundationForm.UnderWall:
                    return Classification.As(ElementKind.StripFooting);

                case FoundationForm.Slab:
                    return Classification.No(
                        "Radier ou dalle de fondation : le moteur ne traite pas la "
                        + "fondation superficielle continue en deux directions. Aucun "
                        + "module ne convient, et l'approcher par une dalle ignorerait la "
                        + "reaction du sol.");

                default:
                    return Classification.No(
                        "Fondation dont la forme n'a pas pu etre determinee. Semelle "
                        + "isolee et semelle filante ne se calculent pas de la meme "
                        + "facon — l'une poinconne, l'autre non — et le batch ne "
                        + "choisit pas a votre place.");
            }
        }

        /// <summary>
        /// Les avertissements que toute execution en lot doit porter, quelle qu'en soit la
        /// composition. Ce ne sont pas des defauts : ce sont les deux endroits ou le
        /// classement automatique peut etre juste par categorie et faux par nature.
        /// </summary>
        public static IEnumerable<string> StandingWarnings
        {
            get
            {
                yield return
                    "Les LONGRINES sont classees en poutres : rien dans la categorie Revit "
                    + "ne les distingue. L'effort de liaison de l'EN 1998-5 art. 5.4.1.2(7) "
                    + "et le minimum de 0,4 % sur les deux nappes ne sont donc PAS "
                    + "verifies. Relancez-les dans le module Longrine.";
                yield return
                    "Un PLANCHER INCLINE modelisant une paillasse d'escalier est classe en "
                    + "dalle. Calcule a plat, son poids propre est sous-estime de plus de "
                    + "40 %. Verifiez qu'aucun plancher du lot n'est une paillasse.";
            }
        }
    }
}
