using System;
using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>Forme de la section d'une poutre.</summary>
    public enum BeamSectionShape
    {
        Rectangular,
        TSection
    }

    /// <summary>Conditions d'appui, qui conditionnent la longueur de flexion nulle l0.</summary>
    public enum BeamSpanKind
    {
        /// <summary>Travee isostatique sur deux appuis simples.</summary>
        SimplySupported,
        /// <summary>Travee de rive d'une poutre continue.</summary>
        EndSpan,
        /// <summary>Travee intermediaire d'une poutre continue.</summary>
        InteriorSpan,
        /// <summary>Console.</summary>
        Cantilever
    }

    /// <summary>
    /// Donnees geometriques d'une poutre, independantes de Revit. Dimensions en millimetres.
    /// </summary>
    public sealed class BeamData
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        public BeamSectionShape Shape { get; set; }

        /// <summary>Largeur de l'ame b_w.</summary>
        public double WebWidthMm { get; set; }

        /// <summary>Hauteur totale h.</summary>
        public double HeightMm { get; set; }

        /// <summary>Largeur totale de la table, section en T. 0 si inconnue.</summary>
        public double FlangeWidthMm { get; set; }

        /// <summary>Epaisseur de la table h_f, section en T.</summary>
        public double FlangeThicknessMm { get; set; }

        /// <summary>Portee entre nus d'appuis.</summary>
        public double SpanMm { get; set; }

        public BeamSpanKind SpanKind { get; set; }

        public List<string> Remarks { get; private set; }

        public BeamData()
        {
            Remarks = new List<string>();
            Name = "Poutre";
            Mark = "BM";
            Shape = BeamSectionShape.Rectangular;
            SpanKind = BeamSpanKind.SimplySupported;
        }

        /// <summary>Aire brute de beton de la section (mm2).</summary>
        public double GrossAreaMm2
        {
            get
            {
                if (Shape == BeamSectionShape.TSection && FlangeWidthMm > WebWidthMm)
                {
                    return WebWidthMm * HeightMm
                           + (FlangeWidthMm - WebWidthMm) * FlangeThicknessMm;
                }
                return WebWidthMm * HeightMm;
            }
        }

        public string SectionLabel
        {
            get
            {
                if (Shape == BeamSectionShape.TSection)
                {
                    return string.Format("T {0:0} x {1:0} (table {2:0} x {3:0})",
                        WebWidthMm, HeightMm, FlangeWidthMm, FlangeThicknessMm);
                }
                return string.Format("{0:0} x {1:0}", WebWidthMm, HeightMm);
            }
        }

        /// <summary>
        /// Hauteur utile d, distance du parement comprime au centre de gravite des aciers
        /// tendus, pour un enrobage, un diametre de cadre et un diametre de barre donnes.
        /// </summary>
        public double EffectiveDepthMm(double coverMm, double stirrupDiameterMm,
                                       double barDiameterMm, int layers = 1)
        {
            double toFirstLayer = coverMm + stirrupDiameterMm + barDiameterMm / 2.0;
            if (layers <= 1) return HeightMm - toFirstLayer;

            // Lits superposes : espacement vertical libre pris egal au diametre des barres.
            double layerPitch = 2.0 * barDiameterMm;
            double centroidShift = layerPitch * (layers - 1) / 2.0;
            return HeightMm - toFirstLayer - centroidShift;
        }
    }
}
