using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STAIR-05 : le ferraillage est CONÇU sur des parametres, pas applique depuis une
    /// disposition figee.
    /// Fiche de validation : docs/validation/STAIR-05.md
    ///
    /// Chaque test verifie qu'un parametre CHANGE REELLEMENT LE PLAN. Un reglage qu'on
    /// peut modifier sans que rien ne bouge n'est pas un parametre, c'est un champ mort.
    /// </summary>
    public class Stair05DetailingTests
    {
        private static StairData Flight(double landingMm = 1300.0)
        {
            return new StairData
            {
                Name = "V5",
                RiserHeightMm = 170.0,
                TreadDepthMm = 280.0,
                RiserCount = 9,
                WaistThicknessMm = 180.0,
                WidthMm = 1200.0,
                LandingThicknessMm = 180.0,
                LandingSpanMm = landingMm,
                SpanKind = landingMm > 0
                    ? StairSpanKind.AlongFlightWithLanding
                    : StairSpanKind.AlongFlightOnly,
                Shape = StairFlightShape.Straight
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
                ConcentratedLoadKn = 2.0,
                IncludeSelfWeight = true,
                AutoCover = true,
                Exposure = ExposureClass.XC1,
                AutoMeshDiameter = true,
                TopReinforcement = true
            };
        }

        private static StairDesignResult Design(StairDesignSettings settings,
                                                double landingMm = 1300.0)
        {
            return new StairDesignModule().Design(Flight(landingMm), settings, null);
        }

        // ------------------------------------------------------------------
        // Chaque decision sort avec sa raison
        // ------------------------------------------------------------------

        [Fact]
        public void Chaque_Decision_Porte_Sa_Raison()
        {
            // Une longueur sans sa raison n'est pas un parametre, c'est un nombre magique.
            StairDesignResult result = Design(Settings());

            Assert.NotEmpty(result.Decisions);
            Assert.All(result.Decisions, d =>
            {
                Assert.False(string.IsNullOrWhiteSpace(d.Question));
                Assert.False(string.IsNullOrWhiteSpace(d.Reason));
            });
        }

        [Fact]
        public void La_Fraction_De_Portee_Des_Chapeaux_Est_Annoncee_Hors_Eurocode()
        {
            StairDesignResult result = Design(Settings());
            DetailingDecision top = Find(result, "Longueur des chapeaux");

            Assert.Contains("PRATIQUE COURANTE", top.Reason);
            Assert.Contains("enveloppe de moments", top.Reason);
        }

        // ------------------------------------------------------------------
        // Les chapeaux
        // ------------------------------------------------------------------

        [Fact]
        public void Le_Mode_Par_Defaut_Retient_Le_Plus_Grand_De_L_Sur_4_Et_De_l_bd()
        {
            // L = 3 540 mm, L/4 = 885 mm ; l_bd HA12 vaut moins : c'est L/4 qui gouverne,
            // arrondi au pas de 50 mm.
            StairDesignResult result = Design(Settings());

            Assert.Equal(900.0, result.Reinforcement.TopBarLengthMm, 0);
        }

        [Fact]
        public void Changer_La_Fraction_Change_La_Longueur_Des_Chapeaux()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.TopBarSpanFraction = 0.4;

            // 0,40 x 3 540 = 1 416 -> 1 450 mm au pas de 50.
            Assert.Equal(1450.0, Design(settings).Reinforcement.TopBarLengthMm, 0);
        }

        [Fact]
        public void Le_Mode_Ancrage_Seul_Donne_La_Longueur_La_Plus_Courte()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.TopBarExtent = TopBarExtentMode.AnchorageOnly;

            StairDesignResult reduced = Design(settings);
            StairDesignResult standard = Design(Settings());

            Assert.True(reduced.Reinforcement.TopBarLengthMm
                        < standard.Reinforcement.TopBarLengthMm);

            // « Le minimum defendable » etait l_bd, et c'etait FAUX : l'art. 9.3.1.2(2)
            // impose 0,2 l des lors que l'encastrement partiel n'est pas pris en compte
            // dans l'analyse. Le mode reste le plus court des quatre, mais son plancher
            // n'est plus l'ancrage — c'est l'article, et la raison le dit.
            Assert.True(reduced.Reinforcement.TopBarLengthMm
                        >= 0.2 * Flight().SpanMm - 1.0,
                        "Meme le mode le plus court reste au-dessus de 0,2 l.");
            Assert.Contains("9.3.1.2(2)", Find(reduced, "Longueur des chapeaux").Reason);
        }

        [Fact]
        public void Une_Longueur_Imposee_Est_Appliquee_Et_Non_Justifiee()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.TopBarExtent = TopBarExtentMode.Fixed;
            settings.Detailing.TopBarFixedLengthMm = 1200.0;

            StairDesignResult result = Design(settings);

            Assert.Equal(1200.0, result.Reinforcement.TopBarLengthMm, 0);
            Assert.Contains("ne la justifie pas",
                            Find(result, "Longueur des chapeaux").Reason);
        }

        [Fact]
        public void La_Nappe_Superieure_Continue_Remplace_Les_Deux_Chapeaux()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.TopBarExtent = TopBarExtentMode.FullSpan;

            StairDesignResult result = Design(settings);

            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("Nappe superieure continue"));
            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("Chapeau appui bas"));
            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("Chapeau appui haut"));
        }

        [Fact]
        public void La_Nappe_Superieure_Continue_Reste_Dans_Le_Beton()
        {
            // Un parametre qui produirait des barres hors du beton serait pire qu'un
            // parametre absent.
            StairDesignSettings settings = Settings();
            settings.Detailing.TopBarExtent = TopBarExtentMode.FullSpan;

            AssertPlanIsInsideConcrete(Flight(), Design(settings));
        }

        // ------------------------------------------------------------------
        // Le noeud
        // ------------------------------------------------------------------

        [Fact]
        public void Le_Detail_Par_Defaut_Croise_Les_Nappes()
        {
            StairDesignResult result = Design(Settings());

            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("croisee au noeud"));
            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("Epingle diagonale"));
            Assert.Contains("CROISEES", Find(result, "Detail du noeud").Reason);
        }

        [Fact]
        public void L_Epingle_Diagonale_Arrete_Les_Nappes_Et_Ajoute_Une_Barre()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.KneeJoint = KneeJointDetail.SeparateHairpin;

            StairDesignResult result = Design(settings);

            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("arretee au noeud"));
            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("Epingle diagonale"));
            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("croisee au noeud"));
            Assert.Contains("EPINGLE DIAGONALE", Find(result, "Detail du noeud").Reason);
        }

        [Fact]
        public void L_Epingle_Diagonale_Reste_Dans_Le_Beton()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.KneeJoint = KneeJointDetail.SeparateHairpin;

            AssertPlanIsInsideConcrete(Flight(), Design(settings));
        }

        [Fact]
        public void Majorer_L_Ancrage_Au_Noeud_Est_Possible_Le_Reduire_Ne_L_Est_Pas()
        {
            StairDesignSettings generous = Settings();
            generous.Detailing.KneeAnchorageFactor = 1.5;
            StairDesignResult result = Design(generous);
            Assert.True(result.Reinforcement.KneeAnchorageMm
                        > Design(Settings()).Reinforcement.KneeAnchorageMm);

            // Reduire l'ancrage de calcul n'est pas un reglage : c'est une faute, et le
            // moteur la refuse au lieu de l'appliquer.
            StairDesignSettings reduced = Settings();
            reduced.Detailing.KneeAnchorageFactor = 0.7;
            Assert.Contains(reduced.Validate(),
                            e => e.Contains("pas un reglage, c'est une faute"));
        }

        // ------------------------------------------------------------------
        // L'ordre des nappes
        // ------------------------------------------------------------------

        [Fact]
        public void La_Repartition_Se_Pose_Au_Dessus_Des_Porteuses_Par_Defaut()
        {
            StairDesignResult result = Design(Settings());

            RebarGroup main = result.Plan.Groups.First(g => g.Label.Contains("volee, croisee"));
            RebarGroup distribution = result.Plan.Groups
                .First(g => g.Label.Contains("Repartition inferieure, volee"));

            // Au droit d'une meme abscisse, la repartition est plus haute que la porteuse.
            Assert.True(distribution.Path[0].Start.Z > main.Path[0].Start.Z);
        }

        [Fact]
        public void Inverser_L_Ordre_Des_Nappes_Descend_La_Repartition()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.DistributionAboveMainBars = false;

            StairDesignResult inverted = Design(settings);
            StairDesignResult standard = Design(Settings());

            double zInverted = inverted.Plan.Groups
                .First(g => g.Label.Contains("Repartition inferieure, volee")).Path[0].Start.Z;
            double zStandard = standard.Plan.Groups
                .First(g => g.Label.Contains("Repartition inferieure, volee")).Path[0].Start.Z;

            Assert.True(zInverted < zStandard);
            Assert.Contains("hauteur utile des porteuses en est reduite",
                            Find(inverted, "Position de la repartition").Reason);
        }

        // ------------------------------------------------------------------
        // Arrondis et barres longues
        // ------------------------------------------------------------------

        [Fact]
        public void Le_Pas_D_Arrondi_S_Applique_Aux_Longueurs_Faconnees()
        {
            StairDesignSettings coarse = Settings();
            coarse.Detailing.LengthRoundingMm = 100.0;

            double length = Design(coarse).Reinforcement.TopBarLengthMm;

            Assert.Equal(0.0, length % 100.0, 6);
        }

        [Fact]
        public void Une_Barre_Plus_Longue_Que_Le_Stock_Est_Signalee_Pas_Decoupee()
        {
            // Decouper en silence mettrait tous les recouvrements au meme endroit.
            StairDesignSettings settings = Settings();
            settings.Detailing.StockLengthMm = 3000.0;

            StairDesignResult result = Design(settings);

            Assert.Contains(result.Warnings,
                w => w.Contains("PLUS LONGUE QUE LE STOCK") && w.Contains("quinconce"));
        }

        [Fact]
        public void Une_Longueur_De_Stock_Absurde_Est_Refusee()
        {
            StairDesignSettings settings = Settings();
            settings.Detailing.StockLengthMm = 500.0;

            Assert.Contains(settings.Validate(), e => e.Contains("entre 3 et 18 m"));
        }

        // ------------------------------------------------------------------
        // L'identite du calcul
        // ------------------------------------------------------------------

        [Fact]
        public void Deux_Dispositions_Differentes_Ne_Sont_Pas_Le_Meme_Calcul()
        {
            // L'empreinte doit changer : sinon relancer la commande croirait retrouver le
            // meme ferraillage alors qu'il a change.
            StairDesignSettings a = Settings();
            StairDesignSettings b = Settings();
            b.Detailing.KneeJoint = KneeJointDetail.SeparateHairpin;

            Assert.NotEqual(StairDesignFingerprint.Compute(Flight(), a),
                            StairDesignFingerprint.Compute(Flight(), b));
        }

        [Fact]
        public void Cloner_Les_Reglages_Ne_Partage_Pas_Les_Dispositions()
        {
            StairDesignSettings original = Settings();
            StairDesignSettings copy = original.Clone();
            copy.Detailing.TopBarSpanFraction = 0.5;

            Assert.Equal(0.25, original.Detailing.TopBarSpanFraction, 6);
        }

        // ------------------------------------------------------------------
        // Outils
        // ------------------------------------------------------------------

        private static void AssertPlanIsInsideConcrete(StairData stair, StairDesignResult result)
        {
            const double tolerance = 0.5;
            Assert.True(result.IsValid);

            foreach (RebarGroup group in result.Plan.Groups)
            {
                foreach (PlanSegment segment in group.Path)
                {
                    foreach (LocalPoint p in new[] { segment.Start, segment.End })
                    {
                        double soffit = Math.Min(Math.Max(p.X, 0.0), stair.TotalGoingMm)
                                        * stair.SlopeTangent;
                        double top = p.X <= stair.TotalGoingMm
                            ? p.X * stair.SlopeTangent
                              + stair.WaistThicknessMm / stair.SlopeCosine
                            : stair.TotalGoingMm * stair.SlopeTangent
                              + stair.LandingThicknessMm;

                        Assert.True(p.Z >= soffit - tolerance && p.Z <= top + tolerance,
                            string.Format("{0} : {1} hors du beton [{2:0.0} ; {3:0.0}]",
                                          group.Label, p, soffit, top));
                    }
                }
            }
        }

        private static DetailingDecision Find(StairDesignResult result, string question)
        {
            DetailingDecision decision = result.Decisions
                .FirstOrDefault(d => d.Question.Contains(question));
            Assert.NotNull(decision);
            return decision;
        }
    }
}
