namespace DanCI.Structural.Core.Materials
{
    /// <summary>Beton, decrit par sa seule resistance caracteristique (MPa).</summary>
    public sealed class ConcreteMaterial
    {
        /// <summary>Resistance caracteristique en compression sur cylindre f_ck (MPa).</summary>
        public double FckMPa { get; set; }

        public ConcreteMaterial()
        {
            FckMPa = 25.0;
        }

        public ConcreteMaterial(double fckMPa)
        {
            FckMPa = fckMPa;
        }

        public string Grade
        {
            get { return string.Format("C{0:0}", FckMPa); }
        }
    }

    /// <summary>Acier d'armature.</summary>
    public sealed class SteelMaterial
    {
        /// <summary>Limite d'elasticite caracteristique f_yk (MPa).</summary>
        public double FykMPa { get; set; }

        /// <summary>Module d'elasticite E_s (MPa).</summary>
        public double ElasticModulusMPa { get; set; }

        public SteelMaterial()
        {
            FykMPa = 500.0;
            ElasticModulusMPa = 200000.0;
        }

        public SteelMaterial(double fykMPa)
            : this()
        {
            FykMPa = fykMPa;
        }

        public string Grade
        {
            get { return string.Format("B{0:0}", FykMPa); }
        }
    }
}
