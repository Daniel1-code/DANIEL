using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Extraction;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class FormworkScopeTests
    {
        [Fact]
        public void Coffrage_seulement_sur_ossature_et_elements_structurels()
        {
            FormworkSettings s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction.Formwork;
            Assert.True(s.FrameworkOnly);
            Assert.True(FormworkScope.IsFramework("OST_StructuralColumns", false, s));
            Assert.True(FormworkScope.IsFramework("OST_StructuralFraming", false, s));
            Assert.True(FormworkScope.IsFramework("OST_Floors", false, s));
            Assert.True(FormworkScope.IsFramework("OST_StructuralFoundation", false, s));
            Assert.False(FormworkScope.IsFramework("OST_Walls", false, s));          // voile non structurel
            Assert.True(FormworkScope.IsFramework("OST_Walls", true, s));            // mur porteur
            Assert.False(FormworkScope.IsFramework("OST_GenericModel", false, s));
            s.FrameworkOnly = false;
            Assert.True(FormworkScope.IsFramework("OST_Walls", false, s));
        }

        [Fact]
        public void Ratios_d_armatures_du_projet_appliques()
        {
            ExtractionSettings s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction;
            Assert.Contains(s.RebarRatios, r => r.Designation == "rives de dalle");
            var p = new ProjectExtraction { RebarRatios = new Dictionary<string, decimal> { ["poteaux"] = 180m }, FormworkFrameworkOnly = false };
            p.ApplyTo(s);
            Assert.Equal(180m, s.RebarRatios.Single(r => r.Designation == "poteaux").KgPerM3);
            Assert.False(s.Formwork.FrameworkOnly);
            ProjectExtraction back = ProjectExtraction.From(s);
            Assert.Equal(180m, back.RebarRatios["poteaux"]);
            Assert.False(back.FormworkFrameworkOnly);
        }
    }
}
