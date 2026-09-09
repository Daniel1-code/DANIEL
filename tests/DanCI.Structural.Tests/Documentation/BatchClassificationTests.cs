using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Dashboard;
using Xunit;

namespace DanCI.Structural.Tests.Documentation
{
    /// <summary>
    /// DASH-03 : le mode batch — classement des elements et plan de couleurs.
    /// Fiche de validation : docs/validation/DASH-03.md
    ///
    /// Le mode batch n'a qu'une question a resoudre, et c'est la seule ou il peut se
    /// tromper GRAVEMENT : a quel module un element appartient-il. Calculer une longrine
    /// comme une poutre, ou une semelle filante comme une semelle isolee, produirait un
    /// resultat d'apparence normale et faux.
    ///
    /// Ces tests verrouillent donc d'abord ce que le classeur REFUSE de faire.
    /// </summary>
    public class BatchClassificationTests
    {
        // ------------------------------------------------------------------
        // Ce qui se classe sans ambiguite
        // ------------------------------------------------------------------

        [Theory]
        [InlineData(StructuralCategory.StructuralColumns, ElementKind.Column)]
        [InlineData(StructuralCategory.StructuralFraming, ElementKind.Beam)]
        [InlineData(StructuralCategory.Floors, ElementKind.Slab)]
        [InlineData(StructuralCategory.Walls, ElementKind.Wall)]
        [InlineData(StructuralCategory.Stairs, ElementKind.Stair)]
        public void Une_Categorie_Sans_Ambiguite_Se_Classe(StructuralCategory category,
                                                           ElementKind expected)
        {
            Classification classification = ElementClassifier.Classify(category);

            Assert.True(classification.Succeeded);
            Assert.Equal(expected, classification.Kind);
        }

        [Theory]
        [InlineData(FoundationForm.Isolated, ElementKind.IsolatedFooting)]
        [InlineData(FoundationForm.UnderWall, ElementKind.StripFooting)]
        public void La_Forme_Distingue_Les_Deux_Semelles(FoundationForm form,
                                                         ElementKind expected)
        {
            Classification classification = ElementClassifier.Classify(
                StructuralCategory.StructuralFoundation, form);

            Assert.True(classification.Succeeded);
            Assert.Equal(expected, classification.Kind);
        }

        // ------------------------------------------------------------------
        // Ce que le classeur REFUSE
        // ------------------------------------------------------------------

        [Fact]
        public void Une_Fondation_De_Forme_Indeterminee_N_Est_Pas_Classee()
        {
            // Revit range la semelle isolee, la semelle filante et le radier sous la MEME
            // categorie. Les confondre serait une faute de calcul : l'une poinconne,
            // l'autre non.
            Classification classification = ElementClassifier.Classify(
                StructuralCategory.StructuralFoundation, FoundationForm.Unknown);

            Assert.False(classification.Succeeded);
            Assert.Contains("poinconne", classification.Reason);
            Assert.Contains("ne choisit pas a votre place", classification.Reason);
        }

        [Fact]
        public void Un_Radier_N_Est_Approche_Par_Aucun_Module()
        {
            // Le moteur ne traite pas la fondation superficielle continue en deux
            // directions. L'approcher par une dalle ignorerait la reaction du sol.
            Classification classification = ElementClassifier.Classify(
                StructuralCategory.StructuralFoundation, FoundationForm.Slab);

            Assert.False(classification.Succeeded);
            Assert.Contains("reaction du sol", classification.Reason);
        }

        [Fact]
        public void Une_Categorie_Inconnue_N_Est_Pas_Devinee()
        {
            Classification classification = ElementClassifier.Classify(
                StructuralCategory.Unknown);

            Assert.False(classification.Succeeded);
            Assert.Contains("ne devine pas", classification.Reason);
        }

        [Fact]
        public void Tout_Refus_Dit_Pourquoi()
        {
            // Un element ecarte sans raison est un element perdu : l'ingenieur ne saura
            // pas qu'il doit le reprendre a la main.
            var refused = new[]
            {
                ElementClassifier.Classify(StructuralCategory.Unknown),
                ElementClassifier.Classify(StructuralCategory.StructuralFoundation,
                                           FoundationForm.Unknown),
                ElementClassifier.Classify(StructuralCategory.StructuralFoundation,
                                           FoundationForm.Slab)
            };

            foreach (Classification classification in refused)
            {
                Assert.False(classification.Succeeded);
                Assert.False(string.IsNullOrWhiteSpace(classification.Reason));
                Assert.True(classification.Reason.Length > 40,
                            "Une raison doit expliquer, pas etiqueter.");
            }
        }

        [Fact]
        public void Les_Deux_Ambiguites_Connues_Sont_Rappelees_A_Chaque_Lot()
        {
            // La longrine et la paillasse-plancher sont les deux cas ou le classement est
            // juste par categorie et faux par nature. Ils ne peuvent pas etre resolus, ils
            // doivent donc etre DITS.
            List<string> warnings = ElementClassifier.StandingWarnings.ToList();

            Assert.Equal(2, warnings.Count);
            Assert.Contains(warnings, w => w.Contains("LONGRINES") && w.Contains("EN 1998-5"));
            Assert.Contains(warnings, w => w.Contains("PLANCHER INCLINE") && w.Contains("40 %"));
        }

