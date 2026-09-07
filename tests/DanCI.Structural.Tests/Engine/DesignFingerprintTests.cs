using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Column;
using Xunit;

namespace DanCI.Structural.Tests.Engine
{
    /// <summary>
    /// Empreinte des donnees de calcul : c'est elle qui permettra de detecter qu'un element
    /// a change et que son ferraillage doit etre repris.
    /// </summary>
    public class DesignFingerprintTests
    {
        private static ColumnData Column()
        {
            return new ColumnData
            {
                Id = "c1",
                Name = "P1",
                Shape = SectionShape.Rectangular,
                WidthMm = 400.0,
                DepthMm = 400.0,
                HeightMm = 3000.0
            };
        }

        [Fact]
        public void Les_Memes_Donnees_Donnent_La_Meme_Empreinte()
        {
            var settings = new ColumnDesignSettings { AxialLoadKn = 1500.0 };
            Assert.Equal(ColumnDesignFingerprint.Compute(Column(), settings),
                         ColumnDesignFingerprint.Compute(Column(), settings));
        }

        [Fact]
        public void Un_Changement_De_Section_Change_L_Empreinte()
        {
            var settings = new ColumnDesignSettings { AxialLoadKn = 1500.0 };
            ColumnData enlarged = Column();
            enlarged.WidthMm = 450.0;

            Assert.NotEqual(ColumnDesignFingerprint.Compute(Column(), settings),
                            ColumnDesignFingerprint.Compute(enlarged, settings));
        }

        [Fact]
        public void Un_Changement_D_Effort_Change_L_Empreinte()
        {
            Assert.NotEqual(
                ColumnDesignFingerprint.Compute(Column(),
                    new ColumnDesignSettings { AxialLoadKn = 1500.0 }),
                ColumnDesignFingerprint.Compute(Column(),
                    new ColumnDesignSettings { AxialLoadKn = 1800.0 }));
        }

        [Fact]
        public void Un_Changement_De_Classe_D_Exposition_Change_L_Empreinte()
        {
            Assert.NotEqual(
                ColumnDesignFingerprint.Compute(Column(),
                    new ColumnDesignSettings { Exposure = ExposureClass.XC1 }),
                ColumnDesignFingerprint.Compute(Column(),
                    new ColumnDesignSettings { Exposure = ExposureClass.XC4 }));
        }

        [Fact]
        public void Une_Variation_Numerique_Insignifiante_Ne_Declenche_Pas_De_Recalcul()
        {
            // La geometrie lue dans Revit peut varier de quelques millièmes de millimetre :
            // l'empreinte est arrondie au centieme pour ne pas signaler un faux changement.
            ColumnData original = Column();
            ColumnData jittered = Column();
            jittered.WidthMm = 400.001;

            var settings = new ColumnDesignSettings();
            Assert.Equal(ColumnDesignFingerprint.Compute(original, settings),
                         ColumnDesignFingerprint.Compute(jittered, settings));
        }

        [Fact]
        public void L_Empreinte_Est_Courte_Et_Stable()
        {
            string hash = new DesignFingerprint().Add("a", 1.0).Add("b", "x").Compute();
            Assert.Equal(16, hash.Length);
            Assert.Equal(hash, new DesignFingerprint().Add("a", 1.0).Add("b", "x").Compute());
        }
    }
}
