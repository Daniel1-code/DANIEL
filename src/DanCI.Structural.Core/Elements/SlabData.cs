using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>Conditions d'appui d'une bande de dalle.</summary>
    public enum SlabSpanKind
    {
        /// <summary>Dalle isostatique sur deux appuis simples.</summary>
        SimplySupported,
        /// <summary>Travee de rive d'une dalle continue.</summary>
        EndSpan,
        /// <summary>Travee intermediaire d'une dalle continue.</summary>
        InteriorSpan,
        /// <summary>Console (balcon, debord).</summary>
        Cantilever
    }

    /// <summary>
    /// Donnees d'une dalle pleine portant dans un sens, independantes de Revit.
    /// Dimensions en millimetres.
    ///
    /// Le calcul se fait sur une **bande de 1 metre** : c'est la convention du metier,
    /// et elle rend les sections directement comparables aux nappes du chantier.
    /// </summary>
    public sealed class SlabData
    {
        /// <summary>Largeur de la bande de calcul (mm). Toujours 1 000 mm.</summary>
        public const double StripWidthMm = 1000.0;

        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Epaisseur de la dalle h (mm).</summary>
        public double ThicknessMm { get; set; }

        /// <summary>Portee de calcul l_eff dans le sens porteur (mm).</summary>
        public double SpanMm { get; set; }

        /// <summary>Dimension du panneau perpendiculairement a la portee (mm).</summary>
        public double WidthMm { get; set; }

        public SlabSpanKind SpanKind { get; set; }

        public List<string> Remarks { get; private set; }

        public SlabData()
        {
            Remarks = new List<string>();
            Name = "Dalle";
            Mark = "DA";
            ThicknessMm = 200.0;
            SpanKind = SlabSpanKind.SimplySupported;
        }

        /// <summary>Aire du panneau en plan (mm2).</summary>
        public double AreaMm2 { get { return SpanMm * WidthMm; } }

        /// <summary>Volume de beton du panneau (mm3).</summary>
        public double VolumeMm3 { get { return AreaMm2 * ThicknessMm; } }

        /// <summary>Elancement de la dalle, portee sur epaisseur.</summary>
        public double Slenderness
        {
            get { return ThicknessMm > 0 ? SpanMm / ThicknessMm : 0.0; }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format("h = {0:0} mm - portee {1:0} mm", ThicknessMm, SpanMm);
            }
        }

        /// <summary>
        /// Rapport de la dimension perpendiculaire sur la portee declaree.
        /// </summary>
        public double PanelAspectRatio
        {
            get { return SpanMm > 0 && WidthMm > 0 ? WidthMm / SpanMm : 0.0; }
        }

        /// <summary>
        /// La dalle porte-t-elle vraiment dans le sens declare, et dans ce seul sens ?
        ///
        /// Une dalle appuyee sur ses quatre cotes ne porte dans un sens que si le panneau
        /// est au moins deux fois plus long perpendiculairement a la portee. En deca, les
        /// deux directions se partagent la charge et le calcul en bande unique surestime
        /// le ferraillage dans un sens tout en l'oubliant dans l'autre.
        ///
        /// Un rapport inferieur a 1 signale en outre que la portee a probablement ete
        /// declaree dans le mauvais sens : une dalle porte par le plus court chemin.
        /// </summary>
        public bool IsGenuinelyOneWay
        {
            get
            {
                if (SpanKind == SlabSpanKind.Cantilever) return true;
                if (SpanMm <= 0 || WidthMm <= 0) return true;
                return PanelAspectRatio >= 2.0;
            }
        }

        /// <summary>La portee declaree est-elle la plus longue dimension du panneau ?</summary>
        public bool SpanIsTheLongDirection
        {
            get
            {
                return SpanKind != SlabSpanKind.Cantilever && SpanMm > 0 && WidthMm > 0
                       && PanelAspectRatio < 1.0;
            }
        }
    }
}
