using DanCI.Structural.Eurocodes.EC2;
using Xunit;

namespace DanCI.Structural.Tests.EC2
{
    /// <summary>
    /// Faconnage des armatures, EN 1992-1-1 article 8.3.
    ///
    /// Mandrin minimal du tableau 8.1N : 4 phi jusqu'a 16 mm, 7 phi au-dela.
    /// Deduction d'un pli : 2 R tan(beta/2) - R beta, avec R = (phi_m + phi)/2.
    /// </summary>
    public class BarBendingTests
    {
        [Theory]
        [InlineData(8.0, 32.0)]
        [InlineData(12.0, 48.0)]
        [InlineData(16.0, 64.0)]     // la limite du tableau est INCLUSE dans 4 phi
        [InlineData(20.0, 140.0)]
        [InlineData(25.0, 175.0)]
        public void Le_Mandrin_Minimal_Suit_Le_Tableau_8_1N(double diameter, double expected)
        {
            Assert.Equal(expected, BarBending.MinimumMandrelDiameterMm(diameter), 6);
        }

        [Fact]
        public void Le_Saut_Du_Tableau_Se_Fait_Au_Dessus_De_16_mm()
        {
            // 16 mm est encore a 4 phi ; le premier diametre courant au-dessus passe a 7.
            Assert.Equal(64.0, BarBending.MinimumMandrelDiameterMm(16.0), 6);
            Assert.Equal(140.0, BarBending.MinimumMandrelDiameterMm(20.0), 6);
        }

        [Theory]
        [InlineData(8.0, 8.5841)]
        [InlineData(12.0, 12.8761)]
        [InlineData(20.0, 34.3363)]
        public void La_Deduction_D_Un_Pli_A_90_Degres(double diameter, double expected)
        {
            // R = (phi_m + phi)/2 ; deduction = 2 R tan(45) - R pi/2 = 0,4292 R
            BendResult bend = BarBending.Bend(90.0, diameter);

            Assert.Equal(expected, bend.DeductionMm, 3);
        }

        [Fact]
        public void Un_Pli_Faible_Ne_Coute_Presque_Rien()
        {
            // 45 degres sur HA12 : 1,29 mm, contre 12,88 mm a 90 degres. La deduction
            // croit tres vite avec l'angle, elle n'est pas proportionnelle.
            Assert.Equal(1.2909, BarBending.Bend(45.0, 12.0).DeductionMm, 3);
            Assert.True(BarBending.Bend(90.0, 12.0).DeductionMm
                        > 9.0 * BarBending.Bend(45.0, 12.0).DeductionMm);
        }

        [Fact]
        public void Une_Barre_Droite_N_A_Aucune_Deduction()
        {
            Assert.Equal(0.0, BarBending.Bend(0.0, 12.0).DeductionMm, 6);
            Assert.False(BarBending.Bend(0.0, 12.0).IsBeyondModel);
        }

        [Fact]
        public void Un_Pli_Trop_Ferme_Sort_Du_Modele_Et_Le_Dit()
        {
            // tan(beta/2) diverge vers 180 degres : un tel pli est un crochet, il se compte
            // par son retour. Le moteur le signale au lieu de rendre une valeur absurde.
            BendResult bend = BarBending.Bend(170.0, 12.0);

            Assert.True(bend.IsBeyondModel);
            Assert.Equal(0.0, bend.DeductionMm, 6);
        }

        [Fact]
        public void La_Deduction_Est_Toujours_Positive_La_Barre_Coupe_Le_Coin()
        {
            // Le chemin d'angle a angle est TOUJOURS plus long que la fibre moyenne :
            // sommer les segments d'une polyligne surestime la longueur.
            for (double angle = 5.0; angle < BarBending.MaximumModelledAngleDegrees; angle += 5.0)
            {
                Assert.True(BarBending.Bend(angle, 12.0).DeductionMm > 0.0);
            }
        }

        [Fact]
        public void Un_Mandrin_Impose_Remplace_Celui_Du_Tableau()
        {
            // Un mandrin plus grand fait un rayon plus grand, donc une deduction plus forte.
            BendResult table = BarBending.Bend(90.0, 12.0);
            BendResult larger = BarBending.Bend(90.0, 12.0, 100.0);

            Assert.Equal(48.0, table.MandrelDiameterMm, 6);
            Assert.Equal(100.0, larger.MandrelDiameterMm, 6);
            Assert.True(larger.DeductionMm > table.DeductionMm);
        }

