using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class WallTrenchAndVrdTests
    {
        [Fact]
        public void Largeur_et_longueur_de_rigole()
        {
            var s = new WallTrenchSettings();                       // 0,20 par cote, min 0,40
            Assert.Equal(0.60, WallTrench.Width(0.20, s), 9);
            Assert.Equal(0.40, WallTrench.Width(0.0, s), 9);
            Assert.Equal(10.0, WallTrench.Length(10, 0.6, 0), 9);
            Assert.Equal(9.4, WallTrench.Length(10, 0.6, 2), 9);    // deux croisements : demi-largeur a chaque bout
            Assert.Equal(0.0, WallTrench.Length(0.2, 0.6, 2), 9);
        }

        [Fact]
        public void Remblai_de_rigole_deduit_le_volume_enterre_pas_le_beton_du_mur()
        {
            var trench = new ElementQuantity
            {
                ElementId = 7, Kind = QuantityKinds.Excavation, Unit = BoqUnit.CubicMeter, Value = 4.8, Reliability = Reliability.Estimated,
                Parameters = new Dictionary<string, string> { ["Ouvrage"] = WallTrench.Work, [WallTrench.BuriedParameter] = "1.6" }
            };
            var wallConcrete = new ElementQuantity { ElementId = 7, Kind = QuantityKinds.ConcreteVolume, Unit = BoqUnit.CubicMeter, Value = 6.0 };
            var list = new List<ElementQuantity> { trench, wallConcrete };
            EarthworkBalance.Apply(list, new GeneralEarthworkSettings(), new List<BoqIssue>());
            ElementQuantity backfill = list.Single(q => q.Kind == QuantityKinds.Backfill);
            Assert.Equal(3.2, backfill.Value, 9);                  // 4,8 − 1,6 ; le beton du mur (6 m³) n'est pas enterre
        }

        [Fact]
        public void Fouilles_sous_murs_article_01_06()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            Assert.True(config.Extraction.WallTrenches.Enabled);
            Assert.Equal(0.80, config.Extraction.WallTrenches.DepthM, 9);
            var q = new List<ElementQuantity>
            {
                new ElementQuantity { ElementId = 1, Kind = QuantityKinds.Excavation, Category = "OST_Walls", Unit = BoqUnit.CubicMeter, Value = 4.8,
                    Parameters = new Dictionary<string, string> { ["Ouvrage"] = WallTrench.Work } },
                new ElementQuantity { ElementId = 2, Kind = QuantityKinds.Excavation, Category = "OST_StructuralFoundation", Unit = BoqUnit.CubicMeter, Value = 2.0 },
            };
            BoqResult boq = new BoqEngine().Build(q, config);
            Assert.Equal(4.8m, boq.AllLines.Single(l => l.ArticleCode == "01.06").NetQuantity, 6);
            Assert.Equal(2.0m, boq.AllLines.Single(l => l.ArticleCode == "01.01").NetQuantity, 6);
        }

        [Fact]
        public void Dalle_d_etage_au_nom_de_VRD_reste_en_gros_oeuvre_et_est_signalee()
        {
            var s = new VrdSettings();
            var notes = new List<BoqIssue>();
            var q = new List<ElementQuantity>
            {
                new ElementQuantity { ElementId = 1, Kind = QuantityKinds.ConcreteVolume, Category = "OST_Floors", TypeName = "Dalle parking 20", Value = 10 },
                new ElementQuantity { ElementId = 2, Kind = QuantityKinds.ConcreteVolume, Category = "OST_Floors", TypeName = "Dalle parking 20", Value = 12 },
            };
            VrdRules.Apply(q, s, notes, new HashSet<long> { 2 });
            Assert.True(q[0].Parameters.ContainsKey(VrdRules.Parameter));      // au sol : VRD
            Assert.False(q[1].Parameters.ContainsKey(VrdRules.Parameter));     // en etage : pas VRD
            Assert.Contains(notes, n => n.Code == "VRD_SOL_EN_ETAGE" && n.ElementIds.SequenceEqual(new long[] { 2 }));
            Assert.Contains(notes, n => n.Code == "VRD_TYPE_RECONNU" && n.Message.Contains("*parking*"));
        }

        [Fact]
        public void Bitume_n_est_plus_un_motif_VRD()
        {
            var s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction.Vrd;
            Assert.False(VrdRules.IsExteriorFloor("OST_Floors", "Toiture terrasse étanchéité bitume", s));
            Assert.False(VrdRules.IsExteriorFloor("OST_Floors", "Toiture terrasse étanchéité bitume", new VrdSettings()));
            Assert.True(VrdRules.IsExteriorFloor("OST_Floors", "Voirie enrobé", s));
        }
    }
}
