using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using DanCI.Structural.Core.Elements;
using DanCI.Structural.Core.Materials;
using DanCI.Structural.Core.Results;
using DanCI.Structural.Documentation.Dashboard;
using DanCI.Structural.Documentation.Quantities;
using DanCI.Structural.Documentation.Reports;
using DanCI.Structural.Engine.Stair;
using Xunit;

namespace DanCI.Structural.Tests.Documentation
{
    /// <summary>
    /// DASH-01 : le tableau de bord de projet.
    /// Fiche de validation : docs/validation/DASH-01.md
    ///
    /// Il ne calcule rien. Il regarde ce qui a deja ete calcule et repond a trois questions
    /// dans l'ordre : qu'est-ce qui ne passe pas, pourquoi, et combien ca coute. Ces tests
    /// verifient les trois, plus la propriete qui compte le plus : qu'il ne moyenne AUCUN
    /// taux de travail.
    /// </summary>
    public class ProjectDashboardTests
    {
        private static CheckResult Check(string code, string clause, CheckStatus status,
                                         double utilization, string description = "V")
        {
            return new CheckResult
            {
                Code = code,
                Clause = clause,
                Description = description,
                Status = status,
                Utilization = utilization
            };
        }

        private static DesignedElement Element(string name, ElementKind kind, bool valid,
                                               params CheckResult[] checks)
        {
            return new DesignedElement
            {
                Name = name,
                Kind = kind,
                IsValid = valid,
                Checks = checks.ToList(),
                Quantities = new SteelQuantities { ConcreteVolumeM3 = 1.0 }
            };
        }

        // ------------------------------------------------------------------
        // 1. Qu'est-ce qui ne passe pas
        // ------------------------------------------------------------------

        [Fact]
        public void Un_Lot_Vide_Ne_Se_Declare_Pas_Livrable()
        {
            // Zero element non conforme sur zero element n'est pas une bonne nouvelle.
            DashboardSummary summary = ProjectDashboard.Build(new DesignedElement[0]);

            Assert.Equal(0, summary.Total);
            Assert.False(summary.IsDeliverable);
        }

        [Fact]
        public void Chaque_Element_Tombe_Dans_Une_Seule_Categorie()
        {
            var ok = Element("P1", ElementKind.Column, true,
                             Check("EC2", "6.1", CheckStatus.Pass, 0.5));
            var toVerify = Element("P2", ElementKind.Column, true,
                                   Check("EC2", "6.1", CheckStatus.Pass, 0.6));
            toVerify.Warnings.Add("Hypothese a confirmer.");
            var notCompliant = Element("P3", ElementKind.Column, true,
                                       Check("EC2", "6.1", CheckStatus.Fail, 1.4));
            var failed = Element("P4", ElementKind.Column, false);

            DashboardSummary summary = ProjectDashboard.Build(
                new[] { ok, toVerify, notCompliant, failed });

            Assert.Equal(4, summary.Total);
            Assert.Equal(1, summary.CompliantCount);
            Assert.Equal(1, summary.ToVerifyCount);
            Assert.Equal(1, summary.NotCompliantCount);
            Assert.Equal(1, summary.FailedCount);
            Assert.Equal(summary.Total, summary.CompliantCount + summary.ToVerifyCount
                                        + summary.NotCompliantCount + summary.FailedCount);
        }

        [Fact]
        public void Un_Seul_Element_Non_Conforme_Rend_Le_Lot_Non_Livrable()
        {
            // Un projet n'est pas conforme « a 97 % ».
            var elements = new List<DesignedElement>();
            for (int i = 0; i < 99; i++)
            {
                elements.Add(Element("OK" + i, ElementKind.Beam, true,
                                     Check("EC2", "6.1", CheckStatus.Pass, 0.4)));
            }
            Assert.True(ProjectDashboard.Build(elements).IsDeliverable);

            elements.Add(Element("KO", ElementKind.Beam, true,
                                 Check("EC2", "6.1", CheckStatus.Fail, 1.1)));

            Assert.False(ProjectDashboard.Build(elements).IsDeliverable);
        }

