using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Export.Csv;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class AuditTests
    {
        private static ExtractionSettings Ratios() => new ExtractionSettings
        {
            RebarRatios = new List<RebarRatio> { new RebarRatio { Category = TestData.List("OST_StructuralColumns"), KgPerM3 = 150m } }
        };

        private static ElementQuantity Classed(long id, double m3)
        {
            ElementQuantity q = TestData.Concrete(id, m3);
            q.Parameters["ClasseBeton"] = "C25/30";
            return q;
        }

        [Fact]
        public void Beton_sans_classe_signale_avec_son_element()
        {
            ElementQuantity q = TestData.Concrete(5, 1.0);
            q.Parameters["ClasseBeton"] = string.Empty;
            AuditReport r = BoqAuditor.Run(new[] { q, Classed(6, 1.0) }, null, null, null);
            AuditFinding f = Assert.Single(r.Findings, x => x.Code == "BETON_SANS_CLASSE");
            Assert.Equal(AuditSection.BetonSansClasse, f.Section);
            Assert.Equal(new long[] { 5 }, f.ElementIds);
        }

        [Fact]
        public void Elements_estimes_par_ratio_et_armatures_manquantes()
        {
            var concrete = new[] { Classed(1, 1.0), Classed(2, 1.0) };
            concrete[1].Category = "OST_StructuralFraming"; // pas de ratio pour les poutres
            List<ElementQuantity> rebar = RebarRatioRule.Apply(concrete, new HashSet<string>(), Ratios().RebarRatios);

            AuditReport r = BoqAuditor.Run(concrete.Concat(rebar).ToList(), null, null, Ratios());
            Assert.Contains(r.Findings, f => f.Code == "ARMATURES_PAR_RATIO" && f.ElementIds.Contains(1) && f.Severity == IssueSeverity.Warning);
            Assert.Contains(r.Findings, f => f.Code == "ARMATURES_MANQUANTES" && f.ElementIds.Contains(2) && f.Severity == IssueSeverity.Error);
        }

        [Fact]
        public void Ferraillage_partiel_signale()
        {
            // Poteau de 1 m³ portant 10 kg de barres modelisees, contre 150 kg/m³ de ratio de reference.
            var column = Classed(1, 1.0);
            var bars = TestData.Rebar(100, 10, Reliability.Measured);
            bars.MethodCode = "TOTAL_LENGTH_X_HA_MASS";
            bars.Parameters["HoteId"] = "1";

            AuditReport r = BoqAuditor.Run(new[] { column, bars }, null, null, Ratios());
            AuditFinding f = Assert.Single(r.Findings, x => x.Code == "FERRAILLAGE_INCOMPLET");
            Assert.Contains("10 kg/m³", f.Message);
            Assert.Equal(new long[] { 1 }, f.ElementIds);
        }

        [Fact]
        public void Ferraillage_complet_non_signale()
        {
            var column = Classed(1, 1.0);
            var bars = TestData.Rebar(100, 140, Reliability.Measured);
            bars.MethodCode = "TOTAL_LENGTH_X_HA_MASS";
            bars.Parameters["HoteId"] = "1";
            Assert.DoesNotContain(BoqAuditor.Run(new[] { column, bars }, null, null, Ratios()).Findings, f => f.Code == "FERRAILLAGE_INCOMPLET");
        }

        [Fact]
        public void Profil_sans_masse_lineique()
        {
            var steel = new ElementQuantity
            {
                ElementId = 9, Kind = QuantityKinds.StructuralSteel, Unit = BoqUnit.Kilogram, Value = 87,
                MethodCode = "SOLID_VOLUME_X_DENSITY", Reliability = Reliability.Estimated, Assumption = "volume × 7850", TypeName = "Tube 100x100x5"
            };
            AuditFinding f = Assert.Single(BoqAuditor.Run(new[] { steel }, null, null, null).Findings);
            Assert.Equal(AuditSection.Profils, f.Section);
        }

        [Fact]
        public void Elements_en_double_a_la_meme_position()
        {
            ElementQuantity a = Classed(1, 0.5), b = Classed(2, 0.5), c = Classed(3, 0.5);
            a.Parameters["Position"] = b.Parameters["Position"] = "0,0,0;300,300,3000";
            c.Parameters["Position"] = "1000,0,0;1300,300,3000";
            AuditFinding f = Assert.Single(BoqAuditor.Run(new[] { a, b, c }, null, null, null).Findings, x => x.Code == "DOUBLON_POSITION");
            Assert.Equal(new long[] { 1, 2 }, f.ElementIds);
            Assert.Equal(IssueSeverity.Error, f.Severity);
        }

        [Fact]
        public void Constats_du_modele_ranges_dans_leur_rubrique()
        {
            var facts = new List<BoqIssue>
            {
                new BoqIssue(IssueSeverity.Warning, "PIECE_NON_FERMEE", "Pièce 12"),
                new BoqIssue(IssueSeverity.Warning, "LIEN_DECHARGE", "Lien"),
                new BoqIssue(IssueSeverity.Error, "CHEVAUCHEMENT_NON_JOINT", "Dalle / poutre"),
                new BoqIssue(IssueSeverity.Warning, "MATERIAU_PAR_DEFAUT", "Mur"),
                new BoqIssue(IssueSeverity.Error, "EXTRACTION_ECHEC", "?"),
                new BoqIssue(IssueSeverity.Info, "ELEMENTS_EXCLUS", "info ignorée")
            };
            AuditReport r = BoqAuditor.Run(new ElementQuantity[0], facts, null, null);
            Assert.Equal(AuditSection.PiecesEtMurs, r.Findings.Single(f => f.Code == "PIECE_NON_FERMEE").Section);
            Assert.Equal(AuditSection.Liens, r.Findings.Single(f => f.Code == "LIEN_DECHARGE").Section);
            Assert.Equal(AuditSection.Doublons, r.Findings.Single(f => f.Code == "CHEVAUCHEMENT_NON_JOINT").Section);
            Assert.Equal(AuditSection.Materiaux, r.Findings.Single(f => f.Code == "MATERIAU_PAR_DEFAUT").Section);
            Assert.Equal(AuditSection.AutresAnomalies, r.Findings.Single(f => f.Code == "EXTRACTION_ECHEC").Section);
            Assert.DoesNotContain(r.Findings, f => f.Code == "ELEMENTS_EXCLUS");
            // Ordre du rapport : rubriques du §6.
            Assert.Equal(r.Findings.Select(f => f.Section).OrderBy(s => s), r.Findings.Select(f => f.Section));
        }

        [Fact]
        public void Non_classes_du_moteur_dans_la_rubrique_7_et_indice_de_fiabilite_repris()
        {
            var config = TestData.Config();
            config.Prices.Prices.Add(TestData.Price("03.01", BoqUnit.CubicMeter, 100m));
            var generic = new ElementQuantity { ElementId = 77, Category = "OST_GenericModel", Kind = "Volume", Value = 1, Unit = BoqUnit.CubicMeter };
            BoqResult boq = new BoqEngine().Build(new[] { Classed(1, 1.0), generic }, config);

            AuditReport r = BoqAuditor.Run(new[] { Classed(1, 1.0), generic }, null, boq, null);
            Assert.Contains(r.Findings, f => f.Section == AuditSection.NonClasses && f.ElementIds.Contains(77));
            Assert.Equal(1m, r.ReliabilityIndex);
        }

        [Fact]
        public void Part_mesuree_par_nature_sans_prix()
        {
            var measured = TestData.Rebar(1, 75, Reliability.Measured);
            var estimated = TestData.Rebar(2, 25, Reliability.Estimated);
            AuditReport r = BoqAuditor.Run(new[] { measured, estimated }, null, null, null);
            Assert.Equal(0.75m, r.MeasuredShareByKind[QuantityKinds.Reinforcement]);
        }

        [Fact]
        public void Rapport_d_audit_en_CSV()
        {
            ElementQuantity q = TestData.Concrete(5, 1.0);
            q.Parameters["ClasseBeton"] = string.Empty;
            var w = new StringWriter();
            AuditCsvWriter.Write(w, BoqAuditor.Run(new[] { q }, null, null, null), CsvFormat.ExcelFrancais);
            string text = w.ToString();
            Assert.Contains("2. Béton sans classe identifiable;Avertissement;BETON_SANS_CLASSE;", text);
            Assert.Contains("Indice de fiabilité", text);
        }
    }

    public class ScopeTests
    {
        [Fact]
        public void Description_de_la_portee()
        {
            Assert.Equal("modèle entier, toutes phases", new AnalysisScope().Describe());
            Assert.Equal("vue active, phase « Nouvelle construction » + démolitions, niveaux : R+1, R+2, liens : Structure",
                new AnalysisScope
                {
                    Mode = ScopeMode.VueActive, Phase = "Nouvelle construction", IncludeDemolition = true,
                    Levels = new List<string> { "R+1", "R+2" }, Links = new List<string> { "Structure" }
                }.Describe());
        }

        private sealed class CancelAt : IProgressSink
        {
            private readonly CancellationTokenSource cts = new CancellationTokenSource();
            public int Reports;
            public int StopAfter;
            public CancellationToken Cancellation => cts.Token;
            public void Report(string stage, int done, int total)
            {
                Reports++;
                if (Reports >= StopAfter) cts.Cancel();
            }
        }

        [Fact]
        public void L_annulation_interrompt_au_prochain_point_de_controle()
        {
            var sink = new CancelAt { StopAfter = 2 };
            int processed = 0;
            Assert.Throws<System.OperationCanceledException>(() =>
            {
                for (int i = 1; i <= 1000; i++)
                {
                    sink.Step("test", i, 1000, every: 10);
                    processed++;
                }
            });
            Assert.Equal(19, processed); // 2e rapport au 20e element : arret avant de le traiter
        }
    }
}
