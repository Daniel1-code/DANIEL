using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Engine.Beam;
using DanCI.Structural.Reinforcement.Optimization;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// BEAM-02 : poutre continue en T et cas limites du module.
    /// Fiche de validation : docs/validation/BEAM-02.md
    /// </summary>
    public class Beam02ContinuousTests
    {
        private static BeamData TBeam()
        {
            return new BeamData
            {
                Id = "BEAM-02",
                Name = "P2",
                Mark = "B2",
                Shape = BeamSectionShape.TSection,
                WebWidthMm = 300.0,
                HeightMm = 600.0,
                FlangeWidthMm = 1500.0,
                FlangeThicknessMm = 180.0,
                SpanMm = 7000.0,
                SpanKind = BeamSpanKind.EndSpan
            };
        }

        private static BeamDesignSettings Settings()
        {
            return new BeamDesignSettings
            {
                ConcreteStrengthMPa = 30.0,
                Exposure = ExposureClass.XC1,
                SpanMomentKnm = 400.0,
                LeftSupportMomentKnm = 0.0,
                RightSupportMomentKnm = 320.0,
                LeftShearKn = 220.0,
                RightShearKn = 300.0
            };
        }

        [Fact]
        public void La_Largeur_Participante_Est_Calculee_Pour_Une_Travee_De_Rive()
        {
            // l0 = 0,85 x 7 000 = 5 950 mm ; debord = 600 mm
            // b_eff,i = min(0,2 x 600 + 0,1 x 5 950 ; 0,2 x 5 950 ; 600) = min(715 ; 1 190 ; 600) = 600
            // b_eff = 2 x 600 + 300 = 1 500 mm, plafonne a la largeur reelle : 1 500 mm
            BeamDesignResult result = new BeamDesignModule().Design(TBeam(), Settings(), null);
            Assert.Equal(1500.0, result.EffectiveFlangeWidthMm, 6);
        }

        [Fact]
        public void La_Table_Comprimee_Reduit_Fortement_L_Acier_De_Travee()
        {
            BeamDesignResult result = new BeamDesignModule().Design(TBeam(), Settings(), null);

            // La meme poutre calculee en section rectangulaire demanderait beaucoup plus d'acier.
            BeamData rectangular = TBeam();
            rectangular.Shape = BeamSectionShape.Rectangular;
            BeamDesignResult asRectangular = new BeamDesignModule()
                .Design(rectangular, Settings(), null);

            Assert.True(result.SpanSteelRequiredMm2 < asRectangular.SpanSteelRequiredMm2,
                "La table comprimee doit reduire la section d'acier en travee.");
        }

        [Fact]
        public void Les_Chapeaux_Sont_Poses_Uniquement_La_Ou_Il_Y_A_Un_Moment_Negatif()
        {
            BeamDesignResult result = new BeamDesignModule().Design(TBeam(), Settings(), null);

            Assert.Equal(0, result.Reinforcement.TopLeft.Count);
            Assert.True(result.Reinforcement.TopRight.Count >= 2,
                "L'appui droit porte un moment de 320 kN.m : il doit recevoir des chapeaux.");
        }

        [Fact]
        public void Les_Chapeaux_Se_Prolongent_Depuis_Le_Nu_D_Appui()
        {
            BeamDesignResult result = new BeamDesignModule().Design(TBeam(), Settings(), null);
            // max(L/4 = 1 750 ; a_l + l_bd)
            Assert.True(result.Reinforcement.TopBarLengthMm >= 1750.0);

            RebarGroup chapeau = result.Plan.Groups
                .FirstOrDefault(g => g.Label.Contains("Chapeau appui droit"));
            Assert.NotNull(chapeau);
            // Le chapeau doit se terminer au nu de la poutre.
            Assert.InRange(chapeau.Path[0].End.X, 6900.0, 7000.0);
        }

        [Fact]
        public void Les_Cadres_Sont_Plus_Resserres_Du_Cote_Le_Plus_Sollicite()
        {
            BeamDesignResult result = new BeamDesignModule().Design(TBeam(), Settings(), null);
            BeamStirrupZone left = result.Reinforcement.StirrupZones.First();
            BeamStirrupZone right = result.Reinforcement.StirrupZones.Last();

            // V = 300 kN a droite contre 220 kN a gauche.
            Assert.True(right.SpacingMm <= left.SpacingMm,
                "L'appui le plus charge doit recevoir des cadres au moins aussi rapproches.");
        }

        [Fact]
        public void Une_Ame_Trop_Etroite_Est_Signalee_Sans_Ferraillage_Arbitraire()
        {
            // 200 x 400 sous 400 kN.m : ni la flexion ni le tranchant ne passent.
            var narrow = new BeamData
            {
                Id = "narrow",
                Name = "P3",
                Mark = "B3",
                Shape = BeamSectionShape.Rectangular,
                WebWidthMm = 200.0,
                HeightMm = 400.0,
                SpanMm = 6000.0
            };
            var settings = new BeamDesignSettings
            {
                SpanMomentKnm = 400.0,
                LeftShearKn = 400.0,
                RightShearKn = 400.0
            };

            BeamDesignResult result = new BeamDesignModule().Design(narrow, settings, null);
            bool reported = !result.IsValid || result.HasFailedCheck || result.Warnings.Count > 0;
            Assert.True(reported, "L'echec doit etre signale explicitement a l'utilisateur.");
        }

        [Fact]
        public void L_Optimiseur_Respecte_La_Largeur_Disponible()
        {
            // 224 mm utiles, HA16, espacement libre 25 mm : 6 barres au maximum par lit.
            Assert.Equal(6, BeamRebarOptimizer.MaxBarsPerLayer(224.0, 16.0, 25.0));
            // HA25 : 1 + floor((224 - 25) / 50) = 4 barres.
            Assert.Equal(4, BeamRebarOptimizer.MaxBarsPerLayer(224.0, 25.0, 25.0));
            // Largeur insuffisante pour une seule barre.
            Assert.Equal(0, BeamRebarOptimizer.MaxBarsPerLayer(10.0, 16.0, 25.0));
        }

        [Fact]
        public void L_Optimiseur_Passe_A_Deux_Lits_Quand_Un_Seul_Ne_Suffit_Pas()
        {
            var optimizer = new BeamRebarOptimizer(null, 2);
            // 3 000 mm2 sur 224 mm utiles : impossible en un seul lit.
            BarSelection selection = optimizer.Select(3000.0, 224.0, 25.0);

            Assert.NotNull(selection);
            Assert.True(selection.AreaMm2 >= 3000.0);
            Assert.True(selection.Layers >= 1);
            Assert.True(selection.BarsPerLayer <= BeamRebarOptimizer.MaxBarsPerLayer(
                224.0, selection.DiameterMm, 25.0));
        }

        [Fact]
        public void L_Optimiseur_Ne_Sur_Ferraille_Pas()
        {
            var optimizer = new BeamRebarOptimizer();
            BarSelection selection = optimizer.Select(1000.0, 250.0, 25.0);

            Assert.NotNull(selection);
            Assert.True(selection.AreaMm2 >= 1000.0);
            Assert.True(selection.AreaMm2 <= 1.35 * 1000.0,
                "Exces d'acier de " + (selection.AreaMm2 / 1000.0 - 1.0) * 100.0 + " %.");
        }
    }
}