        [Fact]
        public void Un_Echec_De_Dimensionnement_Rend_Aussi_Le_Lot_Non_Livrable()
        {
            // Ne pas avoir de resultat n'est pas la meme chose que passer.
            DashboardSummary summary = ProjectDashboard.Build(
                new[] { Element("X", ElementKind.Wall, false) });

            Assert.Equal(DesignStatus.Failed, summary.WorstUtilised[0].Status);
            Assert.False(summary.IsDeliverable);
        }

        [Fact]
        public void Les_Plus_Charges_Viennent_En_Tete_Et_La_Liste_Est_Bornee()
        {
            var elements = new List<DesignedElement>();
            for (int i = 1; i <= 20; i++)
            {
                elements.Add(Element("E" + i, ElementKind.Slab, true,
                                     Check("EC2", "6.1", CheckStatus.Pass, i / 10.0)));
            }

            DashboardSummary summary = ProjectDashboard.Build(elements, 5);

            Assert.Equal(5, summary.WorstUtilised.Count);
            Assert.Equal("E20", summary.WorstUtilised[0].Name);
            Assert.Equal(2.0, summary.WorstUtilization, 6);
            for (int i = 1; i < summary.WorstUtilised.Count; i++)
            {
                Assert.True(summary.WorstUtilised[i - 1].MaxUtilization
                            >= summary.WorstUtilised[i].MaxUtilization);
            }
        }

        [Fact]
        public void A_Taux_Egal_L_Element_En_Defaut_Passe_Devant()
        {
            var passing = Element("A", ElementKind.Beam, true,
                                  Check("EC2", "6.1", CheckStatus.Pass, 1.0));
            var failing = Element("B", ElementKind.Beam, true,
                                  Check("EC2", "6.1", CheckStatus.Fail, 1.0));

            DashboardSummary summary = ProjectDashboard.Build(new[] { passing, failing });

            Assert.Equal("B", summary.WorstUtilised[0].Name);
        }

        // ------------------------------------------------------------------
        // 2. Pourquoi : les articles en defaut
        // ------------------------------------------------------------------

        [Fact]
        public void Les_Defauts_Sont_Groupes_Par_Article_Et_Classes_Par_Frequence()
        {
            // C'est la ligne la plus utile : un article qui tombe dix-huit fois est une
            // hypothese de projet a revoir, pas dix-huit erreurs de saisie.
            var elements = new List<DesignedElement>();
            for (int i = 0; i < 18; i++)
            {
                elements.Add(Element("D" + i, ElementKind.Slab, true,
                    Check("EN 1992-1-1", "7.4.2", CheckStatus.Fail, 1.5 + i / 100.0,
                          "Fleche par l'elancement limite")));
            }
            elements.Add(Element("P1", ElementKind.Column, true,
                Check("EN 1992-1-1", "6.1", CheckStatus.Fail, 3.0, "Flexion composee")));

            DashboardSummary summary = ProjectDashboard.Build(elements);

            Assert.Equal(2, summary.FailuresByClause.Count);

            ClauseFailure first = summary.FailuresByClause[0];
            Assert.Equal("7.4.2", first.Clause);
            Assert.Equal(18, first.ElementCount);
            Assert.Equal("EN 1992-1-1 art. 7.4.2", first.Reference);
            Assert.InRange(first.WorstUtilization, 1.66, 1.68);

            // L'article isole vient apres, meme si SON taux est le plus eleve du projet :
            // le classement mesure l'etendue du probleme, pas sa pointe.
            Assert.Equal("6.1", summary.FailuresByClause[1].Clause);
        }

        [Fact]
        public void Un_Element_Qui_Echoue_Deux_Fois_Sur_Le_Meme_Article_Ne_Compte_Qu_Une_Fois()
        {
            // Une semelle echoue en effort tranchant dans les deux directions : c'est un
            // element touche, pas deux, sinon le classement ne mesure plus rien.
            var footing = Element("S1", ElementKind.IsolatedFooting, true,
                Check("EN 1992-1-1", "6.2.2", CheckStatus.Fail, 1.2, "Tranchant X"),
                Check("EN 1992-1-1", "6.2.2", CheckStatus.Fail, 1.6, "Tranchant Y"));

            ClauseFailure failure = ProjectDashboard.Build(new[] { footing })
                .FailuresByClause.Single();

            Assert.Equal(1, failure.ElementCount);
            Assert.Single(failure.Elements);
            // Mais le taux retenu est bien le pire des deux.
            Assert.Equal(1.6, failure.WorstUtilization, 6);
        }

