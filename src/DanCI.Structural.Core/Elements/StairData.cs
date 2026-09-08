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
