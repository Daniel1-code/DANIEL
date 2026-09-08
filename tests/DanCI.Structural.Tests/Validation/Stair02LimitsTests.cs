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
    /// STAIR-02 : ce qui gouverne reellement une volee, et ce que le moteur refuse de faire.
    /// Fiche de validation : docs/validation/STAIR-02.md
    /// </summary>
    public class Stair02LimitsTests
    {
        private static StairData Flight(double waistMm = 180.0)
        {
            return new StairData
            {
                Name = "V2",
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = waistMm,
                WidthMm = 1200.0,
                LandingThicknessMm = waistMm,
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
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true,
                TopReinforcement = true
            };
        }

        private static StairDesignResult Design(StairData stair, StairDesignSettings settings)
        {
            return new StairDesignModule().Design(stair, settings, null);
        }

        // ------------------------------------------------------------------
        // a. C'est la fleche qui decide de l'epaisseur
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Paillasse_Trop_Mince_Echoue_Par_La_Fleche_Pas_Par_La_Resistance()
        {
            // 120 mm sur 3,54 m : d = 92 mm, l/d = 38,5 pour une limite de l'ordre de 25.
            StairDesignResult result = Design(Flight(120.0), Settings());

            CheckResult deflection = Find(result, "Fleche");
            Assert.Equal(CheckStatus.Fail, deflection.Status);

            // La flexion, elle, reste satisfaite : ce n'est pas la resistance qui manque.
            Assert.Equal(CheckStatus.Pass, Find(result, "Flexion en travee").Status);

            Assert.Contains(result.Warnings,
                w => w.Contains("FLECHE") && w.Contains("epaissir la paillasse"));
        }

        [Fact]
        public void Epaissir_La_Paillasse_Ameliore_La_Fleche_Malgre_Le_Poids_Propre()
        {
            // Epaissir alourdit la volee, donc augmente le moment. La fleche s'ameliore
            // quand meme, et nettement : c'est pour cela que c'est l'action utile.
            StairDesignResult mince = Design(Flight(120.0), Settings());
            StairDesignResult epais = Design(Flight(180.0), Settings());

            Assert.True(epais.SpanMomentKnmPerM > mince.SpanMomentKnmPerM);
            Assert.True(Find(epais, "Fleche").Utilization < Find(mince, "Fleche").Utilization);
        }

        // ------------------------------------------------------------------
        // b. Le noeud n'existe pas toujours
        // ------------------------------------------------------------------

        [Fact]
        public void Sans_Palier_Dans_La_Portee_Il_N_Y_A_Pas_De_Noeud()
        {
            StairData stair = Flight();
            stair.SpanKind = StairSpanKind.AlongFlightOnly;

            StairDesignResult result = Design(stair, Settings());
            CheckResult knee = Find(result, "Ancrage des barres croisees");

            Assert.Equal(2240.0, stair.SpanMm, 6);
            Assert.Equal(KneeJointKind.None, result.Reinforcement.KneeJoint);
            // Sans objet declare, plutot qu'omis.
            Assert.Equal(CheckStatus.NotApplicable, knee.Status);
            Assert.Equal(0.0, knee.Utilization, 6);
        }

        [Fact]
        public void Une_Portee_Transversale_Ne_Traverse_Pas_Le_Pli()
        {
            StairData stair = Flight();
            stair.SpanKind = StairSpanKind.TransverseBetweenWalls;

            StairDesignResult result = Design(stair, Settings());

            // La portee devient la largeur de la volee.
            Assert.Equal(1200.0, stair.SpanMm, 6);
            Assert.Equal(KneeJointKind.None, result.Reinforcement.KneeJoint);
            Assert.Contains(result.Notes,
                n => n.Contains("Portee transversale") && n.Contains("pas de noeud"));
            // Sous la seule charge de volee, par la statique.
            Assert.Null(result.Statics);
        }

        [Fact]
        public void Un_Palier_Trop_Court_Ne_Laisse_Pas_Ancrer_Les_Barres_Croisees()
        {
            StairData stair = Flight();
            stair.LandingSpanMm = 300.0;

            StairDesignResult result = Design(stair, Settings());
            CheckResult knee = Find(result, "Ancrage des barres croisees");

            Assert.Equal(300.0, knee.Resistance.Value, 0);
            Assert.Equal(CheckStatus.Fail, knee.Status);
            Assert.Contains(result.Warnings,
                w => w.Contains("ANCRAGE AU NOEUD INSUFFISANT")
                     && w.Contains("jamais la longueur d'ancrage"));
        }

        [Fact]
        public void Aucune_Barre_Ne_Suit_Jamais_Le_Pli()
        {
            // L'invariant du module : les nappes inferieures remontent toujours au-dela du
            // pli. Aucun reglage ne produit une barre qui le suivrait.
            foreach (double landing in new[] { 300.0, 800.0, 1300.0, 2000.0 })
            {
                StairData stair = Flight();
                stair.LandingSpanMm = landing;
                StairDesignResult result = Design(stair, Settings());

                RebarGroup flight = result.Plan.Groups
                    .FirstOrDefault(g => g.Label.Contains("volee, croisee"));
                Assert.NotNull(flight);

                double zJoint = result.Reinforcement.CoverMm
                                + flight.DiameterMm / 2.0
                                + stair.TotalGoingMm * stair.SlopeTangent;
                Assert.True(MaxZ(flight) > zJoint,
                            "Palier de " + landing + " mm : la barre suit le pli.");
            }
        }

        // ------------------------------------------------------------------
        // c. La geometrie de la marche
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Pente_Excessive_Est_Signalee_Sans_Faire_Echouer_Le_Calcul()
        {
            // R = 200, G = 240 -> 39,8 degres
            StairData stair = Flight();
            stair.RiserHeightMm = 200.0;
            stair.TreadDepthMm = 240.0;

            StairDesignResult result = Design(stair, Settings());

            Assert.Contains(result.Warnings,
                w => w.Contains("pente atteint") && w.Contains("reglementation"));
            // C'est un avertissement de confort : aucune verification de resistance n'echoue.
            Assert.False(result.HasFailedCheck);
        }

        [Fact]
        public void Blondel_Est_Rendue_Comme_Une_Regle_D_Ergonomie()
        {
            StairData stair = Flight();
            stair.RiserHeightMm = 200.0;
            stair.TreadDepthMm = 240.0;   // 2R + G = 640, dans la plage

            StairDesignResult result = Design(stair, Settings());

            Assert.Contains(result.Notes,
                n => n.Contains("Blondel") && n.Contains("ERGONOMIE")
                     && n.Contains("aucun Eurocode"));
        }

        [Fact]
        public void Une_Volee_D_Une_Seule_Contremarche_N_A_Pas_De_Portee()
        {
            StairData stair = Flight();
            stair.RiserCount = 1;

            StairDesignResult result = Design(stair, Settings());

            Assert.Equal(0.0, stair.TotalGoingMm, 6);
            Assert.Contains(result.Warnings, w => w.Contains("moins de deux contremarches"));
        }

        // ------------------------------------------------------------------
        // d. Ce que le moteur ne fait pas
        // ------------------------------------------------------------------

        [Fact]
        public void La_Charge_Concentree_Est_Verifiee_Et_Non_Plus_Seulement_Rappelee()
        {
            // Jusqu'a la 3.8.0 le moteur citait l'article et affirmait que la charge
            // repartie gouverne. Il le VERIFIE desormais : voir STAIR-03.
            StairDesignSettings settings = Settings();
            settings.ConcentratedLoadKn = 2.0;

            StairDesignResult result = Design(Flight(), settings);

            Assert.Contains(result.Notes,
                n => n.Contains("6.3.1.2(1)") && n.Contains("ALTERNATIVE"));
            Assert.True(result.ConcentratedLoadMomentKnmPerM > 0);
            Assert.Contains(result.Checks,
                c => c.Description.Contains("Situation alternative a charge concentree"));
        }

        [Fact]
        public void La_Categorie_D_Usage_Est_Celle_De_La_Zone_Desservie()
        {
            StairDesignResult result = Design(Flight(), Settings());

            Assert.Contains(result.Notes,
                n => n.Contains("6.3.1(1)") && n.Contains("zone qu'il dessert"));
        }

        [Fact]
        public void Le_Moment_Entier_Est_Retenu_Plutot_Que_Sa_Composante()
        {
            StairDesignResult result = Design(Flight(), Settings());

            Assert.Contains(result.Notes,
                n => n.Contains("M cos alpha") && n.Contains("securitaire"));
        }

        [Fact]
        public void Des_Sollicitations_Saisies_Ne_Sont_Pas_Recalculees()
        {
            StairDesignSettings settings = Settings();
            settings.MomentSource = StairMomentSource.Entered;
            settings.SpanMomentKnmPerM = 40.0;
            settings.ShearKnPerM = 45.0;

            StairDesignResult result = Design(Flight(), settings);

            Assert.Equal(40.0, result.SpanMomentKnmPerM, 6);
            Assert.Equal(45.0, result.ShearKnPerM, 6);
            Assert.Null(result.Statics);
            Assert.Contains(result.Notes, n => n.Contains("analyse exterieure"));
        }

        [Fact]
        public void Les_Chapeaux_Sont_Poses_Meme_Sans_Moment_Sur_Appui_Declare()
        {
            // Une volee dite isostatique est toujours partiellement encastree dans ses
            // paliers : sans chapeaux, la fissuration se declare en face superieure.
            StairDesignResult result = Design(Flight(), Settings());

            Assert.Equal(0.0, result.SupportMomentKnmPerM, 6);
            Assert.True(result.Reinforcement.HasTopReinforcement);
            Assert.Contains(result.Notes, n => n.Contains("partiellement encastree"));

            // L'origine de la longueur retenue est desormais portee par la DECISION, pas
            // par la note : c'est la que l'ingenieur va la lire et la changer (STAIR-05).
            Assert.Contains(result.Decisions,
                d => d.Question.Contains("Longueur des chapeaux")
                     && d.Reason.Contains("PRATIQUE COURANTE"));
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