        [Fact]
        public void Le_Meme_Numero_D_Article_Dans_Deux_Normes_Fait_Deux_Problemes()
        {
            var a = Element("A", ElementKind.GradeBeam, true,
                            Check("EN 1992-1-1", "5.8.2", CheckStatus.Fail, 1.1));
            var b = Element("B", ElementKind.GradeBeam, true,
                            Check("EN 1998-1", "5.8.2", CheckStatus.Fail, 1.2));

            Assert.Equal(2, ProjectDashboard.Build(new[] { a, b }).FailuresByClause.Count);
        }

        [Fact]
        public void Un_Lot_Conforme_N_A_Aucun_Article_En_Defaut()
        {
            var element = Element("A", ElementKind.Beam, true,
                                  Check("EC2", "6.1", CheckStatus.Pass, 0.8),
                                  Check("EC2", "6.2", CheckStatus.Warning, 0.9));

            Assert.Empty(ProjectDashboard.Build(new[] { element }).FailuresByClause);
        }

        // ------------------------------------------------------------------
        // 3. Combien
        // ------------------------------------------------------------------

        [Fact]
        public void Les_Totaux_Sont_Ventiles_Par_Famille()
        {
            var column = Element("P1", ElementKind.Column, true);
            column.Quantities = new SteelQuantities
            {
                ConcreteVolumeM3 = 2.0, LongitudinalMassKg = 300.0
            };

            var slab = Element("D1", ElementKind.Slab, true);
            slab.Quantities = new SteelQuantities
            {
                ConcreteVolumeM3 = 10.0, LongitudinalMassKg = 800.0
            };

            DashboardSummary summary = ProjectDashboard.Build(new[] { column, slab });

            Assert.Equal(1100.0, summary.SteelKg, 3);
            Assert.Equal(12.0, summary.ConcreteM3, 3);
            Assert.Equal(1100.0 / 12.0, summary.RatioKgPerM3, 3);

            KindTotals columns = summary.ByKind.Single(k => k.Kind == ElementKind.Column);
            Assert.Equal(150.0, columns.RatioKgPerM3, 3);

            KindTotals slabs = summary.ByKind.Single(k => k.Kind == ElementKind.Slab);
            Assert.Equal(80.0, slabs.RatioKgPerM3, 3);
        }

        [Fact]
        public void Un_Element_Sans_Quantitatif_Ne_Fait_Pas_Echouer_La_Synthese()
        {
            // Un element dont le dimensionnement a echoue n'a pas de plan, donc pas de
            // quantitatif. Il compte quand meme dans les effectifs.
            var failed = Element("X", ElementKind.Stair, false);
            failed.Quantities = null;

            DashboardSummary summary = ProjectDashboard.Build(new[] { failed });

            Assert.Equal(1, summary.Total);
            Assert.Equal(0.0, summary.SteelKg, 6);
            Assert.Equal(1, summary.ByKind.Single().Count);
        }

        [Fact]
        public void Un_Ratio_Sans_Beton_Vaut_Zero_Et_Ne_Divise_Pas_Par_Zero()
        {
            var element = Element("X", ElementKind.Wall, true);
            element.Quantities = new SteelQuantities { ConcreteVolumeM3 = 0.0 };

            Assert.Equal(0.0, ProjectDashboard.Build(new[] { element }).RatioKgPerM3, 6);
        }

        // ------------------------------------------------------------------
        // Ce que le tableau de bord ne fait PAS
        // ------------------------------------------------------------------

