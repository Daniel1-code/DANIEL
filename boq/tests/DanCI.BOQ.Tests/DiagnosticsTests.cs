using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using DanCI.BOQ.Core;
using DanCI.BOQ.Core.Diagnostics;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class RuntimeReportTests
    {
        [Fact]
        public void Le_rapport_indique_le_runtime_NET_reellement_charge()
        {
            var lines = RuntimeReport.Build(null);
            Assert.Contains(lines, l => l.Contains(RuntimeInformation.FrameworkDescription));
            Assert.Contains(lines, l => l.Contains("(hors Revit)"));
        }

        [Fact]
        public void Le_rapport_indique_la_version_et_le_build_de_Revit()
        {
            var host = new HostInfo
            {
                VersionNumber = ProductInfo.TargetRevitVersion,
                SubVersionNumber = ProductInfo.TargetRevitVersion + ".5",
                VersionBuild = "26.5.0.1",
                VersionName = "Autodesk Revit " + ProductInfo.TargetRevitVersion
            };
            var lines = RuntimeReport.Build(host);
            Assert.Contains(lines, l => l.Contains(ProductInfo.TargetRevitVersion + ".5") && l.Contains("26.5.0.1"));
            Assert.DoesNotContain(lines, l => l.StartsWith("ATTENTION"));
        }

        [Fact]
        public void Un_build_charge_par_une_autre_annee_de_Revit_est_signale()
        {
            string other = ProductInfo.TargetRevitVersion == "2027" ? "2026" : "2027";
            string warning = RuntimeReport.MismatchWarning(new HostInfo { VersionNumber = other });
            Assert.NotNull(warning);
            Assert.Contains(other, warning);
            Assert.Contains(RuntimeReport.Build(new HostInfo { VersionNumber = other }), l => l.StartsWith("ATTENTION"));
        }

        [Fact]
        public void Pas_d_alerte_si_la_version_de_Revit_est_inconnue()
        {
            Assert.Null(RuntimeReport.MismatchWarning(null));
            Assert.Null(RuntimeReport.MismatchWarning(new HostInfo()));
        }
    }

    public class BoqLogTests : IDisposable
    {
        private readonly string dir = Path.Combine(Path.GetTempPath(), "danci-boq-tests-" + Guid.NewGuid().ToString("N"));

        public BoqLogTests()
        {
            BoqLog.UseDirectory(dir);
        }

        public void Dispose()
        {
            BoqLog.UseDirectory(null);
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Le_journal_ecrit_dans_le_fichier_du_jour()
        {
            BoqLog.Info("ligne de test é à ç");
            BoqLog.Error("erreur de test", new InvalidOperationException("détail"));

            string text = File.ReadAllText(BoqLog.CurrentFile);
            Assert.Contains("INFO  ligne de test é à ç", text);
            Assert.Contains("ERROR erreur de test", text);
            Assert.Contains("InvalidOperationException", text);
            Assert.StartsWith("boq-", Path.GetFileName(BoqLog.CurrentFile));
        }

        [Fact]
        public void Le_journal_ne_leve_jamais_d_exception()
        {
            // Un fichier existant a la place du dossier rend l'ecriture impossible.
            Directory.CreateDirectory(Path.GetDirectoryName(dir));
            File.WriteAllText(dir, "bloque");
            try
            {
                BoqLog.Info("ne doit pas lever");
            }
            finally
            {
                File.Delete(dir);
            }
        }

        [Fact]
        public void Le_dossier_par_defaut_est_AppData_DanCI_BOQ_logs()
        {
            string[] parts = BoqLog.DefaultDirectory.Split(Path.DirectorySeparatorChar);
            Assert.Equal(new[] { "DanCI", "BOQ", "logs" }, parts.Skip(parts.Length - 3).ToArray());
        }
    }
}

namespace DanCI.BOQ.Tests
{
    public class DependencyReportTests
    {
        [Fact]
        public void Les_dependances_surveillees_sont_toutes_listees_avec_leur_emplacement()
        {
            // Force le chargement de ClosedXML pour verifier l'affichage de la version et de l'emplacement.
            Xunit.Assert.NotNull(typeof(ClosedXML.Excel.XLWorkbook).Assembly);
            var lines = DanCI.BOQ.Core.Diagnostics.RuntimeReport.Dependencies();
            Xunit.Assert.Equal(DanCI.BOQ.Core.Diagnostics.RuntimeReport.WatchedDependencies.Length, lines.Count);
            Xunit.Assert.Contains(lines, l => l.StartsWith("ClosedXML") && l.Contains("ClosedXML.dll"));
        }
    }
}
