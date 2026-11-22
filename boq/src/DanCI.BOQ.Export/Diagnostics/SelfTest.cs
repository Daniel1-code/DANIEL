using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using ClosedXML.Excel;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Audit;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Diagnostics;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Validation;
using DanCI.BOQ.Export.Csv;
using DanCI.BOQ.Export.Excel;
using DanCI.BOQ.Export.Pdf;

namespace DanCI.BOQ.Export.Diagnostics
{
    /// <summary>Resultat d'un controle de l'autotest.</summary>
    public sealed class SelfTestCheck
    {
        public string Name { get; set; }
        public bool Passed { get; set; }

        /// <summary>Faux pour une information (ni reussi ni echoue).</summary>
        public bool IsCheck { get; set; } = true;
        public string Detail { get; set; }
        public long Milliseconds { get; set; }
    }

    /// <summary>
    /// Autotest de l'installation, sans Revit : configuration, moteur, Excel, PDF, CSV, dependances.
    /// Chaque controle est isole : un echec n'empeche pas les suivants, et l'exception est
    /// rapportee telle quelle (type et message) pour le diagnostic a distance.
    /// </summary>
    public static class SelfTest
    {
        public static List<SelfTestCheck> Run(string configDirectory, string workDirectory) =>
            Run(configDirectory, workDirectory, out _);

        /// <summary>Variante qui rend aussi le devis d'essai (pour tester la fenetre avec de vraies donnees).</summary>
        public static List<SelfTestCheck> Run(string configDirectory, string workDirectory, out ExportContext sampleContext)
        {
            sampleContext = null;
            var checks = new List<SelfTestCheck>();
            Directory.CreateDirectory(workDirectory);
            BoqConfiguration config = null;

            Check(checks, "Configuration (" + configDirectory + ")", () =>
            {
                LoadResult load = ConfigurationLoader.LoadDirectory(configDirectory);
                config = load.Configuration;
                int errors = load.Issues.Count(i => i.Severity == IssueSeverity.Error);
                string first = string.Join(" | ", load.Issues.Where(i => i.Severity == IssueSeverity.Error).Take(3).Select(i => i.ToString()));
                return (errors == 0, errors == 0
                    ? load.Issues.Count + " avertissement(s) ; " + (config.Mapping?.Rules.Count ?? 0) + " règles, " + (config.Prices?.Prices.Count ?? 0) + " prix"
                    : errors + " erreur(s) : " + first);
            });

            List<ElementQuantity> sample = Sample();
            BoqResult boq = null;
            Check(checks, "Moteur de calcul (données de test)", () =>
            {
                BoqConfiguration c = config ?? throw new InvalidOperationException("configuration non chargée");
                boq = new BoqEngine().Build(sample, c);
                BoqResult again = new BoqEngine().Build(sample.AsEnumerable().Reverse().ToList(), c);
                bool same = boq.AllLines.Select(l => l.Code + "=" + l.NetQuantity).SequenceEqual(again.AllLines.Select(l => l.Code + "=" + l.NetQuantity));
                return (same, boq.AllLines.Count() + " ligne(s) ; résultat " + (same ? "identique" : "DIFFÉRENT") + " quand l'ordre des éléments change");
            });

            var ctx = new ExportContext
            {
                Project = new ProjectInfo { Name = "AUTOTEST DanCI (données fictives)", Revision = "T" },
                Configuration = config,
                Quantities = sample,
                DocumentTitle = "autotest",
                ScopeDescription = "autotest"
            };

            ctx.Boq = boq;
            if (boq != null)
            {
                ctx.Audit = BoqAuditor.Run(sample, null, boq, config?.Extraction);
            }

            Check(checks, "Excel (ClosedXML)", () =>
            {
                if (boq == null) return (false, "non testé : pas de BOQ (moteur en échec)");
                string path = Path.Combine(workDirectory, "autotest.xlsx");
                BoqWorkbookWriter.BoqSheetMap map = BoqWorkbookWriter.Write(ctx, path);
                using (var wb = new XLWorkbook(path))
                {
                    double ttc = wb.Worksheet("BOQ").Cell(map.TotalIncludingVat).Value.GetNumber();
                    return ((decimal)ttc == boq.TotalIncludingVat, "classeur écrit et relu (" + wb.Worksheets.Count + " feuilles) ; formule du TTC = moteur : " + ((decimal)ttc == boq.TotalIncludingVat));
                }
            });

            Check(checks, "PDF (QuestPDF)", () =>
            {
                if (boq == null) return (false, "non testé : pas de BOQ (moteur en échec)");
                string missing = BoqExporter.MissingPdfComponents();
                if (missing != null)
                {
                    return (false, missing);
                }
                byte[] pdf = BoqPdfWriter.Generate(ctx);
                File.WriteAllBytes(Path.Combine(workDirectory, "autotest.pdf"), pdf);
                return (pdf.Length > 1000, pdf.Length + " octets");
            });

            Check(checks, "CSV (écriture, relecture, empreinte)", () =>
            {
                var w = new StringWriter();
                QuantityCsvWriter.Write(w, sample, CsvFormat.ExcelFrancais);
                List<ElementQuantity> back = QuantityCsvReader.Read(w.ToString());
                bool same = QuantityFingerprint.Compute(sample) == QuantityFingerprint.Compute(back);
                return (same, "empreinte " + (same ? "identique" : "DIFFÉRENTE") + " après relecture");
            });

            Check(checks, "Journal (écriture)", () =>
            {
                BoqLog.Info("Autotest : écriture du journal.");
                return (File.Exists(BoqLog.CurrentFile), BoqLog.CurrentFile);
            });

            sampleContext = boq == null ? null : ctx;
            foreach (string line in RuntimeReport.Dependencies())
            {
                checks.Add(new SelfTestCheck { Name = "Dépendance", IsCheck = false, Passed = true, Detail = line.Trim() });
            }
            return checks;
        }

