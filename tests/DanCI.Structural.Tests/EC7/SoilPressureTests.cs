using DanCI.Structural.Eurocodes.EC7;
using Xunit;

namespace DanCI.Structural.Tests.EC7
{
    /// <summary>
    /// Contraintes sous une semelle, EN 1997-1.
    /// Semelle de reference : 2 400 x 2 400 mm, charge de poteau 1 200 kN, poids propre
    /// 2,4 x 2,4 x 0,6 x 25 = 86,4 kN, soit 1 286,4 kN a la base.
    ///   Aire = 5,76 m2 -> contrainte moyenne = 1 286,4 / 5,76 = 223,3 kPa
    ///   Contrainte nette structurelle = 1 200 / 5,76 = 208,3 kPa
    /// </summary>
    public class SoilPressureTests
    {
        private const double WidthMm = 2400.0;
        private const double TotalLoadN = 1286400.0;
        private const double ColumnLoadN = 1200000.0;

        [Fact]
        public void Charge_Centree_Donne_Une_Contrainte_Uniforme()
        {
            SoilPressureResult result = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                0.0, 0.0, ColumnLoadN);

            Assert.True(result.WithinCore);
            Assert.InRange(result.MaxPressureKpa, 222.0, 224.5);
            Assert.Equal(result.MaxPressureKpa, result.MinPressureKpa, 6);
            Assert.InRange(result.NetPressureKpa, 207.0, 209.5);
        }

        [Fact]
        public void Une_Excentricite_Dans_Le_Noyau_Central_Donne_Un_Trapeze()
        {
            // M = 200 kN.m -> e_x = 200e6 / 1 286 400 = 155,5 mm <= B/6 = 400 mm
            // sigma_max = 223,3 (1 + 6 x 155,5 / 2 400) = 310,1 kPa
            // sigma_min = 223,3 (1 - 0,3888)             = 136,5 kPa
            SoilPressureResult result = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                200e6, 0.0, ColumnLoadN);

            Assert.True(result.WithinCore);
            Assert.InRange(result.EccentricityXMm, 154.0, 157.0);
            Assert.InRange(result.MaxPressureKpa, 307.0, 313.0);
            Assert.InRange(result.MinPressureKpa, 134.0, 139.0);
            Assert.True(result.MinPressureKpa > 0, "Aucune traction ne doit apparaitre.");
        }

        [Fact]
        public void L_Aire_Effective_Suit_L_Annexe_D()
        {
            // B' = 2 400 - 2 x 155,5 = 2 089 mm ; A' = 2 089 x 2 400 = 5,01 m2
            // sigma' = 1 286,4 / 5,01 = 256,6 kPa
            SoilPressureResult result = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                200e6, 0.0, ColumnLoadN);

            Assert.InRange(result.EffectiveWidthMm, 2085.0, 2093.0);
            Assert.Equal(WidthMm, result.EffectiveLengthMm, 6);
            Assert.InRange(result.EffectivePressureKpa, 253.0, 260.0);
            Assert.True(result.EffectivePressureKpa > result.MaxPressureKpa - 60.0);
        }

        [Fact]
        public void Une_Excentricite_Hors_Noyau_Est_Signalee()
        {
            // M = 600 kN.m -> e_x = 466 mm > B/6 = 400 mm : soulevement.
            SoilPressureResult result = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                600e6, 0.0, ColumnLoadN);

            Assert.False(result.WithinCore);
            Assert.True(result.MinPressureKpa < 0,
                "La distribution lineaire doit annoncer une traction, physiquement impossible.");
            Assert.Contains("DEPASSE", result.Justification);
        }

        [Fact]
        public void La_Double_Excentricite_Se_Cumule()
        {
            SoilPressureResult single = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                200e6, 0.0, ColumnLoadN);
            SoilPressureResult biaxial = SoilPressure.Compute(WidthMm, WidthMm, TotalLoadN,
                200e6, 200e6, ColumnLoadN);

            Assert.True(biaxial.MaxPressureKpa > single.MaxPressureKpa);
            Assert.True(biaxial.EffectiveLengthMm < single.EffectiveLengthMm);
        }
    }

    /// <summary>Stabilite d'ensemble : glissement et renversement.</summary>
    public class StabilityChecksTests
    {
        [Fact]
        public void Le_Glissement_Suit_L_Article_6_5_3()
        {
            // tan(30) / 1,25 = 0,5774 / 1,25 = 0,4619
            // R_d = 1 286,4 x 0,4619 = 594 kN ; H_Ed = 150 kN -> taux 0,25
            StabilityResult result = StabilityChecks.Sliding(150000.0, 1286400.0, 30.0, 0.0,
                                                             5760000.0);

            Assert.InRange(result.Stabilising / 1000.0, 590.0, 599.0);
            Assert.InRange(result.Utilization, 0.24, 0.26);
            Assert.True(result.Passes);
        }

        [Fact]
        public void L_Adherence_S_Ajoute_Au_Frottement()
        {
            StabilityResult without = StabilityChecks.Sliding(150000.0, 1286400.0, 30.0, 0.0,
                                                              5760000.0);
            StabilityResult with = StabilityChecks.Sliding(150000.0, 1286400.0, 30.0, 20.0,
                                                           5760000.0);

            // 20 kPa x 5,76 m2 = 115,2 kN de plus.
            Assert.InRange((with.Stabilising - without.Stabilising) / 1000.0, 113.0, 117.0);
        }

        [Fact]
        public void Un_Glissement_Excessif_Est_Detecte()
        {
            StabilityResult result = StabilityChecks.Sliding(800000.0, 1286400.0, 30.0, 0.0,
                                                             5760000.0);
            Assert.False(result.Passes);
        }

        [Fact]
        public void Le_Renversement_Suit_L_Etat_Limite_EQU()
        {
            // M_stb = 0,9 x 1 286,4 x 2,4 / 2 = 1 389 kN.m ; M_dst = 200 kN.m -> taux 0,14
            StabilityResult result = StabilityChecks.Overturning(200e6, 1286400.0, 2400.0);

            Assert.InRange(result.Stabilising / 1e6, 1385.0, 1394.0);
            Assert.InRange(result.Utilization, 0.13, 0.15);
            Assert.True(result.Passes);
        }

        [Fact]
        public void Un_Renversement_Excessif_Est_Detecte()
        {
            StabilityResult result = StabilityChecks.Overturning(2000e6, 1286400.0, 2400.0);
            Assert.False(result.Passes);
        }
    }
}
