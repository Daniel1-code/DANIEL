using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STAIR-01 : volee droite de 9 contremarches, paillasse 180 mm, palier participant.
    /// Fiche de validation : docs/validation/STAIR-01.md
    ///
    /// C25/30, B500, XC1 interieur. R = 170 mm, G = 280 mm, 9 contremarches donc 8 girons.
    ///   denivele 1 530 mm, projection 2 240 mm, cos alpha = 0,85479
    ///   palier 1 300 mm dans la portee -> L = 3 540 mm
    ///
    /// Charges  : volee  g = 25 x 0,180/0,85479 + 25 x 0,170/2 + 1,00 + 0,30/0,85479
    ///                     = 5,264 + 2,125 + 1,000 + 0,351 = 8,740 kN/m2
    ///            palier g = 25 x 0,180 + 1,00 + 0,30 = 5,800 kN/m2
    ///            q = 3,00 kN/m2
    ///            w1 = 1,35 x 8,740 + 1,5 x 3,00 = 16,300 kN/m2
    ///            w2 = 1,35 x 5,800 + 1,5 x 3,00 = 12,330 kN/m2
    /// Statique : R_bas = 27,903 kN/m, R_haut = 24,637 kN/m
    ///            tranchant nul a x = 1,712 m, M_Ed = 23,88 kN.m/m
    ///            (la charge de volee etalee partout aurait donne 25,53)
    /// Enrobage : XC1, geometrie de dalle -> classe S3 -> c_min,dur = 10 mm
    ///            c_min = max(12 ; 10 ; 10) = 12 -> c_nom = 22 mm ; d = 180 - 22 - 6 = 152 mm
    /// Flexion  : mu = 0,0620 -> x/d = 0,0801, z = 147,1 mm, As = 373 mm2/m
    ///            As,min = 0,26 x 2,5648/500 x 1 000 x 152 = 203 mm2/m, ne gouverne pas
    /// Noeud    : l_bd HA12 arrondi a 500 mm ; disponible min(1 300 palier ;
    ///            2 240 x 0,85479 = 1 914 paillasse) = 1 300 mm
    /// </summary>
    public class Stair01DesignTests
    {
        private static StairData Flight()
        {
            return new StairData
            {
                Id = "STAIR-01",
                Name = "V1",
                Mark = "V1",
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = 180.0,
                WidthMm = 1200.0,
                LandingThicknessMm = 180.0,
                LandingSpanMm = 1300.0,
                SpanKind = StairSpanKind.AlongFlightWithLanding
            };
        }

        private static StairDesignSettings Settings()
        {
            return new StairDesignSettings
            {
                ConcreteStrengthMPa = 25.0,
                SteelStrengthMPa = 500.0,
                TreadFinishKnM2 = 1.0,
                SoffitFinishKnM2 = 0.3,
                VariableLoadKnM2 = 3.0,
                IncludeSelfWeight = true,
                ConcreteUnitWeightKnM3 = 25.0,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true,
                TopReinforcement = true
            };
        }

        private static StairDesignResult Design()
        {
            return new StairDesignModule().Design(Flight(), Settings(), null);
        }

        [Fact]
        public void Design_Succeeds()
        {
            StairDesignResult result = Design();

            Assert.True(result.IsValid);
            Assert.False(result.HasFailedCheck);
        }

        [Fact]
        public void Le_Poids_Propre_Corrige_La_Pente_Et_Compte_Les_Marches()
        {
            StairDesignResult result = Design();

            Assert.Equal(8.740, result.FlightLoad.PermanentKnM2, 3);
            Assert.Equal(5.800, result.LandingLoad.PermanentKnM2, 3);
            // La volee pese nettement plus que le palier de meme epaisseur.
            Assert.True(result.FlightLoad.PermanentKnM2 > result.LandingLoad.PermanentKnM2 * 1.4);
        }

        [Fact]
        public void L_Erreur_Du_Calcul_Naif_Est_Annoncee()
        {
            StairDesignResult result = Design();

            Assert.Contains(result.Notes,
                n => n.Contains("gamma t comme poids propre") && n.Contains("sous-estime"));
        }

        [Fact]
        public void Les_Charges_ELU_Different_Entre_Volee_Et_Palier()
        {
            StairDesignResult result = Design();

            Assert.Equal(16.300, result.FlightUltimateLoadKnM2, 3);
            Assert.Equal(12.330, result.LandingUltimateLoadKnM2, 3);
        }

        [Fact]
        public void La_Statique_Suit_Le_Calcul_Manuel()
        {
            StairDesignResult result = Design();

            Assert.NotNull(result.Statics);
            Assert.Equal(27.903, result.Statics.ReactionLowerKnPerM, 2);
            Assert.Equal(24.637, result.Statics.ReactionUpperKnPerM, 2);
            Assert.Equal(1.712, result.Statics.CriticalPositionM, 3);
            Assert.Equal(23.88, result.SpanMomentKnmPerM, 2);
            Assert.Equal(27.90, result.ShearKnPerM, 2);
        }

        [Fact]
        public void L_Ecart_Avec_La_Charge_Etalee_Est_Rendu_Visible()
        {
            StairDesignResult result = Design();

            // 16,300 x 3,540^2 / 8 = 25,53 kN.m/m, soit 6,9 % de plus que le calcul exact.
            Assert.Equal(25.53, result.Statics.UniformFlightLoadMomentKnmPerM, 2);
            Assert.Contains(result.Notes, n => n.Contains("securitaire, mais faux"));
        }

        [Fact]
        public void L_Enrobage_Et_La_Hauteur_Utile_Suivent_La_Paillasse()
        {
            StairDesignResult result = Design();

            // La hauteur utile se mesure sur l'epaisseur perpendiculaire a la pente.
            Assert.Equal(22.0, result.Reinforcement.CoverMm, 1);
            Assert.Equal(152.0, result.Reinforcement.EffectiveDepthMm, 1);
        }

        [Fact]
        public void La_Section_Requise_Suit_Le_Calcul_Manuel()
        {
            StairDesignResult result = Design();

            Assert.InRange(result.SpanSteelRequiredMm2PerM, 365.0, 385.0);
            Assert.True(result.Reinforcement.BottomMain.AreaPerMetreMm2
                        >= result.SpanSteelRequiredMm2PerM);
        }

        [Fact]
        public void Le_Minimum_Ne_Gouverne_Pas_Sur_Cette_Volee()
        {
            StairDesignResult result = Design();
            CheckResult minimum = Find(result, "Section minimale");

            // 203 mm2/m contre 373 requis par la flexion.
            Assert.InRange(minimum.Demand.Value, 198.0, 208.0);
            Assert.Equal(CheckStatus.Pass, minimum.Status);
        }

        [Fact]
        public void Le_Noeud_Est_Un_Angle_Rentrant_Tendu()
        {
            StairDesignResult result = Design();

            Assert.Equal(KneeJointKind.ReentrantInTension, result.Reinforcement.KneeJoint);
            Assert.Contains(result.Notes, n => n.Contains("angle RENTRANT"));
            Assert.Contains(result.Notes, n => n.Contains("CROISEES"));
        }

        [Fact]
        public void L_Efficacite_Du_Noeud_N_Est_Pas_Calculee_Et_Le_Moteur_Le_Dit()
        {
            StairDesignResult result = Design();

            Assert.Contains(result.Warnings,
                w => w.Contains("EFFICACITE DU NOEUD N'EST PAS CALCULEE")
                     && w.Contains("bielles-tirants"));
        }

        [Fact]
        public void L_Ancrage_Au_Noeud_Est_Verifie_Contre_La_Longueur_Disponible()
        {
            StairDesignResult result = Design();
            CheckResult knee = Find(result, "Ancrage des barres croisees");

            // Disponible = min(1 300 cote palier ; 1 914 cote paillasse) = 1 300 mm
            Assert.Equal(1300.0, knee.Resistance.Value, 0);
            Assert.Equal(CheckStatus.Pass, knee.Status);
            Assert.Contains("la plus courte", knee.Comment);
        }

        [Fact]
        public void Les_Deux_Nappes_Inferieures_Se_Croisent_Au_Noeud()
        {
            StairDesignResult result = Design();

            RebarGroup flight = result.Plan.Groups
                .FirstOrDefault(g => g.Label.Contains("volee, croisee"));
            RebarGroup landing = result.Plan.Groups
                .FirstOrDefault(g => g.Label.Contains("palier, croisee"));

            Assert.NotNull(flight);
            Assert.NotNull(landing);

            // z du pli : 22 + 6 + 2 240 x 170/280 = 1 388 mm
            const double zJoint = 1388.0;

            // Chacune doit REMONTER au-dela du pli pour s'ancrer dans la face opposee.
            // Une barre qui suivrait le pli resterait a l'altitude de la sous-face.
            Assert.True(MaxZ(flight) > zJoint + 50.0,
                        "La barre de volee doit s'ancrer dans la face superieure du palier.");
            Assert.True(MaxZ(landing) > zJoint + 50.0,
                        "La barre de palier doit s'ancrer dans la face superieure de la volee.");
        }

        [Fact]
        public void La_Fleche_Passe_Sans_La_Majoration_De_La_BS_8110()
        {
            StairDesignResult result = Design();
            CheckResult deflection = Find(result, "Fleche");

            // l/d = 3 540 / 152 = 23,3
            Assert.Equal(23.29, deflection.Demand.Value, 1);
            Assert.Equal(CheckStatus.Pass, deflection.Status);
            Assert.Contains("BS 8110", deflection.Comment);
        }

        [Fact]
        public void L_Effort_Tranchant_Passe_Largement()
        {
            StairDesignResult result = Design();
            CheckResult shear = Find(result, "Effort tranchant");

            Assert.Equal(27.90, shear.Demand.Value, 1);
            Assert.True(shear.Utilization < 0.50);
        }

        [Fact]
        public void Le_Plan_Contient_Les_Deux_Nappes_Croisees_La_Repartition_Et_Les_Chapeaux()
        {
            StairDesignResult result = Design();

            // 2 nappes croisees + repartition inferieure + 2 chapeaux + repartition superieure
            Assert.Equal(6, result.Plan.Groups.Count);
            Assert.All(result.Plan.Groups, g => Assert.True(g.HasNormal));
            Assert.All(result.Plan.Groups, g => Assert.True(g.BarLengthMm > 0));
        }

        [Fact]
        public void Chaque_Verification_Porte_Sa_Clause()
        {
            StairDesignResult result = Design();

            Assert.NotEmpty(result.Checks);
            Assert.All(result.Checks, c =>
            {
                Assert.False(string.IsNullOrWhiteSpace(c.Code));
                Assert.False(string.IsNullOrWhiteSpace(c.Clause));
                Assert.False(string.IsNullOrWhiteSpace(c.Description));
            });
        }

        private static double MaxZ(RebarGroup group)
        {
            double max = double.MinValue;
            foreach (PlanSegment segment in group.Path)
            {
                if (segment.Start.Z > max) max = segment.Start.Z;
                if (segment.End.Z > max) max = segment.End.Z;
            }
            return max;
        }

        private static CheckResult Find(StairDesignResult result, string description)
        {
            CheckResult check = result.Checks
                .FirstOrDefault(c => c.Description.Contains(description));
            Assert.NotNull(check);
            return check;
        }
    }
}
