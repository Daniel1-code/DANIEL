using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Geometry;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Engine.Stair;
using DanCI.Structural.Reinforcement.Plan;
using Xunit;

namespace DanCI.Structural.Tests.Validation
{
    /// <summary>
    /// STAIR-06 : la geometrie calculee est celle de l'escalier DESSINE.
    /// Fiche de validation : docs/validation/STAIR-06.md
    ///
    /// LE DEFAUT REMONTE DE REVIT. Le lecteur ne lisait pas les paliers. La valeur par
    /// defaut — un palier de 1 300 mm participant a la portee — survivait donc a la
    /// lecture, sur TOUT escalier, y compris ceux qui n'en ont aucun. Consequences, toutes
    /// verifiees ici :
    ///
    /// 1. la portee etait majoree de 1 300 mm, donc le moment de deux tiers ;
    /// 2. un ferraillage de palier etait pose la ou il n'y a pas de beton — les barres
    ///    visibles en l'air au-dela de la derniere marche ;
    /// 3. la fleche etait declaree en defaut avec un taux qui n'etait pas celui de
    ///    l'escalier reel.
    ///
    /// Ces tests ne peuvent pas ouvrir Revit. Ils verifient donc les DEUX CHOSES QUI
    /// RESTENT : que le moteur tire les bonnes conclusions d'une geometrie sans palier, et
    /// qu'il DIT lesquelles de ses valeurs il n'a pas lues. La lecture elle-meme est du
    /// ressort du couple StairReader / Revit, et seule une execution reelle la valide.
    /// </summary>
    public class Stair06GeometrySourceTests
    {
        // La volee de l'utilisateur, relevee sur sa fenetre : 18 CM de 174 mm, giron
        // 250 mm, paillasse 150 mm. C'est le cas qui a produit les barres en l'air.
        private static StairData RealFlight(double landingMm)
        {
            return new StairData
            {
                Name = "Escalier prefabrique",
                RiserHeightMm = 174.0,
                TreadDepthMm = 250.0,
                RiserCount = 18,
                WaistThicknessMm = 150.0,
                WidthMm = 1200.0,
                LandingThicknessMm = 150.0,
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

        private static StairDesignResult Design(StairData stair)
        {
            return new StairDesignModule().Design(stair, Settings(), null);
        }

        // ------------------------------------------------------------------
        // Ce que le palier invente coutait
        // ------------------------------------------------------------------

        [Fact]
        public void La_Portee_De_La_Volee_Reelle_Est_Sa_Projection_Et_Rien_De_Plus()
        {
            // 18 contremarches donnent 17 girons : 17 x 250 = 4 250 mm. C'est la valeur
            // que Revit affiche, et c'est la portee des que le modele ne porte pas de
            // palier.
            StairData stair = RealFlight(0.0);

            Assert.Equal(4250.0, stair.TotalGoingMm, 6);
            Assert.Equal(4250.0, stair.SpanMm, 6);

            // Le palier par defaut portait la portee a 5 550 mm : c'est exactement le
            // chiffre qu'affichait la fenetre sur un escalier qui n'a aucun palier.
            Assert.Equal(5550.0, RealFlight(1300.0).SpanMm, 6);
        }

        [Fact]
        public void Le_Palier_Invente_Majorait_Le_Moment_De_Plus_De_Moitie()
        {
            StairDesignResult real = Design(RealFlight(0.0));
            StairDesignResult invented = Design(RealFlight(1300.0));

            Assert.True(real.IsValid);
            Assert.True(invented.IsValid);

            // Calcul a la main, volee seule : g = 0,150 x 25 / cos(34,83) + 25 x 0,174 / 2
            // + 1,000 + 0,300 / cos(34,83) = 4,567 + 2,175 + 1,000 + 0,365 = 8,107 kN/m2.
            // ELU = 1,35 x 8,107 + 1,5 x 3,0 = 15,44 kN/m2.
            // M = w l2 / 8 = 15,44 x 4,252 / 8 = 34,9 kN.m/m.
            Assert.InRange(real.SpanMomentKnmPerM, 34.0, 35.8);

            // La fenetre affichait 57,7 kN.m/m. Le rapport est de l'ordre de 1,65 : le
            // palier invente majorait le moment de deux tiers.
            Assert.InRange(invented.SpanMomentKnmPerM, 56.0, 59.5);
            Assert.InRange(invented.SpanMomentKnmPerM / real.SpanMomentKnmPerM, 1.55, 1.75);
        }

        [Fact]
        public void Sans_Palier_Aucune_Barre_Ne_Depasse_Le_Sommet_De_La_Volee()
        {
            // LES BARRES EN L'AIR. Le ferraillage de palier etait pose au-dela de la
            // derniere marche, sur un palier qui n'existe pas.
            StairData stair = RealFlight(0.0);
            StairDesignResult result = Design(stair);
            Assert.True(result.IsValid);

            foreach (RebarGroup group in result.Plan.Groups)
            {
                foreach (LocalPoint point in EnumerateAllCopies(group))
                {
                    Assert.True(point.X <= stair.TotalGoingMm + 1.0,
                        string.Format(
                            "{0} : le point {1} est a {2:0} mm, au-dela du sommet de la " +
                            "volee ({3:0} mm) — il n'y a pas de beton la.",
                            group.Label, point, point.X, stair.TotalGoingMm));
                }
            }
        }

        [Fact]
        public void Sans_Palier_Le_Plan_Ne_Contient_Aucun_Groupe_De_Palier()
        {
            StairDesignResult result = Design(RealFlight(0.0));

            Assert.DoesNotContain(result.Plan.Groups, g => g.Label.Contains("palier"));
            Assert.Contains(result.Plan.Groups, g => g.Label.Contains("volee"));
        }

        [Fact]
        public void Corriger_Le_Palier_Ne_Rend_Pas_La_Volee_Conforme()
        {
            // L'HONNETETE DE LA CORRECTION. Le palier invente exagerait le defaut, il ne
            // l'a pas invente : une paillasse de 150 mm sur 4,25 m de portee echoue en
            // fleche de toute facon. l/d = 4 250 / 118 = 36 pour une limite de l'ordre de
            // 17 : le rapport reste proche de 2. Dire que la correction "arrange
            // l'escalier" serait faux.
            StairDesignResult result = Design(RealFlight(0.0));

            Assert.True(result.IsValid);
            Assert.True(result.HasFailedCheck,
                "Une paillasse de 150 mm sur 4,25 m ne passe pas, palier ou pas.");

            CheckResult deflection = result.Checks
                .First(c => c.Description.IndexOf("leche", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.Equal(CheckStatus.Fail, deflection.Status);
            Assert.InRange(deflection.Utilization, 1.7, 2.4);
        }

        [Fact]
        public void Une_Paillasse_De_220_mm_Passe_La_Fleche_Sur_La_Meme_Portee()
        {
            // Et la contrepartie : ce n'est pas la volee qui est impossible, c'est son
            // epaisseur. Calcul a la main : d = 188 mm, rho = 0,0028 < rho_0, la limite de
            // l'art. 7.4.2 vaut environ 36 pour un l/d reel de 22,6.
            StairData stair = RealFlight(0.0);
            stair.WaistThicknessMm = 220.0;
            stair.LandingThicknessMm = 220.0;

            StairDesignResult result = Design(stair);

            CheckResult deflection = result.Checks
                .First(c => c.Description.IndexOf("leche", StringComparison.OrdinalIgnoreCase) >= 0);
            Assert.NotEqual(CheckStatus.Fail, deflection.Status);
        }

        // ------------------------------------------------------------------
        // Dire ce qui a ete lu, et ce qui ne l'a pas ete
        // ------------------------------------------------------------------

        [Fact]
        public void Par_Defaut_Tout_Est_Suppose()
        {
            // C'est la verite avant qu'on ait lu quoi que ce soit, et c'est ce qui doit
            // etre dit — plutot qu'une valeur par defaut qui se fait passer pour une
            // lecture.
            var provenance = new StairGeometryProvenance();

            Assert.Equal(8, provenance.Assumptions().Count());
            foreach (StairDimension dimension in provenance.Assumptions())
            {
                Assert.False(provenance.IsEstablished(dimension));
                Assert.Equal(StairDimensionSource.Assumed, provenance.Of(dimension));
            }
        }

        [Fact]
        public void Le_Mode_D_Appui_Vient_En_Tete_Des_Hypotheses()
        {
            // Parce que c'est lui qui fixe la portee, donc le moment, donc tout le reste.
            var provenance = new StairGeometryProvenance();

            Assert.Equal(StairDimension.Support, provenance.Assumptions().First());
            Assert.Equal(StairDimension.LandingSpan, provenance.Assumptions().Skip(1).First());
        }

        [Theory]
        [InlineData(StairDimensionSource.ReadFromModel)]
        [InlineData(StairDimensionSource.StatedByEngineer)]
        public void Une_Valeur_Lue_Ou_Declaree_N_Est_Plus_Une_Hypothese(
            StairDimensionSource source)
        {
            var provenance = new StairGeometryProvenance();
            provenance.Set(StairDimension.Support, source);

            Assert.True(provenance.IsEstablished(StairDimension.Support));
            Assert.DoesNotContain(StairDimension.Support, provenance.Assumptions());
            Assert.Equal(7, provenance.Assumptions().Count());
        }

        [Fact]
        public void La_Provenance_Se_Copie_Avec_L_Escalier()
        {
            var provenance = new StairGeometryProvenance();
            provenance.Set(StairDimension.Width, StairDimensionSource.ReadFromModel);

            StairGeometryProvenance copy = provenance.Clone();
            copy.Set(StairDimension.Support, StairDimensionSource.ReadFromModel);

            Assert.True(copy.IsEstablished(StairDimension.Width));
            Assert.True(copy.IsEstablished(StairDimension.Support));
            // La copie est independante : modifier l'une ne touche pas l'autre.
            Assert.False(provenance.IsEstablished(StairDimension.Support));
        }

        [Fact]
        public void Chaque_Dimension_A_Un_Nom_Lisible()
        {
            // Un controle qui dirait « LandingSpan est suppose » ne sert a personne.
            foreach (StairDimension dimension in
                     Enum.GetValues(typeof(StairDimension)).Cast<StairDimension>())
            {
                string label = StairGeometryProvenance.Label(dimension);
                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.DoesNotContain(dimension.ToString(), label);
            }
        }

        // ------------------------------------------------------------------
        // Le controle d'origine dans le resultat
        // ------------------------------------------------------------------

        private static CheckResult Origin(StairDesignResult result)
        {
            return result.Checks.First(c => c.Description.Contains("Origine de la geometrie"));
        }

        [Fact]
        public void Une_Geometrie_Supposee_Sort_En_Avertissement()
        {
            StairDesignResult result = Design(RealFlight(0.0));
            CheckResult origin = Origin(result);

            Assert.Equal(CheckStatus.Warning, origin.Status);
            Assert.Contains("SUPPOSEES", origin.Comment);
            Assert.Contains("mode d'appui", origin.Comment);
        }

        [Fact]
        public void Le_Controle_Nomme_La_Portee_Qu_Il_Met_En_Doute()
        {
            // Un avertissement qui ne dit pas de combien on parle ne fait rien changer.
            StairData stair = RealFlight(0.0);
            CheckResult origin = Origin(Design(stair));

            Assert.Contains("4250", origin.Comment.Replace(" ", string.Empty));
        }

        [Fact]
        public void Une_Geometrie_Entierement_Lue_Ne_Produit_Aucun_Avertissement_D_Origine()
        {
            StairData stair = RealFlight(0.0);
            foreach (StairDimension dimension in
                     Enum.GetValues(typeof(StairDimension)).Cast<StairDimension>())
            {
                stair.Provenance.Set(dimension, StairDimensionSource.ReadFromModel);
            }

            CheckResult origin = Origin(Design(stair));

            Assert.Equal(CheckStatus.Pass, origin.Status);
            Assert.Contains("lue sur l'element dessine", origin.Comment);
        }

        [Fact]
        public void Le_Mode_D_Appui_Lu_Suffit_A_Lever_L_Avertissement_De_Portee()
        {
            // La distinction qui compte : l'epaisseur de paillasse supposee est un
            // avertissement, mais le comptage des contremarches, lui, ne l'est pas — il ne
            // deplace pas la portee.
            StairData stair = RealFlight(0.0);
            stair.Provenance.Set(StairDimension.Support, StairDimensionSource.ReadFromModel);
            stair.Provenance.Set(StairDimension.LandingSpan, StairDimensionSource.ReadFromModel);
            stair.Provenance.Set(StairDimension.WaistThickness,
                                 StairDimensionSource.StatedByEngineer);

            CheckResult origin = Origin(Design(stair));

            Assert.Equal(CheckStatus.Pass, origin.Status);
            // Il reste des hypotheses, et elles sont citees : simplement, aucune ne
            // deplace la portee ni le poids propre.
            Assert.Contains("SUPPOSEES", origin.Comment);
        }

        [Fact]
        public void L_Epaisseur_De_Paillasse_Supposee_Est_Un_Avertissement()
        {
            // Elle pilote tout le poids propre : la supposer, c'est supposer la charge.
            StairData stair = RealFlight(0.0);
            stair.Provenance.Set(StairDimension.Support, StairDimensionSource.ReadFromModel);
            stair.Provenance.Set(StairDimension.LandingSpan, StairDimensionSource.ReadFromModel);

            CheckResult origin = Origin(Design(stair));

            Assert.Equal(CheckStatus.Warning, origin.Status);
            Assert.Contains("poids propre", origin.Comment);
        }

        [Fact]
        public void Le_Controle_D_Origine_Est_Rendu_Avant_Les_Verifications_De_Resistance()
        {
            // On lit une note de calcul dans l'ordre : savoir sur quelle geometrie on
            // travaille vient avant de savoir si elle passe.
            StairDesignResult result = Design(RealFlight(0.0));

            int origin = result.Checks.FindIndex(
                c => c.Description.Contains("Origine de la geometrie"));
            int bending = result.Checks.FindIndex(
                c => c.Description.Contains("Flexion en travee"));

            Assert.True(origin >= 0 && bending > origin,
                        "Le controle d'origine doit preceder les verifications.");
        }

        // ------------------------------------------------------------------
        // Un parametre ne passe pas sous un article — art. 9.3.1.2(2)
        // ------------------------------------------------------------------

        private static StairDesignSettings WithRules(StairDetailingRules rules)
        {
            StairDesignSettings settings = Settings();
            settings.Detailing = rules;
            return settings;
        }

        private static double TopBarLength(StairData stair, StairDetailingRules rules)
        {
            return new StairDesignModule().Design(stair, WithRules(rules), null)
                .Reinforcement.TopBarLengthMm;
        }

        [Fact]
        public void Le_Chapeau_Ne_Descend_Jamais_Sous_Un_Cinquieme_De_La_Portee()
        {
            // LA FAILLE DE LA COUCHE PARAMETRIQUE. « Ancrage seul » produisait un chapeau
            // d'un l_bd, soit environ 400 mm sur une portee de 4 250 : moins de la moitie
            // du minimum de l'art. 9.3.1.2(2). Un parametre pouvait donc violer un article
            // en silence, ce qui est exactement le piege que la couche devait eviter.
            StairData stair = RealFlight(0.0);
            double floor = 0.2 * stair.SpanMm;

            double anchorageOnly = TopBarLength(stair,
                new StairDetailingRules { TopBarExtent = TopBarExtentMode.AnchorageOnly });
            double tooShort = TopBarLength(stair,
                new StairDetailingRules
                {
                    TopBarExtent = TopBarExtentMode.Fixed,
                    TopBarFixedLengthMm = 300.0
                });
            double tinyFraction = TopBarLength(stair,
                new StairDetailingRules { TopBarSpanFraction = 0.05 });

            Assert.True(anchorageOnly >= floor - 1.0,
                string.Format("Ancrage seul : {0:0} mm pour un minimum de {1:0}.",
                              anchorageOnly, floor));
            Assert.True(tooShort >= floor - 1.0,
                string.Format("Longueur imposee : {0:0} mm pour un minimum de {1:0}.",
                              tooShort, floor));
            Assert.True(tinyFraction >= floor - 1.0,
                string.Format("Fraction 0,05 : {0:0} mm pour un minimum de {1:0}.",
                              tinyFraction, floor));
        }

        [Fact]
        public void Le_Plancher_Reglementaire_Ne_Bride_Pas_Les_Choix_Plus_Longs()
        {
            // Il borne par le bas, il ne normalise pas : une nappe continue reste continue.
            StairData stair = RealFlight(0.0);

            double fullSpan = TopBarLength(stair,
                new StairDetailingRules { TopBarExtent = TopBarExtentMode.FullSpan });

            Assert.True(fullSpan > 0.2 * stair.SpanMm * 2.0,
                        "Une nappe continue doit rester bien plus longue que 0,2 l.");
        }

        [Fact]
        public void Le_Chapeau_Porte_Au_Minimum_Dit_Pourquoi()
        {
            // Une longueur qu'on n'a pas demandee doit s'expliquer, sinon elle passe pour
            // un bug.
            StairDesignResult result = new StairDesignModule().Design(RealFlight(0.0),
                WithRules(new StairDetailingRules
                {
                    TopBarExtent = TopBarExtentMode.AnchorageOnly
                }), null);

            DetailingDecision decision = result.Decisions
                .First(d => d.Question.Contains("Longueur des chapeaux"));

            Assert.Contains("9.3.1.2(2)", decision.Reason);
            Assert.Contains("0,2 l", decision.Reason);
        }

        [Fact]
        public void La_Nappe_Superieure_Reprend_Le_Quart_Du_Moment_De_Travee()
        {
            StairDesignResult result = Design(RealFlight(0.0));

            Assert.True(result.PartialFixitySteelMm2PerM > 0);
            Assert.True(result.Reinforcement.TopMain.AreaPerMetreMm2
                        >= result.PartialFixitySteelMm2PerM - 1.0,
                "La nappe posee doit couvrir le forfait de l'art. 9.3.1.2(2).");

            CheckResult fixity = result.Checks
                .First(c => c.Clause == "9.3.1.2 (2)");
            Assert.Equal(CheckStatus.Pass, fixity.Status);
        }

        [Fact]
        public void Le_Forfait_Ne_Decroit_Jamais_Quand_La_Portee_Croit()
        {
            // A geometrie de section identique, A_s,min est le meme : c'est le moment de
            // travee, et lui seul, qui peut faire monter le forfait. Il ne peut donc pas
            // baisser quand la portee augmente.
            StairData small = RealFlight(0.0);
            small.RiserCount = 6;

            Assert.True(Design(RealFlight(0.0)).PartialFixitySteelMm2PerM
                        >= Design(small).PartialFixitySteelMm2PerM,
                        "Le forfait suit le moment de travee, donc la portee.");
        }

        [Fact]
        public void Sous_Charge_Elevee_Le_Forfait_Devient_Dimensionnant()
        {
            // C'EST LA QUE LA REGLE SERT, et c'est le defaut qu'elle corrige. Quand aucun
            // moment sur appui n'etait declare, les chapeaux etaient poses au seul
            // A_s,min. Sur une volee courante A_s,min gouverne effectivement et rien ne
            // change ; des que la travee est chargee, 0,25 M le depasse et les chapeaux
            // etaient alors insuffisants.
            //
            // La comparaison porte sur la MEME section — meme paillasse, meme enrobage,
            // donc meme A_s,min — et ne fait varier que la charge. Un forfait qui monte
            // ne peut alors venir que du moment.
            StairData stair = RealFlight(0.0);

            StairDesignSettings light = Settings();
            StairDesignSettings heavy = Settings();
            heavy.VariableLoadKnM2 = 8.0;

            double atThreeKn = new StairDesignModule().Design(stair, light, null)
                .PartialFixitySteelMm2PerM;
            double atEightKn = new StairDesignModule().Design(stair, heavy, null)
                .PartialFixitySteelMm2PerM;

            Assert.True(atEightKn > atThreeKn * 1.2,
                string.Format("Le forfait doit suivre la charge : {0:0} puis {1:0} mm2/m.",
                              atThreeKn, atEightKn));
        }

        [Fact]
        public void Le_Controle_D_Encastrement_Partiel_Cite_Son_Article()
        {
            CheckResult fixity = Design(RealFlight(0.0)).Checks
                .First(c => c.Description.Contains("Encastrement partiel"));

            Assert.Equal("9.3.1.2 (2)", fixity.Clause);
            Assert.Contains("0,2 l", fixity.Comment);
        }

        // ------------------------------------------------------------------
        // Reconstruction des copies, comme dans STAIR-04
        // ------------------------------------------------------------------

        private static IEnumerable<LocalPoint> EnumerateAllCopies(RebarGroup group)
        {
            for (int copy = 0; copy < group.BarCount; copy++)
            {
                foreach (LocalPoint point in CopyPoints(group, copy)) yield return point;
            }
        }

        private static IEnumerable<LocalPoint> CopyPoints(RebarGroup group, int copyIndex)
        {
            double step = 0.0;
            LocalVector direction = LocalVector.AxisX;

            if (group.Layout != null && group.Layout.Kind != LayoutKind.Single
                && group.BarCount > 1)
            {
                direction = group.Layout.Direction;
                double length = Math.Sqrt(direction.X * direction.X
                                          + direction.Y * direction.Y
                                          + direction.Z * direction.Z);
                if (length > 0)
                {
                    direction = new LocalVector(direction.X / length, direction.Y / length,
                                                direction.Z / length);
                }
                step = group.Layout.ArrayLengthMm / (group.BarCount - 1) * copyIndex;
            }

            const int samples = 12;
            foreach (PlanSegment segment in group.Path)
            {
                for (int i = 0; i <= samples; i++)
                {
                    double t = (double)i / samples;
                    double x = segment.Start.X + (segment.End.X - segment.Start.X) * t;
                    double y = segment.Start.Y + (segment.End.Y - segment.Start.Y) * t;
                    double z = segment.Start.Z + (segment.End.Z - segment.Start.Z) * t;
                    yield return new LocalPoint(x + direction.X * step,
                                                y + direction.Y * step,
                                                z + direction.Z * step);
                }
            }
        }
    }
}