        /// <summary>Controle isole : une exception devient un echec rapporte (type, message, cause).</summary>
        public static void Check(List<SelfTestCheck> checks, string name, Func<(bool ok, string detail)> action)
        {
            var watch = Stopwatch.StartNew();
            var c = new SelfTestCheck { Name = name };
            try
            {
                (bool ok, string detail) = action();
                c.Passed = ok;
                c.Detail = detail;
            }
            catch (Exception ex)
            {
                c.Passed = false;
                Exception root = ex;
                while (root.InnerException != null) root = root.InnerException;
                c.Detail = ex.GetType().Name + " : " + ex.Message + (root != ex ? " — cause : " + root.GetType().Name + " : " + root.Message : string.Empty);
            }
            c.Milliseconds = watch.ElapsedMilliseconds;
            checks.Add(c);
        }

        /// <summary>Petit jeu de quantites fictives couvrant beton, armatures, maconnerie, portes.</summary>
        public static List<ElementQuantity> Sample()
        {
            var list = new List<ElementQuantity>();
            for (int i = 1; i <= 6; i++)
            {
                list.Add(new ElementQuantity
                {
                    ElementId = i, Category = "OST_StructuralColumns", TypeName = "Poteau 30x30", Material = "Béton C25/30",
                    Kind = QuantityKinds.ConcreteVolume, Value = 0.27 * i, Unit = BoqUnit.CubicMeter,
                    Parameters = new Dictionary<string, string> { { "ClasseBeton", "C25/30" } }
                });
                list.Add(new ElementQuantity
                {
                    ElementId = 100 + i, Category = "OST_Rebar", Kind = QuantityKinds.Reinforcement, Value = 31.5 * i, Unit = BoqUnit.Kilogram,
                    Parameters = new Dictionary<string, string> { { "HoteCategorie", "OST_StructuralColumns" } }
                });
            }
            list.Add(new ElementQuantity { ElementId = 200, Category = "OST_Walls", TypeName = "Bloc 20", Kind = QuantityKinds.Masonry, Value = 37.4, Unit = BoqUnit.SquareMeter });
            list.Add(new ElementQuantity
            {
                ElementId = 300, Category = "OST_Doors", Family = "Porte", TypeName = "P90", Kind = QuantityKinds.Count, Value = 1, Unit = BoqUnit.Each,
                Parameters = new Dictionary<string, string> { { "LargeurMm", "900" }, { "HauteurMm", "2100" } }
            });
            return list;
        }

        /// <summary>Rapport texte lisible (envoye au support avec le journal).</summary>
        public static string Format(IEnumerable<string> header, IEnumerable<SelfTestCheck> checks)
        {
            var lines = new List<string> { "AUTOTEST " + ProductInfo.Name + " " + ProductInfo.ApplicationVersion + " — " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), string.Empty };
            lines.AddRange(header);
            lines.Add(string.Empty);
            List<SelfTestCheck> list = checks.ToList();
            foreach (SelfTestCheck c in list)
            {
                string mark = !c.IsCheck ? "[i]  " : c.Passed ? "[OK] " : "[ÉCHEC] ";
                lines.Add(mark + c.Name + (c.IsCheck ? " (" + c.Milliseconds + " ms)" : string.Empty) + " : " + c.Detail);
            }
            int total = list.Count(c => c.IsCheck), ok = list.Count(c => c.IsCheck && c.Passed);
            lines.Add(string.Empty);
            lines.Add("RÉSULTAT : " + ok + " / " + total + " contrôles réussis.");
            return string.Join(Environment.NewLine, lines);
        }
    }
}
