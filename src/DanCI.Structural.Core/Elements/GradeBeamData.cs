using System.Collections.Generic;

namespace DanCI.Structural.Core.Elements
{
    /// <summary>Conditions d'appui d'une longrine entre fondations.</summary>
    public enum GradeBeamSpanKind
    {
        /// <summary>Travee isostatique entre deux semelles.</summary>
        SimplySupported,
        /// <summary>Travee de rive d'une file continue.</summary>
        EndSpan,
        /// <summary>Travee intermediaire d'une file continue.</summary>
        InteriorSpan
    }

    /// <summary>
    /// Ce sur quoi la longrine repose entre ses appuis. Le choix change tout : une
    /// longrine suspendue porte l'integralite de sa charge en flexion, une longrine
    /// posee sur le sol n'en porte presque rien.
    /// </summary>
    public enum GradeBeamBedding
    {
        /// <summary>
        /// Suspendue entre appuis : remblai compressible, vide sanitaire, ou forme
        /// perdue sur sol gonflant. Elle porte toute sa charge.
        /// </summary>
        Suspended,

        /// <summary>
        /// Posee sur un sol capable de reagir. Le moteur la calcule quand meme comme
        /// suspendue, ce qui est securitaire, et le dit.
        /// </summary>
        SoilBearing
    }

    /// <summary>
    /// Donnees d'une longrine, independantes de Revit. Dimensions en millimetres.
    /// </summary>
    public sealed class GradeBeamData
    {
        public string Id { get; set; }
        public string Name { get; set; }

        /// <summary>Repere de l'element, prefixe des reperes de barres.</summary>
        public string Mark { get; set; }

        /// <summary>Largeur b_w (mm).</summary>
        public double WidthMm { get; set; }

        /// <summary>Hauteur h_w (mm).</summary>
        public double HeightMm { get; set; }

        /// <summary>Portee entre nus d'appui (mm).</summary>
        public double SpanMm { get; set; }

        public GradeBeamSpanKind SpanKind { get; set; }

        public List<string> Remarks { get; private set; }

        public GradeBeamData()
        {
            Remarks = new List<string>();
            Name = "Longrine";
            Mark = "LG";
            WidthMm = 300.0;
            HeightMm = 500.0;
            SpanKind = GradeBeamSpanKind.SimplySupported;
        }

        /// <summary>Aire de beton de la section (mm2).</summary>
        public double GrossAreaMm2 { get { return WidthMm * HeightMm; } }

        /// <summary>Volume de beton (mm3).</summary>
        public double VolumeMm3 { get { return GrossAreaMm2 * SpanMm; } }

        /// <summary>Elancement de la travee, portee sur hauteur.</summary>
        public double SpanToDepth
        {
            get { return HeightMm > 0 ? SpanMm / HeightMm : 0.0; }
        }

        public string SectionLabel
        {
            get
            {
                return string.Format("{0:0} x {1:0} mm - portee {2:0} mm",
                                     WidthMm, HeightMm, SpanMm);
            }
        }
    }
}
