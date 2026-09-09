using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// EN 1992-1-1 art. 9.3.1.2(2) — encastrement partiel non pris en compte dans l'analyse.
    ///
    /// La regle est courte, et c'est ce qui la rend facile a oublier : quand on calcule une
    /// dalle ou une volee en travee isostatique, on n'a pas fait disparaitre le moment
    /// negatif sur les appuis coules en continuite. On a seulement choisi de ne pas le
    /// calculer. L'Eurocode impose alors un forfait.
    /// </summary>
    public class PartialFixityTests
    {
        [Fact]
        public void Le_Forfait_Est_Le_Quart_Du_Moment_De_Travee()
        {
            Assert.Equal(0.25, PartialFixity.MomentFraction, 6);
            Assert.Equal(10.0e6, PartialFixity.RequiredSupportMomentNmm(40.0e6), 1);
        }

        [Fact]
        public void La_Longueur_Minimale_Est_Un_Cinquieme_De_La_Portee()
        {
            Assert.Equal(0.20, PartialFixity.ExtentFraction, 6);
            Assert.Equal(850.0, PartialFixity.MinimumExtentMm(4250.0), 6);
        }

        [Theory]
        [InlineData(0.0)]
        [InlineData(-1000.0)]
        public void Un_Moment_Nul_Ou_Negatif_N_Exige_Rien(double moment)
        {
            Assert.Equal(0.0, PartialFixity.RequiredSupportMomentNmm(moment), 6);
        }

        [Fact]
        public void La_Longueur_Est_Jugee_A_La_Tolerance_De_Faconnage()
        {
            // Un chapeau arrondi au pas de 50 mm qui vaut exactement 0,2 l ne doit pas
            // echouer sur une erreur d'arrondi flottant.
            Assert.True(PartialFixity.ExtentIsSufficient(850.0, 4250.0));
            Assert.True(PartialFixity.ExtentIsSufficient(849.5, 4250.0));
            Assert.False(PartialFixity.ExtentIsSufficient(800.0, 4250.0));
        }

        [Fact]
        public void La_Justification_Cite_Son_Article_Et_Sa_Valeur()
        {
            string text = PartialFixity.Justification(4250.0);

            Assert.Contains("9.3.1.2(2)", text);
            Assert.Contains("25", text);
            Assert.Contains("850", text);
        }
    }
}
