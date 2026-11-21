using System;
using System.IO;
using System.Linq;
using System.Reflection;
using DanCI.BOQ.Core;
using Xunit;

namespace DanCI.BOQ.Tests
{
    /// <summary>
    /// Regle d'architecture : le Core et l'Export ne connaissent pas Revit. Si cette regle
    /// tombe, les tests du metre ne peuvent plus tourner sans Revit.
    /// </summary>
    public class ArchitectureTests
    {
        [Theory]
        [InlineData("DanCI.BOQ.Core")]
        [InlineData("DanCI.BOQ.Export")]
        public void Assembly_ne_reference_pas_l_API_Revit(string assemblyName)
        {
            Assembly assembly = Assembly.Load(assemblyName);
            string[] revit = assembly.GetReferencedAssemblies()
                .Select(a => a.Name)
                .Where(n => n.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase)
                         || n.StartsWith("Autodesk", StringComparison.OrdinalIgnoreCase))
                .ToArray();
            Assert.Empty(revit);
        }

        [Fact]
        public void Sources_du_Core_sans_using_Autodesk()
        {
            string coreDir = FindRepoDirectory(Path.Combine("src", "DanCI.BOQ.Core"));
            var offenders = Directory.GetFiles(coreDir, "*.cs", SearchOption.AllDirectories)
                .Where(f => !f.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
                .Where(f => File.ReadAllLines(f).Any(l => l.TrimStart().StartsWith("using Autodesk")))
                .ToArray();
            Assert.Empty(offenders);
        }

        [Fact]
        public void La_version_de_Revit_ciblee_est_inscrite_dans_l_assembly()
        {
            Assert.Contains(ProductInfo.TargetRevitVersion, new[] { "2025", "2026", "2027" });
            Assert.Equal("R" + ProductInfo.TargetRevitVersion, ProductInfo.BuildConfiguration);
        }

        [Fact]
        public void Le_framework_suit_la_configuration()
        {
            int expectedMajor = ProductInfo.TargetRevitVersion == "2027" ? 10 : 8;
            Assert.Equal(expectedMajor, Environment.Version.Major);
        }

        private static string FindRepoDirectory(string relative)
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                string candidate = Path.Combine(dir.FullName, relative);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
                dir = dir.Parent;
            }
            throw new DirectoryNotFoundException(relative);
        }
    }
}
