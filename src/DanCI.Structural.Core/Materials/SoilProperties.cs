namespace DanCI.Structural.Core.Materials
{
    /// <summary>
    /// Caracteristiques du sol de fondation, telles qu'elles figurent au rapport
    /// geotechnique. Le moteur travaille a partir d'une contrainte de calcul admissible,
    /// donnee la plus couramment disponible ; l'angle de frottement sert au glissement.
    /// </summary>
    public sealed class SoilProperties
    {
        /// <summary>Contrainte de calcul admissible du sol (kPa).</summary>
        public double AllowableBearingPressureKpa { get; set; }

        /// <summary>Angle de frottement a l'interface sol-semelle delta (degres).</summary>
        public double InterfaceFrictionAngleDeg { get; set; }

        /// <summary>Adherence a l'interface (kPa), nulle pour un sol pulverulent.</summary>
        public double InterfaceAdhesionKpa { get; set; }

        /// <summary>Poids volumique des terres au-dessus de la semelle (kN/m3).</summary>
        public double SoilUnitWeightKnM3 { get; set; }

        public SoilProperties()
        {
            AllowableBearingPressureKpa = 250.0;
            InterfaceFrictionAngleDeg = 30.0;
            InterfaceAdhesionKpa = 0.0;
            SoilUnitWeightKnM3 = 18.0;
        }
    }
}
