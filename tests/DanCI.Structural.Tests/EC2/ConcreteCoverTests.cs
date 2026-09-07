using DanCI.Structural.Core.Materials;
using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Enrobage, EN 1992-1-1 article 4.4.1 et tableaux 4.3N et 4.4N.
    /// Les valeurs attendues sont lues directement dans les tableaux de la norme.
    /// </summary>
    public class ConcreteCoverTests
    {
        [Theory]
        // 50 ans, beton courant : classe de reference S4.
        [InlineData(ExposureClass.XC1, 25.0, false, false, 4)]
        // C30/37 en XC1 : seuil atteint, la classe descend a S3.
        [InlineData(ExposureClass.XC1, 30.0, false, false, 3)]
        // C30/37 en XC3 : seuil de C35/45 non atteint, on reste en S4.
        [InlineData(ExposureClass.XC3, 30.0, false, false, 4)]
        // C35/45 en XC3 : seuil atteint.
        [InlineData(ExposureClass.XC3, 35.0, false, false, 3)]
        // Controle de production assure : une unite de moins.
        [InlineData(ExposureClass.XC1, 25.0, false, true, 3)]
        // 100 ans : deux unites de plus.
        [InlineData(ExposureClass.XC1, 25.0, true, false, 6)]
        // 100 ans, C30/37 et controle assure : 4 + 2 - 1 - 1 = 4.
        [InlineData(ExposureClass.XC1, 30.0, true, true, 4)]
        public void Classe_Structurale_Conforme_Au_Tableau_4_3N(ExposureClass exposure,
                                                                double fck, bool longLife,
                                                                bool qualityControl, int expected)
        {
            DesignWorkingLife life = longLife ? DesignWorkingLife.Years100 : DesignWorkingLife.Years50;
            Assert.Equal(expected, ConcreteCover.StructuralClass(exposure, fck, life, false,
                                                                 qualityControl));
        }

        [Theory]
        [InlineData(ExposureClass.X0, 4, 10.0)]
        [InlineData(ExposureClass.XC1, 4, 15.0)]
        [InlineData(ExposureClass.XC2, 4, 25.0)]
        [InlineData(ExposureClass.XC3, 4, 25.0)]
        [InlineData(ExposureClass.XC4, 4, 30.0)]
        [InlineData(ExposureClass.XD1, 4, 35.0)]
        [InlineData(ExposureClass.XS1, 4, 35.0)]
        [InlineData(ExposureClass.XD3, 4, 45.0)]
        [InlineData(ExposureClass.XS3, 4, 45.0)]
        [InlineData(ExposureClass.XC1, 3, 10.0)]
        [InlineData(ExposureClass.XC4, 6, 40.0)]
        public void C_Min_Dur_Conforme_Au_Tableau_4_4N(ExposureClass exposure, int structuralClass,
                                                       double expected)
        {
            Assert.Equal(expected, ConcreteCover.MinimumDurabilityCover(exposure, structuralClass), 6);
        }

        [Fact]
        public void Cas_De_Reference_Poteau_Interieur_XC1_C25_HA16()
        {
            // Classe structurale S4 -> c_min,dur = 15 mm ; c_min,b = 16 mm (diametre de barre).
            // c_min = max(16 ; 15 ; 10) = 16 mm ; c_nom = 16 + 10 = 26 mm.
            CoverResult cover = ConcreteCover.Compute(16.0, ExposureClass.XC1, 25.0);

            Assert.Equal(4, cover.StructuralClass);
            Assert.Equal(15.0, cover.MinCoverDurabilityMm, 6);
            Assert.Equal(16.0, cover.MinCoverBondMm, 6);
            Assert.Equal(16.0, cover.MinCoverMm, 6);
            Assert.Equal(26.0, cover.NominalCoverMm, 6);
        }

        [Fact]
        public void Cas_De_Reference_Poteau_Exterieur_XC4_C30_HA20()
        {
            // XC4, C30/37 : seuil C40/50 non atteint -> S4 -> c_min,dur = 30 mm.
            // c_min = max(20 ; 30 ; 10) = 30 mm ; c_nom = 30 + 10 = 40 mm.
            CoverResult cover = ConcreteCover.Compute(20.0, ExposureClass.XC4, 30.0);

            Assert.Equal(4, cover.StructuralClass);
            Assert.Equal(30.0, cover.MinCoverDurabilityMm, 6);
            Assert.Equal(30.0, cover.MinCoverMm, 6);
            Assert.Equal(40.0, cover.NominalCoverMm, 6);
        }

        [Fact]
        public void Cas_De_Reference_Bord_De_Mer_XS3_C45_HA25()
        {
            // XS3, C45/55 : seuil atteint -> S3 -> c_min,dur = 40 mm.
            // c_min = max(25 ; 40 ; 10) = 40 mm ; c_nom = 50 mm.
            CoverResult cover = ConcreteCover.Compute(25.0, ExposureClass.XS3, 45.0);

            Assert.Equal(3, cover.StructuralClass);
            Assert.Equal(40.0, cover.MinCoverDurabilityMm, 6);
            Assert.Equal(50.0, cover.NominalCoverMm, 6);
        }

        [Fact]
        public void Une_Grosse_Barre_Fait_Gouverner_L_Adherence()
        {
            // HA40 en XC1 : c_min,b = 40 mm depasse c_min,dur = 15 mm.
            CoverResult cover = ConcreteCover.Compute(40.0, ExposureClass.XC1, 25.0);
            Assert.Equal(40.0, cover.MinCoverMm, 6);
            Assert.Equal(50.0, cover.NominalCoverMm, 6);
        }

        [Fact]
        public void L_Enrobage_Ne_Descend_Jamais_Sous_Dix_Millimetres()
        {
            // X0 avec une barre HA8 et C30/37 : classe S3, c_min,dur = 10 mm.
            CoverResult cover = ConcreteCover.Compute(8.0, ExposureClass.X0, 30.0);
            Assert.True(cover.MinCoverMm >= 10.0);
        }
    }
}
