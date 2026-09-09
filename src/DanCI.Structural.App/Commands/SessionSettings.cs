using DanCI.Structural.Documentation.Dashboard;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Engine.Column;
using DanCI.Structural.Engine.IsolatedFooting;
using DanCI.Structural.Engine.Slab;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Engine.StripFooting;
using DanCI.Structural.Engine.Wall;

namespace DanCI.Structural.App.Commands
{
    /// <summary>
    /// LES REGLAGES QUE L'INGENIEUR A REELLEMENT POSES, cette session, famille par famille.
    ///
    /// C'est la piece qui rend le mode batch defendable. Un lot ne se calcule pas avec des
    /// charges par defaut : la charge d'exploitation d'un plancher, la classe d'exposition,
    /// la contrainte admissible du sol ne se devinent pas, et les inventer produirait un
    /// ferraillage d'apparence normale et faux sur tout un projet d'un seul coup.
    ///
    /// Le mode batch ne traite donc QUE les familles dont la fenetre a ete ouverte et
    /// validee au moins une fois. Les autres sont nommees et laissees de cote. C'est la
    /// meme regle que partout ailleurs dans le moteur : quand une donnee manque, on le dit,
    /// on n'invente pas.
    ///
    /// Rien n'est persiste entre deux sessions Revit : des reglages vieux d'une semaine
    /// ressembleraient a des reglages voulus.
    /// </summary>
    public static class SessionSettings
    {
        public static ColumnDesignSettings Column { get; set; }
        public static BeamDesignSettings Beam { get; set; }
        public static SlabDesignSettings Slab { get; set; }
        public static WallDesignSettings Wall { get; set; }
        public static FootingDesignSettings IsolatedFooting { get; set; }
        public static StripFootingDesignSettings StripFooting { get; set; }
        public static StairDesignSettings Stair { get; set; }

        /// <summary>La famille a-t-elle des reglages poses ?</summary>
        public static bool IsSet(ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Column: return Column != null;
                case ElementKind.Beam: return Beam != null;
                case ElementKind.Slab: return Slab != null;
                case ElementKind.Wall: return Wall != null;
                case ElementKind.IsolatedFooting: return IsolatedFooting != null;
                case ElementKind.StripFooting: return StripFooting != null;
                case ElementKind.Stair: return Stair != null;
                default: return false;
            }
        }

        /// <summary>
        /// Pourquoi une famille est laissee de cote. Le message nomme la fenetre a ouvrir :
        /// « famille ignoree » sans mode d'emploi n'aide personne.
        /// </summary>
        public static string MissingReason(ElementKind kind)
        {
            if (kind == ElementKind.GradeBeam)
            {
                return "Les longrines ne sont pas classables automatiquement : rien dans la "
                       + "categorie Revit ne les distingue d'une poutre. Lancez-les depuis "
                       + "le module Longrine.";
            }

            return string.Format(
                "Aucun reglage pose pour les {0} cette session. Le batch ne calcule pas avec "
                + "des charges par defaut : ouvrez une fois la fenetre du module, validez "
                + "les hypotheses, puis relancez le lot.",
                DesignedElement.Label(kind).ToLowerInvariant());
        }
    }
}
