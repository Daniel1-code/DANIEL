using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Pricing;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class ElementRefTests
    {
        [Fact]
        public void Element_du_modele_hote_et_element_de_lien()
        {
            var host = TestData.Concrete(10, 1.0);
            var linked = TestData.Concrete(10, 1.0);
            linked.SourceDocument = "Structure.rvt [555]";
            linked.Parameters[ElementRef.LinkInstanceParameter] = "555";

            Assert.False(ElementRef.Of(host).IsLinked);
            ElementRef r = ElementRef.Of(linked);
            Assert.True(r.IsLinked);
            Assert.Equal(555, r.LinkInstanceId);
            Assert.NotEqual(ElementRef.Of(host), r); // meme ElementId, documents differents : jamais confondus
        }

        [Fact]
        public void Les_references_d_une_ligne_distinguent_hote_et_lien()
        {
            var host = TestData.Concrete(10, 1.0);
            var linked = TestData.Concrete(10, 2.0);
            linked.SourceDocument = "Structure.rvt [555]";
            linked.Parameters[ElementRef.LinkInstanceParameter] = "555";
            BoqLine line = new BoqEngine().Build(new[] { host, linked }, TestData.Config()).AllLines.Single();
            Assert.Equal(2, line.ElementRefs.Count);
            Assert.Equal(3.0m, line.NetQuantity);
        }
    }

    public class ProjectFileTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-boq-projet-" + Guid.NewGuid().ToString("N"));

        public ProjectFileTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, true);

        [Fact]
        public void Le_fichier_projet_est_a_cote_du_rvt()
        {
            string rvt = Path.Combine(dir, "Immeuble R+3.rvt");
            File.WriteAllText(rvt, "x");
            Assert.Equal(Path.Combine(dir, "Immeuble R+3.danciboq.json"), ProjectFileStore.PathFor(rvt, "/autre", "ignoré"));
            Assert.Equal(Path.Combine(dir, "Projet non enregistré.danciboq.json"),
                ProjectFileStore.PathFor(string.Empty, dir, "Projet non enregistré.rvt"));
        }

        [Fact]
        public void Aller_retour_des_forfaits_de_la_phase_et_des_liens()
        {
            string path = Path.Combine(dir, "p.danciboq.json");
            var file = new ProjectFile
            {
                Phase = "Nouvelle construction",
                IncludeDemolition = true,
                Links = new List<string> { "Structure.rvt" },
                ManualLines =
                {
                    new ManualLine { Lot = "00", Designation = "Installation de chantier", UnitPrice = 4500.5m, Justification = "Devis n° 12" },
                    new ManualLine { Lot = "00", Code = "00.10", Designation = "Essais de laboratoire", Unit = BoqUnit.Each, Quantity = 6m, UnitPrice = 1250000m, Currency = Currency.CDF }
                }
            };
            ProjectFileStore.Save(path, file);
            var issues = new List<BoqIssue>();
            ProjectFile back = ProjectFileStore.Load(path, issues);

            Assert.Empty(issues);
            Assert.Equal("Nouvelle construction", back.Phase);
            Assert.True(back.IncludeDemolition);
            Assert.Equal(new[] { "Structure.rvt" }, back.Links);
            Assert.Equal(2, back.ManualLines.Count);
            Assert.Equal(BoqUnit.LumpSum, back.ManualLines[0].Unit);
            Assert.Equal(Currency.CDF, back.ManualLines[1].Currency);
            Assert.Equal(1250000m, back.ManualLines[1].UnitPrice);
            Assert.NotNull(back.SavedAt);
            Assert.Contains("\"forfaits\"", File.ReadAllText(path));
        }

        [Fact]
        public void Reecriture_atomique_avec_sauvegarde_de_la_version_precedente()
        {
            string path = Path.Combine(dir, "p.danciboq.json");
            ProjectFileStore.Save(path, new ProjectFile { Phase = "A" });
            ProjectFileStore.Save(path, new ProjectFile { Phase = "B" });
            Assert.Equal("B", ProjectFileStore.Load(path, new List<BoqIssue>()).Phase);
            Assert.Equal("A", ProjectFileStore.Load(path + ".bak", new List<BoqIssue>()).Phase);
            Assert.False(File.Exists(path + ".tmp"));
        }

        [Fact]
        public void Fichier_absent_projet_vide_fichier_corrompu_signale()
        {
            var issues = new List<BoqIssue>();
            Assert.Empty(ProjectFileStore.Load(Path.Combine(dir, "absent.json"), issues).ManualLines);
            Assert.Empty(issues);

            string bad = Path.Combine(dir, "bad.danciboq.json");
            File.WriteAllText(bad, "{ \"forfaits\": [ { \"lot\": \"00\" ");
            ProjectFileStore.Load(bad, issues);
            Assert.Contains(issues, i => i.Code == "JSON_INVALIDE");
        }
    }

    public class DemolitionTests
    {
        private static ElementQuantity Demolished(ElementQuantity q)
        {
            q.Parameters[PhaseStatus.Parameter] = PhaseStatus.Demolished;
            return q;
        }

        [Fact]
        public void Les_quantites_demolies_vont_dans_le_lot_DEM_et_le_reste_est_exclu_avec_motif()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var newColumn = TestData.Concrete(1, 1.0);
            newColumn.Parameters["ClasseBeton"] = "C25/30";
            newColumn.Parameters[PhaseStatus.Parameter] = PhaseStatus.New;
            var oldColumn = Demolished(TestData.Concrete(2, 0.5));
            var oldWall = Demolished(new ElementQuantity { ElementId = 3, Kind = QuantityKinds.Masonry, Category = "OST_Walls", TypeName = "Bloc 20", Value = 12, Unit = BoqUnit.SquareMeter });
            var oldDoor = Demolished(new ElementQuantity { ElementId = 4, Kind = QuantityKinds.Count, Category = "OST_Doors", TypeName = "P80", Value = 1, Unit = BoqUnit.Each });
            var oldFormwork = Demolished(new ElementQuantity { ElementId = 2, Kind = QuantityKinds.Formwork, Category = "OST_StructuralColumns", Value = 4, Unit = BoqUnit.SquareMeter });

            BoqResult r = new BoqEngine().Build(new[] { newColumn, oldColumn, oldWall, oldDoor, oldFormwork }, config);

            Assert.Equal("DEM", r.Lots.First().Code); // les demolitions passent en tete
            Assert.Equal(0.5m, r.AllLines.Single(l => l.Code == "DEM.01").NetQuantity);
            Assert.Equal(12m, r.AllLines.Single(l => l.Code == "DEM.02").NetQuantity);
            Assert.Equal(1m, r.AllLines.Single(l => l.Code == "DEM.03").NetQuantity);
            Assert.Equal("03.01", r.AllLines.Single(l => l.ElementIds.Contains(1)).Code);
            Assert.Contains(r.Excluded, x => x.Element.ElementId == 2 && x.RuleId == "DEM-SANS-OBJET");
        }

        [Fact]
        public void Sans_statut_de_phase_aucune_regle_de_demolition_ne_s_applique()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var column = TestData.Concrete(1, 1.0);
            column.Parameters["ClasseBeton"] = "C25/30";
            Assert.DoesNotContain(new BoqEngine().Build(new[] { column }, config).Lots, l => l.Code == "DEM");
        }
    }

    public class PartsAndLinksTests
    {
        [Fact]
        public void Une_Part_dont_l_element_d_origine_porte_des_armatures_n_est_pas_estimee_par_ratio()
        {
            var part = TestData.Concrete(900, 0.4);
            part.Parameters[PhaseStatus.SourceElementParameter] = "77";
            var ratios = new List<RebarRatio> { new RebarRatio { Category = TestData.List("OST_StructuralColumns"), KgPerM3 = 150m } };

            Assert.Empty(RebarRatioRule.Apply(new[] { part }, new HashSet<string> { RebarRatioRule.HostKey("", 77) }, ratios));
            Assert.Single(RebarRatioRule.Apply(new[] { part }, new HashSet<string>(), ratios));
        }

        [Fact]
        public void Un_constat_sur_un_element_de_lien_garde_son_instance_de_lien()
        {
            var q = TestData.Concrete(10, 1.0);
            q.Parameters["ClasseBeton"] = string.Empty;
            q.SourceDocument = "Structure.rvt [555]";
            q.Parameters[ElementRef.LinkInstanceParameter] = "555";

            AuditFinding f = Assert.Single(BoqAuditor.Run(new[] { q }, null, null, null).Findings);
            Assert.Equal(555, f.LinkInstanceId);
            Assert.True(f.ElementRefs.Single().IsLinked);
            Assert.Contains("[lien Structure.rvt [555]]", f.Message);
        }

        [Fact]
        public void Un_constat_du_modele_transmis_avec_son_lien()
        {
            var fact = new BoqIssue(IssueSeverity.Warning, "SANS_MATERIAU", "x") { LinkInstanceId = 42 };
            fact.ElementIds.Add(7);
            AuditFinding f = Assert.Single(BoqAuditor.Run(new ElementQuantity[0], new[] { fact }, null, null).Findings);
            Assert.Equal(new ElementRef(7, 42), f.ElementRefs.Single());
        }
    }
}
