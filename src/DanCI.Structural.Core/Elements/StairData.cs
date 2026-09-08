using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>
    /// Comment la volee porte. C'est la question qui change tout dans un escalier :
    /// la meme volee, appuyee autrement, n'a ni la meme portee ni le meme ferraillage.
    /// </summary>
    public enum StairSpanKind
    {
        /// <summary>
        /// La volee porte longitudinalement, et le palier participe a la portee. Cas le
        /// plus courant : les appuis sont au pied de la volee et au bord oppose du palier.
        /// </summary>
        AlongFlightWithLanding,

        /// <summary>
        /// La volee porte longitudinalement entre deux appuis situes a ses extremites
        /// (poutres ou voiles de palier). La portee est la projection horizontale de la
        /// volee seule ; les paliers sont portes independamment.
        /// </summary>
        AlongFlightOnly,

        /// <summary>
        /// La volee porte transversalement entre deux limons ou deux voiles. La portee est
        /// la largeur de la volee, et les armatures principales sont perpendiculaires a la
        /// montee.
        /// </summary>
        TransverseBetweenWalls
    }

    /// <summary>
    /// Forme de la volee en plan. Le module ne sait calculer que la volee DROITE ; les
    /// autres formes ne sont pas des variantes, ce sont d'autres problemes.
    ///
    /// Une volee balancee ou helicoidale porte en flexion ET en torsion, et sa portee n'est
    /// pas la projection d'une droite. La calculer comme une volee droite de memes
    /// contremarches donnerait un resultat d'apparence normale et faux. Le moteur refuse.
    /// </summary>
    public enum StairFlightShape
    {
        /// <summary>Volee droite : marches paralleles, ligne de foulee rectiligne.</summary>
        Straight,

        /// <summary>Volee balancee : marches non paralleles, ligne de foulee brisee.</summary>
        Winder,

        /// <summary>Volee helicoidale ou courbe.</summary>
        Spiral,

        /// <summary>
        /// La forme n'a pas pu etre determinee. Ce n'est pas la meme chose que droite : le
        /// moteur poursuit, mais il le dit et n'endosse pas l'hypothese.
        /// </summary>
        Undetermined
    }

    /// <summary>
    /// Une dimension d'escalier, pour pouvoir dire D'OU ELLE VIENT.
    /// </summary>
    public enum StairDimension
    {
        RiserCount,
        RiserHeight,
        TreadDepth,
        Width,
        WaistThickness,
        LandingSpan,
        LandingThickness,
        Support
    }

    /// <summary>
    /// D'ou vient une dimension. La distinction n'est pas documentaire : une valeur LUE
    /// engage le modele, une valeur SUPPOSEE n'engage personne, et confondre les deux est
    /// exactement ce qui fait poser des armatures dans le vide.
    /// </summary>
    public enum StairDimensionSource
    {
        /// <summary>
        /// Hypothese. Ni lue sur l'element dessine, ni confirmee par l'ingenieur : c'est
        /// une valeur par defaut qui a survecu, et elle doit etre annoncee comme telle.
        /// </summary>
        Assumed,

        /// <summary>
        /// Lue sur l'element dessine. Elle fait foi : si l'escalier existe dans le modele,
        /// c'est lui qui a raison, pas le formulaire.
        /// </summary>
        ReadFromModel,

        /// <summary>
        /// Declaree par l'ingenieur, en connaissance de cause. Elle fait foi aussi, mais
        /// c'est une personne qui la porte, pas le modele.
        /// </summary>
        StatedByEngineer
    }

    /// <summary>
    /// Ce qui a ete LU sur l'escalier dessine, et ce qui reste suppose.
    ///
    /// LA REGLE. Quand l'escalier est deja dessine, c'est le dessin qui decide. Le
    /// formulaire ne sert qu'a ce que le dessin ne porte pas — et il doit alors le DIRE,
    /// parce qu'une hypothese silencieuse ressemble trait pour trait a une lecture.
    ///
    /// La version 3.12.0 et anterieures supposaient un palier de 1 300 mm sur TOUT
    /// escalier, y compris ceux qui n'en ont aucun. La portee etait donc majoree de
    /// 1 300 mm, et le ferraillage du palier etait pose la ou il n'y a pas de beton.
    /// C'est ce que cette classe rend impossible : une valeur non lue est marquee.
    /// </summary>
    public sealed class StairGeometryProvenance
    {
        private readonly Dictionary<StairDimension, StairDimensionSource> _sources
            = new Dictionary<StairDimension, StairDimensionSource>();

        public StairDimensionSource Of(StairDimension dimension)
        {
            StairDimensionSource source;
            return _sources.TryGetValue(dimension, out source)
                ? source : StairDimensionSource.Assumed;
        }

        public void Set(StairDimension dimension, StairDimensionSource source)
        {
            _sources[dimension] = source;
        }

        /// <summary>La dimension vient-elle d'ailleurs que d'une valeur par defaut ?</summary>
        public bool IsEstablished(StairDimension dimension)
        {
            return Of(dimension) != StairDimensionSource.Assumed;
        }

        /// <summary>Nom lisible d'une dimension, pour la note et la fenetre.</summary>
        public static string Label(StairDimension dimension)
        {
            switch (dimension)
            {
                case StairDimension.RiserCount: return "nombre de contremarches";
                case StairDimension.RiserHeight: return "hauteur de contremarche";
                case StairDimension.TreadDepth: return "giron";
                case StairDimension.Width: return "largeur de volee";
                case StairDimension.WaistThickness: return "epaisseur de paillasse";
                case StairDimension.LandingSpan: return "longueur de palier portante";
                case StairDimension.LandingThickness: return "epaisseur de palier";
                default: return "mode d'appui";
            }
        }

        /// <summary>
        /// Les dimensions encore SUPPOSEES, dans l'ordre ou elles comptent. Le mode
        /// d'appui vient en tete : c'est lui qui fixe la portee, donc le moment, donc
        /// tout le reste.
        /// </summary>
        public IEnumerable<StairDimension> Assumptions()
        {
            var order = new[]
            {
                StairDimension.Support, StairDimension.LandingSpan,
                StairDimension.WaistThickness, StairDimension.RiserCount,
                StairDimension.TreadDepth, StairDimension.RiserHeight,
                StairDimension.Width, StairDimension.LandingThickness
            };
            foreach (StairDimension dimension in order)
            {
                if (!IsEstablished(dimension)) yield return dimension;
            }
        }

        public StairGeometryProvenance Clone()
        {
            var copy = new StairGeometryProvenance();
            foreach (var pair in _sources) copy._sources[pair.Key] = pair.Value;
            return copy;
        }
    }

    /// <summary>
    /// Donnees d'une volee d'escalier droit en beton arme, independantes de Revit.
    /// Dimensions en millimetres.
    ///
    /// Le calcul se fait sur une **bande de 1 metre** de largeur, comme pour une dalle :
    /// une volee est une dalle inclinee qui porte des marches.
    /// </summary>
    public sealed class StairData
    {
        /// <summary>Largeur de la bande de calcul (mm). Toujours 1 000 mm.</summary>
        public const double StripWidthMm = 1000.0;

        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Hauteur de contremarche R (mm).</summary>
        public double RiserHeightMm { get; set; }

        /// <summary>Giron G, profondeur d'une marche en projection horizontale (mm).</summary>
        public double TreadDepthMm { get; set; }

        /// <summary>Nombre de contremarches de la volee.</summary>
        public int RiserCount { get; set; }

        /// <summary>
        /// Epaisseur de la paillasse t (mm), mesuree **perpendiculairement a la pente**.
        /// C'est la definition qui compte : mesuree verticalement, elle vaut t / cos alpha
        /// et le poids propre serait faux.
        /// </summary>
        public double WaistThicknessMm { get; set; }

        /// <summary>Largeur de la volee (mm).</summary>
        public double WidthMm { get; set; }

        /// <summary>Epaisseur du palier (mm).</summary>
        public double LandingThicknessMm { get; set; }

        /// <summary>
        /// Longueur de palier comprise dans la portee, mesuree du nu de la volee a l'appui
        /// (mm). Nulle si les paliers sont portes independamment.
        /// </summary>
        public double LandingSpanMm { get; set; }

        public StairSpanKind SpanKind { get; set; }

        /// <summary>Forme de la volee en plan, telle que lue ou declaree.</summary>
        public StairFlightShape Shape { get; set; }

        /// <summary>
        /// D'ou vient chaque dimension. Jamais nul : par defaut, TOUT est suppose, ce qui
        /// est la verite avant qu'on ait lu quoi que ce soit.
        /// </summary>
        public StairGeometryProvenance Provenance { get; set; }

        public List<string> Remarks { get; private set; }

        public StairData()
        {
            Remarks = new List<string>();
            Name = "Escalier";
            Mark = "EC";
            RiserHeightMm = 170.0;
            TreadDepthMm = 280.0;
            RiserCount = 9;
            WaistThicknessMm = 150.0;
            WidthMm = 1200.0;
            LandingThicknessMm = 150.0;
            LandingSpanMm = 1300.0;
            SpanKind = StairSpanKind.AlongFlightWithLanding;
            Shape = StairFlightShape.Straight;
            Provenance = new StairGeometryProvenance();
        }

        /// <summary>Denivele total de la volee (mm) : n contremarches.</summary>
        public double TotalRiseMm { get { return RiserCount * RiserHeightMm; } }

        /// <summary>
        /// Projection horizontale de la volee (mm).
        ///
        /// Une volee de n contremarches ne compte que **n - 1 girons** : la derniere
        /// contremarche debouche sur le palier, dont le nez de marche est le bord. Compter
        /// n girons allonge la volee d'une marche entiere et fausse a la fois la portee et
        /// la pente.
        /// </summary>
        public double TotalGoingMm
        {
            get { return RiserCount > 1 ? (RiserCount - 1) * TreadDepthMm : 0.0; }
        }

        /// <summary>Tangente de l'angle de la paillasse sur l'horizontale.</summary>
        public double SlopeTangent
        {
            get { return TreadDepthMm > 0 ? RiserHeightMm / TreadDepthMm : 0.0; }
        }

        /// <summary>Angle de la paillasse sur l'horizontale (degres).</summary>
        public double SlopeAngleDegrees
        {
            get { return Math.Atan(SlopeTangent) * 180.0 / Math.PI; }
        }

        /// <summary>
        /// cos alpha, calcule sur la geometrie de la marche : G / racine(G2 + R2).
        /// C'est par lui que passe tout le poids propre d'une volee.
        /// </summary>
        public double SlopeCosine
        {
            get
            {
                if (TreadDepthMm <= 0) return 1.0;
                double hyp = Math.Sqrt(TreadDepthMm * TreadDepthMm
                                       + RiserHeightMm * RiserHeightMm);
                return hyp > 0 ? TreadDepthMm / hyp : 1.0;
            }
        }

        /// <summary>Portee de calcul l_eff (mm), selon le mode d'appui declare.</summary>
        public double SpanMm
        {
            get
            {
                switch (SpanKind)
                {
                    case StairSpanKind.TransverseBetweenWalls:
                        return WidthMm;
                    case StairSpanKind.AlongFlightOnly:
                        return TotalGoingMm;
                    default:
                        return TotalGoingMm + LandingSpanMm;
                }
            }
        }

        /// <summary>
        /// Formule de Blondel, 2R + G. Regle d'ERGONOMIE, pas de resistance : entre 600 et
        /// 650 mm l'escalier se monte naturellement. Elle ne releve d'aucun Eurocode.
        /// </summary>
        public double BlondelValueMm { get { return 2.0 * RiserHeightMm + TreadDepthMm; } }

        /// <summary>
        /// La geometrie permet-elle un calcul ? Une volee de moins de deux contremarches
        /// n'a aucun giron, donc aucune portee ; sans giron ni epaisseur, il n'y a rien a
        /// dimensionner. Le moteur refuse plutot que de produire un ferraillage arbitraire.
        /// </summary>
        public bool IsCalculable
        {
            get
            {
                return RiserCount >= 2 && RiserHeightMm > 0 && TreadDepthMm > 0
                       && WaistThicknessMm > 0 && WidthMm > 0;
            }
        }

        /// <summary>Volume de beton de la volee, paillasse et marches (mm3).</summary>
        public double FlightVolumeMm3
        {
            get
            {
                double waist = TotalGoingMm / SlopeCosine * WaistThicknessMm * WidthMm;
                double steps = (RiserCount - 1) * (RiserHeightMm * TreadDepthMm / 2.0) * WidthMm;
                return waist + Math.Max(steps, 0.0);
            }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format(
                    "{0} CM de {1:0} mm, giron {2:0} mm, paillasse {3:0} mm - pente {4:0.0} deg",
                    RiserCount, RiserHeightMm, TreadDepthMm, WaistThicknessMm,
                    SlopeAngleDegrees);
            }
        }
    }
}