        [Fact]
        public void Une_Longrine_Est_Classee_En_Poutre_Et_C_Est_Assume()
        {
            // Rien dans la categorie Revit ne distingue une longrine d'une poutre. Le
            // classement en poutre est le choix le moins pire ; ce qui compte, c'est que
            // l'avertissement permanent le dise.
            Classification classification = ElementClassifier.Classify(
                StructuralCategory.StructuralFraming);

            Assert.Equal(ElementKind.Beam, classification.Kind);
            Assert.Contains(ElementClassifier.StandingWarnings,
                            w => w.Contains("Relancez-les dans le module Longrine"));
        }

        // ------------------------------------------------------------------
        // Le plan de couleurs
        // ------------------------------------------------------------------

        private static DesignedElement Painted(string id, double utilization,
                                               bool failed = false, bool valid = true)
        {
            return new DesignedElement
            {
                Name = "E" + id,
                ElementId = id,
                IsValid = valid,
                Checks = new List<CheckResult>
                {
                    new CheckResult
                    {
                        Code = "EC2", Clause = "6.1",
                        Status = failed ? CheckStatus.Fail : CheckStatus.Pass,
                        Utilization = utilization
                    }
                }
            };
        }

        [Fact]
        public void Chaque_Element_Adressable_Est_Peint_Une_Fois_Et_Une_Seule()
        {
            var elements = new[]
            {
                Painted("1", 0.20), Painted("2", 0.60), Painted("3", 0.90),
                Painted("4", 1.30, failed: true), Painted("5", 0.70)
            };

            ColourPlan plan = ViewColourPlan.Build(elements);

            Assert.Equal(5, plan.PaintedCount);
            List<string> painted = plan.Groups.SelectMany(g => g.ElementIds).ToList();
            Assert.Equal(5, painted.Distinct().Count());
        }

        [Fact]
        public void Les_Elements_Sont_Groupes_Par_Bande()
        {
            // Une vue se colore par remplacement graphique applique a beaucoup d'elements,
            // pas element par element.
            var elements = new[]
            {
                Painted("1", 0.60), Painted("2", 0.70), Painted("3", 0.80),
                Painted("4", 1.30, failed: true)
            };

            ColourPlan plan = ViewColourPlan.Build(elements);

            Assert.Equal(2, plan.Groups.Count);
            Assert.Equal(3, plan.Groups.Single(g => g.Band == ControlBand.Normal)
                                       .ElementIds.Count);
            Assert.Single(plan.Groups.Single(g => g.Band == ControlBand.Overloaded)
                                     .ElementIds);
        }

        [Fact]
        public void Une_Bande_Sans_Element_Ne_Produit_Aucun_Groupe()
        {
            // Inutile d'encombrer une vue d'un remplacement qui ne s'applique a rien.
            ColourPlan plan = ViewColourPlan.Build(new[] { Painted("1", 0.60) });

            Assert.Single(plan.Groups);
            Assert.Equal(ControlBand.Normal, plan.Groups[0].Band);
        }

        [Fact]
        public void Les_Groupes_Suivent_L_Ordre_De_Gravite()
        {
            // Une legende qui ne suit pas l'ordre de gravite se lit deux fois.
            var elements = new[]
            {
                Painted("1", 1.50, failed: true),
                Painted("2", 0.20),
                Painted("3", 0.90),
                Painted("4", 0.60),
                new DesignedElement { Name = "Z", ElementId = "5", IsValid = false }
            };

            ColourPlan plan = ViewColourPlan.Build(elements);

            var expected = new[]
            {
                ControlBand.NotDesigned, ControlBand.LightlyUtilised,
                ControlBand.Normal, ControlBand.Tight, ControlBand.Overloaded
            };
            Assert.Equal(expected, plan.Groups.Select(g => g.Band).ToArray());
        }

        [Fact]
        public void Un_Element_Sans_Identifiant_Est_Compte_Pas_Oublie()
        {
            // On ne peut pas colorer ce qu'on ne sait pas designer. Mais un element absent
            // de la vue coloree doit s'expliquer, sinon la couleur ment par omission.
            var elements = new[]
            {
                Painted("1", 0.60),
                new DesignedElement { Name = "Sans id", IsValid = true },
                new DesignedElement { Name = "Vide", ElementId = "   ", IsValid = true }
            };

            ColourPlan plan = ViewColourPlan.Build(elements);

            Assert.Equal(1, plan.PaintedCount);
            Assert.Equal(2, plan.UnaddressableCount);
        }

        [Fact]
        public void La_Legende_Du_Plan_Ne_Montre_Que_Les_Bandes_Employees()
        {
            ColourPlan plan = ViewColourPlan.Build(
                new[] { Painted("1", 0.60), Painted("2", 1.20, failed: true) });

            List<ControlBand> legend = plan.Legend.Select(c => c.Band).ToList();

            Assert.Equal(2, legend.Count);
            Assert.DoesNotContain(ControlBand.NotDesigned, legend);
        }

        [Fact]
        public void Un_Lot_Vide_Donne_Un_Plan_Vide_Sans_Echouer()
        {
            ColourPlan plan = ViewColourPlan.Build(new DesignedElement[0]);

            Assert.Empty(plan.Groups);
            Assert.Equal(0, plan.PaintedCount);
            Assert.Equal(0, plan.UnaddressableCount);
        }

        [Fact]
        public void Un_Plan_Construit_Sur_Rien_Ne_Leve_Pas()
        {
            ColourPlan plan = ViewColourPlan.Build(null);

            Assert.NotNull(plan);
            Assert.Empty(plan.Groups);
        }
    }
}