        // ------------------------------------------------------------------
        // Longueur de coupe
        // ------------------------------------------------------------------

        [Fact]
        public void La_Longueur_De_Coupe_D_Un_Cadre_300x500()
        {
            // Perimetre d'angle a angle 2 x (300 + 500) = 1 600 mm
            // 4 plis a 90 degres en HA8 : 4 x 8,584 = 34,34 mm
            // Crochets 2 x 10 phi = 160 mm
            // Coupe = 1 600 - 34,34 + 160 = 1 725,7 mm
            CutLengthResult cut = BarBending.CutLength(1600.0,
                new[] { 90.0, 90.0, 90.0, 90.0 }, 8.0, 160.0);

            Assert.Equal(4, cut.BendCount);
            Assert.Equal(34.336, cut.TotalDeductionMm, 2);
            Assert.Equal(1725.66, cut.CutLengthMm, 1);
        }

        [Fact]
        public void Sommer_Les_Segments_Surestime_De_Deux_Pourcent_Sur_Un_Cadre()
        {
            // 34,3 mm sur 1 600 : petit par barre, et il se compte en tonnes sur un projet.
            CutLengthResult cut = BarBending.CutLength(1600.0,
                new[] { 90.0, 90.0, 90.0, 90.0 }, 8.0);

            Assert.InRange(cut.TotalDeductionMm / cut.PolylineLengthMm * 100.0, 2.0, 2.5);
        }

        [Fact]
        public void Une_Barre_Droite_A_Une_Coupe_Egale_A_Son_Developpe()
        {
            CutLengthResult cut = BarBending.CutLength(4000.0, new double[0], 12.0);

            Assert.Equal(0, cut.BendCount);
            Assert.Equal(4000.0, cut.CutLengthMm, 6);
        }

        [Fact]
        public void La_Justification_Nomme_L_Article_Et_Le_Mandrin()
        {
            CutLengthResult cut = BarBending.CutLength(1600.0, new[] { 90.0 }, 12.0);

            Assert.Contains("8.3", cut.Justification);
            Assert.Contains("tableau 8.1N", cut.Justification);
            Assert.Contains("48", cut.Justification);
        }

        // ------------------------------------------------------------------
        // Expression (8.1)
        // ------------------------------------------------------------------

        [Fact]
        public void L_Expression_8_1_Donne_Le_Mandrin_Contre_La_Rupture_Du_Beton()
        {
            // F_bt = 50 kN, a_b = 60 mm, phi = 16 mm, f_cd = 16,667 MPa (C25/30)
            // 50 000 x (1/60 + 1/32) / 16,667 = 143,7 mm
            double mandrel = BarBending.MandrelAgainstConcreteFailure(50000.0, 60.0, 16.0,
                                                                      16.667);

            Assert.Equal(143.7, mandrel, 1);
        }

        [Fact]
        public void L_Expression_8_1_Peut_Exiger_Bien_Plus_Que_Le_Tableau()
        {
            // 143,7 mm contre 64 mm au tableau : ce n'est pas un detail, et c'est pour cela
            // que le moteur rend les deux valeurs sans les confondre.
            Assert.True(BarBending.MandrelAgainstConcreteFailure(50000.0, 60.0, 16.0, 16.667)
                        > 2.0 * BarBending.MinimumMandrelDiameterMm(16.0));
        }

        [Fact]
        public void L_Expression_8_1_Plafonne_f_cd_Au_C55_67()
        {
            // L'article l'impose : au-dela du C55/67, f_cd n'est plus credite.
            double atC55 = BarBending.MandrelAgainstConcreteFailure(50000.0, 60.0, 16.0,
                                                                     55.0 / 1.5);
            double atC90 = BarBending.MandrelAgainstConcreteFailure(50000.0, 60.0, 16.0,
                                                                     90.0 / 1.5);

            Assert.Equal(atC55, atC90, 6);
        }

        [Fact]
        public void Une_Donnee_Manquante_Ne_Produit_Pas_De_Valeur_Inventee()
        {
            Assert.Equal(0.0, BarBending.MandrelAgainstConcreteFailure(0.0, 60.0, 16.0, 16.667), 6);
            Assert.Equal(0.0, BarBending.MandrelAgainstConcreteFailure(50000.0, 0.0, 16.0, 16.667), 6);
        }
    }
}