        [Fact]
        public void Aucune_Moyenne_De_Taux_De_Travail_N_Est_Exposee()
        {
            // Un element dont neuf verifications passent a 0,3 et la dixieme echoue a 2,9
            // n'est pas « a 0,56 en moyenne » : il est en defaut. Moyenner des taux de
            // travail est la facon la plus simple de rendre un projet dangereux ET
            // rassurant. Ce test empeche qu'une telle propriete soit ajoutee sans y penser.
            var types = new[]
            {
                typeof(DashboardSummary), typeof(KindTotals),
                typeof(ClauseFailure), typeof(DesignedElement)
            };

            foreach (Type type in types)
            {
                foreach (MemberInfo member in type.GetMembers())
                {
                    string name = member.Name.ToUpperInvariant();
                    Assert.False(name.Contains("AVERAGE") || name.Contains("MOYEN")
                                 || name.Contains("MEAN"),
                        string.Format("{0}.{1} : le tableau de bord ne moyenne pas les taux.",
                                      type.Name, member.Name));
                }
            }
        }

        [Fact]
        public void Le_Taux_D_Un_Element_Est_Le_Maximum_Pas_La_Moyenne()
        {
            var element = Element("E", ElementKind.Column, true,
                Check("EC2", "6.1", CheckStatus.Pass, 0.3),
                Check("EC2", "6.2", CheckStatus.Pass, 0.3),
                Check("EC2", "7.4.2", CheckStatus.Fail, 2.9));

            Assert.Equal(2.9, element.MaxUtilization, 6);
        }

        [Fact]
        public void Une_Verification_Sans_Objet_N_Entre_Pas_Dans_Le_Taux()
        {
            // Sinon un « Sans objet » a 0,00 tirerait le maximum vers le bas... ou, pire,
            // un Sans objet mal rempli le tirerait vers le haut.
            var element = Element("E", ElementKind.StripFooting, true,
                Check("EC2", "6.4.4", CheckStatus.NotApplicable, 99.0),
                Check("EC2", "6.1", CheckStatus.Pass, 0.7));

            Assert.Equal(0.7, element.MaxUtilization, 6);
        }

        // ------------------------------------------------------------------
        // La note de synthese
        // ------------------------------------------------------------------

        private static ReportHeader Header()
        {
            return new ReportHeader
            {
                ProjectName = "Essai",
                StructuralCode = "EN 1992-1-1:2004",
                NationalAnnex = "Valeurs recommandees",
                ApplicationVersion = "test",
                EngineVersion = "test"
            };
        }

        [Fact]
        public void La_Synthese_D_Un_Lot_Vide_Ne_Se_Declare_Pas_Conforme()
        {
            string note = CalculationReport.BuildProject(Header(), new DesignedElement[0]);

            Assert.Contains("AUCUN ELEMENT DIMENSIONNE", note);
            Assert.Contains("n'est pas un lot conforme", note);
            Assert.DoesNotContain("LOT CONFORME", note);
        }

        [Fact]
        public void La_Synthese_Annonce_La_Conformite_En_Une_Phrase_Binaire()
        {
            var ok = Element("A", ElementKind.Beam, true,
                             Check("EC2", "6.1", CheckStatus.Pass, 0.5));

            Assert.Contains("LOT CONFORME", CalculationReport.BuildProject(Header(), new[] { ok }));

            var ko = Element("B", ElementKind.Beam, true,
                             Check("EC2", "6.1", CheckStatus.Fail, 1.4));
            string note = CalculationReport.BuildProject(Header(), new[] { ok, ko });

            Assert.Contains("LOT NON CONFORME", note);
            Assert.Contains("1 element(s) sur 2", note);
        }

        [Fact]
        public void La_Synthese_Nomme_Les_Articles_En_Defaut_Et_Les_Elements_A_Reprendre()
        {
            var slab = Element("D12", ElementKind.Slab, true,
                Check("EN 1992-1-1", "7.4.2", CheckStatus.Fail, 2.1,
                      "Fleche par l'elancement limite"));

            string note = CalculationReport.BuildProject(Header(), new[] { slab });

            Assert.Contains("ARTICLES EN DEFAUT", note);
            Assert.Contains("EN 1992-1-1 art. 7.4.2", note);
            Assert.Contains("ELEMENTS A REPRENDRE", note);
            Assert.Contains("D12", note);
            Assert.Contains("Non conforme", note);
        }

