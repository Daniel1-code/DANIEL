using System.Linq;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Reinforcement.Plan;
using DanCI.Structural.Reinforcement.Schedule;
using Xunit;

namespace DanCI.Structural.Tests.Reinforcement
{
    /// <summary>
    /// Carnet de ferraillage : regroupement des formes identiques et longueurs de coupe.
    /// Fiche de validation : docs/validation/BBS-01.md
    /// </summary>
    public class BarScheduleTests
    {
        /// <summary>Une barre droite de longueur donnee, repetee n fois.</summary>
        private static RebarGroup Straight(double diameterMm, double lengthMm, int count,
                                           string label = "Nappe")
        {
            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = diameterMm,
                Label = label,
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, (count - 1) * 200.0)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, 0, 0),
                                            new LocalPoint(lengthMm, 0, 0)));
            return group;
        }

        /// <summary>Une barre en L : un segment horizontal puis un segment vertical.</summary>
        private static RebarGroup Elbow(double diameterMm, double aMm, double bMm, int count)
        {
            var group = new RebarGroup
            {
                Kind = RebarKind.Longitudinal,
                DiameterMm = diameterMm,
                Label = "Attente en L",
                Layout = count > 1
                    ? ArrayLayout.FixedNumber(LocalVector.AxisY, count, (count - 1) * 200.0)
                    : ArrayLayout.Single()
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, 0, 0), new LocalPoint(aMm, 0, 0)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(aMm, 0, 0),
                                            new LocalPoint(aMm, 0, bMm)));
            return group;
        }

        /// <summary>Un cadre rectangulaire ferme.</summary>
        private static RebarGroup Stirrup(double diameterMm, double widthMm, double heightMm,
                                          int count)
        {
            var group = new RebarGroup
            {
                Kind = RebarKind.Stirrup,
                DiameterMm = diameterMm,
                Label = "Cadre",
                IsClosedLoop = true,
                WithHooks = true,
                Layout = ArrayLayout.FixedNumber(LocalVector.AxisX, count, (count - 1) * 200.0)
            };
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, 0, 0),
                                            new LocalPoint(0, widthMm, 0)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, widthMm, 0),
                                            new LocalPoint(0, widthMm, heightMm)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, widthMm, heightMm),
                                            new LocalPoint(0, 0, heightMm)));
            group.Path.Add(PlanSegment.Line(new LocalPoint(0, 0, heightMm),
                                            new LocalPoint(0, 0, 0)));
            return group;
        }

        private static ReinforcementPlan PlanOf(params RebarGroup[] groups)
        {
            var plan = new ReinforcementPlan();
            foreach (RebarGroup group in groups) plan.Add(group);
            return plan;
        }

        // ------------------------------------------------------------------
        // Les angles de pli sortent du trajet reel
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Barre_Droite_N_A_Aucun_Pli()
        {
            Assert.Empty(Straight(12.0, 4000.0, 1).BendAnglesDegrees());
        }

        [Fact]
        public void Une_Barre_En_L_A_Un_Pli_A_90_Degres()
        {
            var angles = Elbow(16.0, 1000.0, 400.0, 1).BendAnglesDegrees();

            Assert.Single(angles);
            Assert.Equal(90.0, angles[0], 3);
        }

        [Fact]
        public void Un_Cadre_Ferme_A_Quatre_Plis_Pas_Trois()
        {
            // Le retour au point de depart est un pli, lui aussi. L'oublier sous-estime la
            // deduction d'un quart.
            var angles = Stirrup(8.0, 300.0, 500.0, 10).BendAnglesDegrees();

            Assert.Equal(4, angles.Count);
            Assert.All(angles, a => Assert.Equal(90.0, a, 3));
        }

        // ------------------------------------------------------------------
        // Le regroupement
        // ------------------------------------------------------------------

        [Fact]
        public void Deux_Barres_Identiques_Dans_Deux_Elements_Partagent_Un_Repere()
        {
            // C'EST LA RAISON D'ETRE DU CARNET. Jusqu'ici chaque element numerotait pour
            // lui-meme : le facconnier recevait deux fois la meme forme sous deux reperes.
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Stirrup(8.0, 300.0, 500.0, 20))),
                new ScheduledElement("P2", PlanOf(Stirrup(8.0, 300.0, 500.0, 15)))
            });

            Assert.Single(schedule.Rows);
            Assert.Equal(35, schedule.Rows[0].Count);
            Assert.Equal(2, schedule.Rows[0].Uses.Count);
            Assert.Contains(schedule.Rows[0].Uses, u => u.ElementName == "P1" && u.Count == 20);
            Assert.Contains(schedule.Rows[0].Uses, u => u.ElementName == "P2" && u.Count == 15);
        }

        [Fact]
        public void Un_Millimetre_D_Ecart_Ne_Fait_Pas_Deux_Formes()
        {
            // La tolerance de regroupement vaut le millimetre : c'est la precision du
            // faconnage, pas celle du calcul.
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Straight(12.0, 4000.0, 4))),
                new ScheduledElement("P2", PlanOf(Straight(12.0, 4000.3, 4)))
            });

            Assert.Single(schedule.Rows);
            Assert.Equal(8, schedule.Rows[0].Count);
        }

        [Fact]
        public void Un_Diametre_Different_Fait_Deux_Formes()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Straight(12.0, 4000.0, 4),
                                                  Straight(16.0, 4000.0, 4)))
            });

            Assert.Equal(2, schedule.Rows.Count);
        }

        [Fact]
        public void Une_Longueur_Franchement_Differente_Fait_Deux_Formes()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Straight(12.0, 4000.0, 4),
                                                  Straight(12.0, 3000.0, 4)))
            });

            Assert.Equal(2, schedule.Rows.Count);
        }

        // ------------------------------------------------------------------
        // Le repere et le tri
        // ------------------------------------------------------------------

        [Fact]
        public void Le_Carnet_Est_Trie_Par_Diametre_Puis_Par_Longueur_Decroissante()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(
                    Straight(16.0, 2000.0, 2),
                    Straight(8.0, 1000.0, 2),
                    Straight(8.0, 5000.0, 2),
                    Straight(12.0, 3000.0, 2)))
            });

            Assert.Equal(new[] { 8.0, 8.0, 12.0, 16.0 },
                         schedule.Rows.Select(r => r.DiameterMm).ToArray());
            // A diametre egal, la plus longue d'abord.
            Assert.Equal(5000.0, schedule.Rows[0].CutLengthMm, 1);
            Assert.Equal(1000.0, schedule.Rows[1].CutLengthMm, 1);
        }

        [Fact]
        public void Le_Repere_Suit_L_Ordre_Du_Carnet()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Straight(16.0, 2000.0, 2),
                                                  Straight(8.0, 1000.0, 2)))
            }, "R");

            Assert.Equal("R01", schedule.Rows[0].Mark);
            Assert.Equal("R02", schedule.Rows[1].Mark);
            Assert.Equal(8.0, schedule.Rows[0].DiameterMm, 6);
        }

        // ------------------------------------------------------------------
        // La longueur de coupe
        // ------------------------------------------------------------------

        [Fact]
        public void La_Coupe_D_Un_Cadre_Deduit_Ses_Quatre_Plis()
        {
            // Perimetre 2 x (300 + 500) = 1 600 ; 4 plis HA8 = 34,34 ; crochets 160
            // Coupe = 1 725,7 mm
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Stirrup(8.0, 300.0, 500.0, 10)))
            });
            BarScheduleRow row = schedule.Rows[0];

            Assert.Equal(1600.0, row.PolylineLengthMm, 1);
            Assert.Equal(34.34, row.BendDeductionMm, 1);
            Assert.Equal(160.0, row.HookAllowanceMm, 1);
            Assert.Equal(1725.7, row.CutLengthMm, 1);
            Assert.Equal(32.0, row.MandrelDiameterMm, 6);
        }

        [Fact]
        public void La_Coupe_D_Une_Attente_En_L_Deduit_Son_Pli()
        {
            // 1 000 + 400 = 1 400 d'angle a angle ; un pli HA20 a 90 degres = 34,34 mm
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("S1", PlanOf(Elbow(20.0, 1000.0, 400.0, 6)))
            });
            BarScheduleRow row = schedule.Rows[0];

            Assert.Equal(1400.0, row.PolylineLengthMm, 1);
            Assert.Equal(34.34, row.BendDeductionMm, 1);
            Assert.Equal(1365.66, row.CutLengthMm, 1);
            // 7 phi au-dela de 16 mm.
            Assert.Equal(140.0, row.MandrelDiameterMm, 6);
        }

        [Fact]
        public void La_Masse_Suit_La_Coupe_Pas_Le_Developpe()
        {
            // 10 cadres de 1 725,66 mm en HA8 a 0,3946 kg/m = 6,81 kg
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Stirrup(8.0, 300.0, 500.0, 10)))
            });

            Assert.Equal(17.257, schedule.TotalLengthM, 2);
            Assert.Equal(6.81, schedule.TotalMassKg, 2);
        }

        [Theory]
        [InlineData(8.0, 0.3946)]
        [InlineData(12.0, 0.8878)]
        [InlineData(20.0, 2.4662)]
        public void La_Masse_Lineique_Est_Celle_De_L_Acier(double diameter, double expected)
        {
            Assert.Equal(expected, BarScheduleRow.MassPerMetreKgM(diameter), 4);
        }

        // ------------------------------------------------------------------
        // Lisibilite du carnet
        // ------------------------------------------------------------------

        [Fact]
        public void Chaque_Ligne_Nomme_Sa_Forme_Et_Ses_Cotes()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(
                    Stirrup(8.0, 300.0, 500.0, 10),
                    Elbow(20.0, 1000.0, 400.0, 6),
                    Straight(12.0, 4000.0, 4)))
            });

            Assert.Contains(schedule.Rows, r => r.ShapeLabel == "cadre ferme");
            Assert.Contains(schedule.Rows, r => r.ShapeLabel == "pliee en L");
            Assert.Contains(schedule.Rows, r => r.ShapeLabel == "droite");

            BarScheduleRow elbow = schedule.Rows.First(r => r.ShapeLabel == "pliee en L");
            Assert.Equal("1000 x 400", elbow.DimensionsLabel);
        }

        [Fact]
        public void Un_Carnet_Vide_Ne_Fait_Pas_Echouer_Le_Constructeur()
        {
            Assert.Empty(BarScheduleBuilder.Build(null).Rows);
            Assert.Empty(BarScheduleBuilder.Build(new ScheduledElement[0]).Rows);
            Assert.Empty(BarScheduleBuilder.Build(
                new[] { new ScheduledElement("P1", null) }).Rows);
        }

        [Fact]
        public void Le_Quantitatif_Compte_La_Coupe_Et_Non_Le_Developpe()
        {
            // NON-REGRESSION D'UNE CORRECTION. Jusqu'a la 3.10.0 incluse, le quantitatif
            // sommait les segments d'angle a angle : il commandait 2,3 % d'acier de trop
            // sur un cadre. Il doit desormais rendre exactement la meme masse que le
            // carnet, qui deduit les plis.
            ReinforcementPlan plan = PlanOf(Stirrup(8.0, 300.0, 500.0, 10),
                                            Elbow(20.0, 1000.0, 400.0, 6),
                                            Straight(12.0, 4000.0, 4));

            var quantities = DanCI.Structural.Documentation.Quantities.QuantityCalculator
                .Compute(1.0, plan);
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", plan)
            });

            Assert.Equal(schedule.TotalMassKg, quantities.TotalMassKg, 3);
        }

        [Fact]
        public void Le_Developpe_Reste_Superieur_A_La_Coupe_Des_Qu_Il_Y_A_Un_Pli()
        {
            // Le sens de la correction : la barre coupe le coin, donc la coupe est plus
            // COURTE que le developpe. Une correction qui irait dans l'autre sens serait
            // le signe d'une erreur de signe.
            ReinforcementPlan plan = PlanOf(Stirrup(8.0, 300.0, 500.0, 10));
            BarScheduleRow row = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", plan)
            }).Rows[0];

            Assert.True(row.CutLengthMm < row.PolylineLengthMm + row.HookAllowanceMm);
        }

        [Fact]
        public void La_Masse_Par_Diametre_Est_Rendue_Dans_L_Ordre()
        {
            BarSchedule schedule = BarScheduleBuilder.Build(new[]
            {
                new ScheduledElement("P1", PlanOf(Straight(16.0, 2000.0, 2),
                                                  Straight(8.0, 1000.0, 2),
                                                  Straight(12.0, 3000.0, 2)))
            });

            Assert.Equal(new[] { 8.0, 12.0, 16.0 },
                         schedule.MassByDiameter().Select(p => p.Key).ToArray());
        }
    }
}
