using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Validation;
using DanCI.BOQ.Export.Csv;
using DanCI.BOQ.Export.Validation;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class TakeoffComparerTests
    {
        private static List<ElementQuantity> Model() => new List<ElementQuantity>
        {
            TestData.Concrete(1, 1.000),
            TestData.Concrete(2, 0.500),
            TestData.Concrete(3, 0.270),
            TestData.Rebar(10, 100, Reliability.Measured)
        };

        private static ManualEntry M(long id, string kind, decimal v) => new ManualEntry { ElementId = id, Kind = kind, Value = v };

        [Fact]
        public void Ecart_par_element_reference_manuelle_et_tolerance_de_2_pour_cent()
        {
            var manual = new List<ManualEntry>
            {
                M(1, QuantityKinds.ConcreteVolume, 0.990m),  // +1,01 % : conforme
                M(2, QuantityKinds.ConcreteVolume, 0.480m),  // +4,17 % : hors tolerance
                M(99, QuantityKinds.ConcreteVolume, 0.300m)  // absent du modele
            };
            TakeoffComparisonReport r = TakeoffComparer.Compare(Model(), null, manual);

            ComparisonRow e1 = r.Elements.Single(x => x.ElementId == 1);
            Assert.Equal(ComparisonStatus.Conforme, e1.Status);
            Assert.Equal(Math.Round(0.010m / 0.990m * 100m, 6), Math.Round(e1.DifferencePercent.Value, 6));

            ComparisonRow e2 = r.Elements.Single(x => x.ElementId == 2);
            Assert.Equal(ComparisonStatus.HorsTolerance, e2.Status);
            Assert.Contains(e2.Hints, h => h.Contains("Jointures"));
            Assert.Contains(e2.Hints, h => h.Contains("chevauchement"));

            Assert.Equal(ComparisonStatus.AbsentDuModele, r.Elements.Single(x => x.ElementId == 99).Status);
            Assert.Equal(ComparisonStatus.NonMesure, r.Elements.Single(x => x.ElementId == 3).Status);
        }

        [Fact]
        public void Le_verdict_porte_sur_le_total_de_chaque_nature()
        {
            // Jointure : 0,02 m³ attribue au poteau par Revit, a la poutre par le metreur. Elements hors tolerance, total exact.
            // Poteau : 1,000 (modele) / 0,985 (manuel) = +1,5 % ; poutre : 0,500 / 0,515 = -2,9 % ; total : 1,500 / 1,500.
            var manual = new List<ManualEntry> { M(1, QuantityKinds.ConcreteVolume, 0.985m), M(2, QuantityKinds.ConcreteVolume, 0.515m), M(10, QuantityKinds.Reinforcement, 101m) };
            TakeoffComparisonReport r = TakeoffComparer.Compare(Model(), null, manual);

            KindSummary concrete = r.Kinds.Single(k => k.Kind == QuantityKinds.ConcreteVolume);
            Assert.Equal(2, concrete.Compared);
            Assert.Equal(1.5m, concrete.ModelTotal);
            Assert.Equal(1.5m, concrete.ManualTotal);
            Assert.True(concrete.WithinTolerance);
            Assert.Equal(1, concrete.OutOfTolerance); // la poutre (-2,9 %) reste a expliquer
            Assert.True(r.TargetsMet);
        }

        [Fact]
        public void Objectif_non_atteint_si_un_total_cible_depasse_2_pour_cent()
        {
            var manual = new List<ManualEntry> { M(10, QuantityKinds.Reinforcement, 95m) }; // modele +5,3 %
            TakeoffComparisonReport r = TakeoffComparer.Compare(Model(), null, manual);
            Assert.False(r.TargetsMet);
            Assert.Contains(r.Elements.Single(x => x.ElementId == 10).Hints, h => h.Contains("crochets"));
        }

        [Fact]
        public void Sans_beton_ni_acier_mesures_l_objectif_n_est_pas_evaluable()
        {
            Assert.False(TakeoffComparer.Compare(Model(), null, new List<ManualEntry>()).TargetsMet);
        }

        [Fact]
        public void Comparaison_par_article_sur_la_quantite_nette()
        {
            var config = TestData.Config();
            config.Coefficients.Items.Add(new DanCI.BOQ.Core.Pricing.Coefficient { Code = "P", Value = 0.05m, AppliesTo = TestData.List("03.01") });
            BoqResult boq = new BoqEngine().Build(Model(), config);
            var manual = new List<ManualEntry>
            {
                new ManualEntry { ArticleCode = "03.01", Value = 1.77m },
                new ManualEntry { ArticleCode = "07.99", Value = 3m }
            };
            TakeoffComparisonReport r = TakeoffComparer.Compare(Model(), boq, manual);
            ComparisonRow a = r.Articles.Single(x => x.ArticleCode == "03.01");
            Assert.Equal(1.77m, a.Model); // nette, sans les 5 % de pertes
            Assert.Equal(ComparisonStatus.Conforme, a.Status);
            Assert.Equal(ComparisonStatus.AbsentDuModele, r.Articles.Single(x => x.ArticleCode == "07.99").Status);
        }
    }

    public class FingerprintTests
    {
        private static List<ElementQuantity> Sample()
        {
            var list = new List<ElementQuantity>();
            var rnd = new Random(11);
            for (int i = 1; i <= 50; i++)
            {
                ElementQuantity q = TestData.Concrete(i, rnd.NextDouble() * 3);
                q.Parameters["ClasseBeton"] = "C25/30";
                list.Add(q);
                list.Add(TestData.Rebar(1000 + i, rnd.NextDouble() * 200, i % 4 == 0 ? Reliability.Estimated : Reliability.Measured));
            }
            return list;
        }

        [Fact]
        public void Independante_de_l_ordre_sensible_au_micro_metre_cube_insensible_au_bruit()
        {
            List<ElementQuantity> a = Sample();
            string fa = QuantityFingerprint.Compute(a);
            Assert.Equal(fa, QuantityFingerprint.Compute(a.AsEnumerable().Reverse().ToList()));
            Assert.Equal(64, fa.Length);

            List<ElementQuantity> noise = Sample();
            noise[0].Value += 1e-9; // bruit de calcul flottant
            Assert.Equal(fa, QuantityFingerprint.Compute(noise));

            List<ElementQuantity> changed = Sample();
            changed[0].Value += 2e-6; // 2 cm³
            Assert.NotEqual(fa, QuantityFingerprint.Compute(changed));
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Un_metre_brut_relu_depuis_le_CSV_donne_la_meme_empreinte(bool frenchFormat)
        {
            List<ElementQuantity> a = Sample();
            a[0].TypeName = "=Type piégé; avec \"guillemets\"";
            a[1].SourceDocument = "Structure.rvt [555]";
            a[1].Parameters[ElementRef.LinkInstanceParameter] = "555";
            var w = new StringWriter();
            QuantityCsvWriter.Write(w, a, frenchFormat ? CsvFormat.ExcelFrancais : CsvFormat.Invariant);

            List<ElementQuantity> back = QuantityCsvReader.Read(w.ToString());

            Assert.Equal(a.Count, back.Count);
            Assert.Equal(QuantityFingerprint.Compute(a), QuantityFingerprint.Compute(back));
            Assert.Contains(back, q => q.TypeName == "=Type piégé; avec \"guillemets\"");
            Assert.Equal(555, ElementRef.Of(back.Single(q => q.SourceDocument == "Structure.rvt [555]")).LinkInstanceId);
        }

        [Fact]
        public void Comparaison_de_deux_analyses()
        {
            List<ElementQuantity> a = Sample(), b = Sample();
            ElementQuantity Find(List<ElementQuantity> l, long id) => l.Single(q => q.ElementId == id);

            Find(b, 3).Value += 0.001;                          // valeur differente
            b.Remove(Find(b, 5));                               // absent de B
            Find(a, 1001).Reliability = Reliability.Estimated;  // meme valeur, statut different
            Find(a, 1001).Assumption = "x";
            Find(b, 1001).Reliability = Reliability.Measured;

            RunComparisonReport r = RunComparer.Compare(a, b);
            Assert.False(r.Identical);
            Assert.Equal(3, r.Differences.Count);
            Assert.Contains(r.Differences, d => d.Nature == "Valeur différente" && d.Key.StartsWith("|3|"));
            Assert.Contains(r.Differences, d => d.Nature == "Absent du métré B" && d.Key.StartsWith("|5|"));
            Assert.Contains(r.Differences, d => d.Nature == "Fiabilité différente" && d.Key.StartsWith("|1001|"));
            Assert.True(RunComparer.Compare(Sample(), Sample()).Identical);
        }
    }

    public class ValidationWorkbookTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-boq-valid-" + Guid.NewGuid().ToString("N"));

        public ValidationWorkbookTests() => Directory.CreateDirectory(dir);

        public void Dispose() => Directory.Delete(dir, true);

        [Fact]
        public void Gabarit_sans_quantites_du_modele_rempli_puis_compare()
        {
            var model = new List<ElementQuantity> { TestData.Concrete(1, 1.2345), TestData.Concrete(2, 0.5), TestData.Rebar(10, 87.65, Reliability.Measured) };
            BoqResult boq = new BoqEngine().Build(model, TestData.Config());
            string template = Path.Combine(dir, "gabarit.xlsx");
            ValidationWorkbook.WriteTemplate(template, model, boq, "Test");

            using (var wb = new XLWorkbook(template))
            {
                IXLWorksheet ws = wb.Worksheet(ValidationWorkbook.ElementSheet);
                // Independance du metre manuel : aucune quantite du modele dans le gabarit.
                Assert.DoesNotContain(ws.CellsUsed(), c => c.Value.IsNumber && (Math.Abs(c.Value.GetNumber() - 1.2345) < 1e-9 || Math.Abs(c.Value.GetNumber() - 87.65) < 1e-9));
                Assert.Equal(3, ws.RowsUsed().Count(r => r.RowNumber() > 3));
                // Le metreur remplit deux lignes.
                foreach (IXLRow row in ws.RowsUsed().Where(r => r.RowNumber() > 3))
                {
                    long id = (long)row.Cell(2).Value.GetNumber();
                    if (id == 1) row.Cell(9).Value = 1.22;
                    if (id == 10) row.Cell(9).Value = "86,9";
                }
                wb.Worksheet(ValidationWorkbook.ArticleSheet).Cell(4, 4).Value = 1.73;
                wb.Save();
            }

            List<ManualEntry> manual = ValidationWorkbook.ReadManual(template);
            Assert.Equal(3, manual.Count);
            Assert.Contains(manual, m => m.ElementId == 10 && m.Value == 86.9m);

            TakeoffComparisonReport report = TakeoffComparer.Compare(model, boq, manual);
            string reportPath = Path.Combine(dir, "rapport.xlsx");
            ValidationWorkbook.WriteComparison(reportPath, report, "Test", QuantityFingerprint.Compute(model));
            using (var wb = new XLWorkbook(reportPath))
            {
                Assert.Equal(new[] { "Synthèse", "Par élément", "Par article" }, wb.Worksheets.Select(w => w.Name).ToArray());
                Assert.Contains(wb.Worksheet("Synthèse").CellsUsed(), c => c.GetString().StartsWith("OBJECTIF ATTEINT"));
            }
        }

        [Fact]
        public void Rapport_de_reproductibilite()
        {
            var a = new List<ElementQuantity> { TestData.Concrete(1, 1.0) };
            var b = new List<ElementQuantity> { TestData.Concrete(1, 1.001) };
            string path = Path.Combine(dir, "repro.xlsx");
            ValidationWorkbook.WriteRunComparison(path, RunComparer.Compare(a, b), "A.csv", "B.csv");
            using (var wb = new XLWorkbook(path))
            {
                Assert.Equal(2, wb.Worksheet("Différences").RowsUsed().Count());
            }
        }
    }
}