        [Fact]
        public void Un_Lot_Conforme_N_Affiche_Ni_Article_En_Defaut_Ni_Element_A_Reprendre()
        {
            var ok = Element("A", ElementKind.Column, true,
                             Check("EC2", "6.1", CheckStatus.Pass, 0.5));

            string note = CalculationReport.BuildProject(Header(), new[] { ok });

            Assert.DoesNotContain("ARTICLES EN DEFAUT", note);
            Assert.DoesNotContain("ELEMENTS A REPRENDRE", note);
            Assert.Contains("QUANTITATIF PAR FAMILLE", note);
        }

        [Fact]
        public void La_Synthese_Ne_Contient_Aucun_Taux_Moyen()
        {
            var elements = new[]
            {
                Element("A", ElementKind.Beam, true, Check("EC2", "6.1", CheckStatus.Pass, 0.3)),
                Element("B", ElementKind.Beam, true, Check("EC2", "6.1", CheckStatus.Fail, 2.9))
            };

            string note = CalculationReport.BuildProject(Header(), elements).ToUpperInvariant();

            Assert.DoesNotContain("MOYEN", note);
            Assert.DoesNotContain("MOYENNE", note);
        }

        [Fact]
        public void La_Synthese_Renvoie_Aux_Notes_D_Element()
        {
            // Elle dit ou regarder, pas pourquoi une section passe. Le pretendre serait
            // faire passer un tableau pour une justification.
            string note = CalculationReport.BuildProject(Header(),
                new[] { Element("A", ElementKind.Wall, true,
                                Check("EC2", "6.1", CheckStatus.Pass, 0.5)) });

            Assert.Contains("ne remplace aucune note d'element", note);
        }

        // ------------------------------------------------------------------
        // La vue uniforme dit la meme chose que le module
        // ------------------------------------------------------------------

        private static StairDesignSettings StairSettings()
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

        private static StairData Flight(double waistMm)
        {
            return new StairData
            {
                Name = "V",
                RiserHeightMm = 174.0,
                TreadDepthMm = 250.0,
                RiserCount = 18,
                WaistThicknessMm = waistMm,
                WidthMm = 1200.0,
                LandingThicknessMm = waistMm,
                LandingSpanMm = 0.0,
                SpanKind = StairSpanKind.AlongFlightOnly,
                Shape = StairFlightShape.Straight
            };
        }

        [Theory]
        [InlineData(150.0)]   // echoue en fleche
        [InlineData(220.0)]   // passe
        public void Le_Statut_De_La_Vue_Uniforme_Est_Celui_Du_Module(double waistMm)
        {
            // DEUX DEFINITIONS DU MOT « CONFORME » QUI DIVERGENT SERAIENT PIRES QU'UNE
            // SEULE IMPARFAITE. La vue uniforme recalcule le statut ; ce test verifie
            // qu'elle retrouve exactement celui du module.
            StairDesignResult result = new StairDesignModule()
                .Design(Flight(waistMm), StairSettings(), null);

            var view = new DesignedElement
            {
                Name = result.Stair.Name,
                Kind = ElementKind.Stair,
                IsValid = result.IsValid,
                Checks = result.Checks,
                Warnings = result.Warnings
            };

            Assert.Equal(result.Status, view.StatusLabel);
            Assert.Equal(result.MaxUtilization, view.MaxUtilization, 6);
            Assert.Equal(result.HasFailedCheck, view.HasFailedCheck);
        }

        [Fact]
        public void Les_Libelles_De_Statut_Couvrent_Les_Quatre_Cas()
        {
            Assert.Equal("OK", DesignedElement.Label(DesignStatus.Compliant));
            Assert.Equal("A verifier", DesignedElement.Label(DesignStatus.ToVerify));
            Assert.Equal("Non conforme", DesignedElement.Label(DesignStatus.NotCompliant));
            Assert.Equal("Echec", DesignedElement.Label(DesignStatus.Failed));
        }

        [Fact]
        public void Chaque_Famille_A_Un_Nom_Lisible()
        {
            foreach (ElementKind kind in Enum.GetValues(typeof(ElementKind)).Cast<ElementKind>())
            {
                string label = DesignedElement.Label(kind);
                Assert.False(string.IsNullOrWhiteSpace(label));
                Assert.DoesNotContain(kind.ToString(), label);
            }
        }
    }
}
