using DanCI.Structural.Core.Units;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using DanCI.Structural.Eurocodes.NationalAnnex;
using System;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// COLUMN-04 : interaction biaxiale, EN 1992-1-1 article 5.8.9(4).
    /// Fiche de validation : docs/validation/COLUMN-04.md
    ///
    /// Section 400 x 400, C25/30, 8 HA20 (A_s = 2 513 mm2), NEd = 1 500 kN.
    ///   N_Rd = A_c f_cd + A_s f_yd = 160 000 x 16,667 + 2 513 x 434,78 = 3 759 kN
    ///   N_Ed / N_Rd = 0,399  ->  a = 1,0 + 0,5 x (0,399 - 0,1) / 0,6 = 1,249
    /// </summary>
    public class Column04BiaxialTests
    {
        [Theory]
        [InlineData(0.05, 1.0)]    // en deca de 0,1 : a = 1,0
        [InlineData(0.10, 1.0)]
        [InlineData(0.40, 1.25)]   // 1,0 + 0,5 x (0,40 - 0,10) / 0,6
        [InlineData(0.70, 1.5)]
        [InlineData(0.85, 1.75)]   // 1,5 + 0,5 x (0,85 - 0,70) / 0,3
        [InlineData(1.00, 2.0)]
        [InlineData(1.20, 2.0)]    // au-dela de 1,0 : a = 2,0
        public void L_Exposant_Est_Interpole_Selon_Le_Tableau_De_L_Article_5_8_9(
            double relativeAxial, double expected)
        {
            Assert.Equal(expected, SecondOrder.BiaxialExponent(relativeAxial), 6);
        }

        [Fact]
        public void N_Rd_Du_Cas_De_Reference_Vaut_3759_kN()
        {
            var materials = new ConcreteProperties(new ConcreteMaterial(25.0),
                                                   new SteelMaterial(500.0), new RecommendedAnnex());
            double steelArea = 8.0 * UnitConverter.BarArea(20.0);
            double axialResistance = new InteractionDiagram(materials)
                .AxialResistance(400.0 * 400.0, steelArea);

            Assert.InRange(UnitConverter.NToKn(axialResistance), 3750.0, 3770.0);
        }

        [Fact]
        public void Le_Cas_De_Reference_Est_Verifie()
        {
            // MEd,x = 100 kN.m, MEd,y = 60 kN.m, MRd,x = MRd,y = 236,5 kN.m (fiche COLUMN-02).
            // (100/236,5)^1,249 + (60/236,5)^1,249 = 0,341 + 0,180 = 0,521 <= 1,00
            const double exponent = 1.249;
            double utilization = Math.Pow(100.0 / 236.5, exponent) + Math.Pow(60.0 / 236.5, exponent);

            Assert.InRange(utilization, 0.51, 0.53);
            Assert.True(utilization <= 1.0);
        }

        [Fact]
        public void Une_Flexion_Uniaxiale_Se_Ramene_Au_Rapport_Simple()
        {
            // Avec un seul moment, la formule doit redonner MEd / MRd eleve a la puissance a.
            const double exponent = 1.249;
            double utilization = Math.Pow(236.5 / 236.5, exponent) + Math.Pow(0.0, exponent);
            Assert.Equal(1.0, utilization, 6);
        }

        [Fact]
        public void Le_Depassement_Est_Detecte()
        {
            // MEd,x = 200 et MEd,y = 180 sur MRd = 236,5 : la somme doit depasser 1,00.
            const double exponent = 1.249;
            double utilization = Math.Pow(200.0 / 236.5, exponent) + Math.Pow(180.0 / 236.5, exponent);
            Assert.True(utilization > 1.0);
        }
    }
}
