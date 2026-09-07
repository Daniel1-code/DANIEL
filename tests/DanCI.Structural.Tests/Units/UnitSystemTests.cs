using DanCI.Structural.Core.Units;
using Xunit;

namespace DanCI.Structural.Tests.Units
{
    public class UnitSystemTests
    {
        [Fact]
        public void UnPied_Vaut_304_8_Millimetres()
        {
            Assert.Equal(304.8, UnitSystem.FeetToMm(1.0), 9);
            Assert.Equal(1.0, UnitSystem.MmToFeet(304.8), 9);
        }

        [Fact]
        public void Les_Conversions_Sont_Reversibles()
        {
            const double millimetres = 1234.567;
            Assert.Equal(millimetres, UnitSystem.FeetToMm(UnitSystem.MmToFeet(millimetres)), 9);
        }

        [Theory]
        [InlineData(8, 50.27)]
        [InlineData(12, 113.10)]
        [InlineData(16, 201.06)]
        [InlineData(20, 314.16)]
        [InlineData(25, 490.87)]
        [InlineData(32, 804.25)]
        public void Aire_Des_Barres_Conforme_Aux_Tables(double diameterMm, double expectedMm2)
        {
            Assert.Equal(expectedMm2, UnitSystem.BarArea(diameterMm), 2);
        }

        [Fact]
        public void Les_Efforts_Se_Convertissent_En_Unites_Internes()
        {
            Assert.Equal(1000.0, UnitSystem.KnToN(1.0), 9);
            Assert.Equal(1e6, UnitSystem.KnmToNmm(1.0), 9);
            Assert.Equal(1.0, UnitSystem.NmmToKnm(1e6), 9);
        }
    }
}
