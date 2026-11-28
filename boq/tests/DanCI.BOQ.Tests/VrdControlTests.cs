using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Dashboard;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class VrdControlTests
    {
        [Fact]
        public void Types_exclus_et_classement_par_nom_desactivable()
        {
            var s = new VrdSettings();
            Assert.True(VrdRules.IsExteriorFloor("OST_Floors", "Dalle parking 20", s));
            s.ExcludedTypes = new PatternList(new[] { "Dalle parking 20" });
            Assert.False(VrdRules.IsExteriorFloor("OST_Floors", "Dalle parking 20", s));
            Assert.True(VrdRules.IsExteriorFloor("OST_Floors", "Voirie enrobé", s));
            s.ExcludedTypes = new PatternList(new[] { "Dalle*" });
            Assert.False(VrdRules.IsExteriorFloor("OST_Floors", "Dalle pavée cour", s));
            s = new VrdSettings { ByTypeName = false };
            Assert.False(VrdRules.IsExteriorFloor("OST_Floors", "Voirie enrobé", s));
            Assert.False(VrdRules.IsFence("OST_StairsRailing", "Clôture grillage", s));
        }

        [Fact]
        public void Reglages_du_projet_aller_retour()
        {
            var installation = new ExtractionSettings();
            var p = new ProjectExtraction { VrdByTypeName = true, VrdExcludedTypes = " Dalle cour ; Sol*;; " };
            p.ApplyTo(installation);
            Assert.Equal(new[] { "Dalle cour", "Sol*" }, installation.Vrd.ExcludedTypes.Items.Select(i => i.Pattern));
            ProjectExtraction back = ProjectExtraction.From(installation);
            Assert.Equal("Dalle cour; Sol*", back.VrdExcludedTypes);
            Assert.True(back.VrdByTypeName);
        }

        [Fact]
        public void Le_diagramme_montre_les_types_et_le_mot_cle_VRD()
        {
            var lot = new BoqLotResult { Code = "11", Designation = "VRD" };
            var line = new BoqLine { Code = "11.02", LotCode = "11", Unit = BoqUnit.CubicMeter, Quantity = 5, NetQuantity = 5 };
            var q = new ElementQuantity { ElementId = 4, Category = "OST_Floors", TypeName = "Dalle parking 20", Value = 5, Unit = BoqUnit.CubicMeter };
            q.Parameters["MotifVRD"] = "*parking*";
            line.Contributions.Add(new LineContribution(q, "VRD-BETON"));
            lot.Lines.Add(line);
            var boq = new BoqResult();
            boq.Lots.Add(lot);
            ChartBar bar = ViewChart.Build(boq, ColoringMode.Lot).Bars.Single();
            Assert.Equal(new[] { "« Dalle parking 20 » (mot-clé *parking*)" }, bar.Types);
        }
    }
}
