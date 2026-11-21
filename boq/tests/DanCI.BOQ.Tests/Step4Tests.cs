using System;
using System.Collections.Generic;
using System.Linq;
using DanCI.BOQ.Core.Configuration;
using DanCI.BOQ.Core.Engine;
using DanCI.BOQ.Core.Extraction;
using DanCI.BOQ.Core.Model;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class FormworkFaceRuleTests
    {
        private static readonly FormworkSettings Defaults = new FormworkSettings();

        private static double Nz(double slopeFromHorizontalDeg, bool up) =>
            (up ? 1 : -1) * Math.Cos(slopeFromHorizontalDeg * Math.PI / 180.0);

        [Fact]
        public void Face_verticale_coffree()
        {
            Assert.Equal(FormworkFaceRole.Side, FormworkFaceRule.Classify(0.0, false, false, Defaults));
        }

        [Fact]
        public void Dessus_de_dalle_non_coffre_sous_face_coffree()
        {
            Assert.Equal(FormworkFaceRole.Top, FormworkFaceRule.Classify(1.0, false, false, Defaults));
            Assert.Equal(FormworkFaceRole.Soffit, FormworkFaceRule.Classify(-1.0, false, false, Defaults));
        }

        [Fact]
        public void Le_seuil_de_pente_des_dessus()
        {
            Assert.Equal(FormworkFaceRole.Top, FormworkFaceRule.Classify(Nz(20, true), false, false, Defaults));
            Assert.Equal(FormworkFaceRole.Side, FormworkFaceRule.Classify(Nz(45, true), false, false, Defaults));
        }

        [Fact]
        public void Sous_face_de_fondation_et_de_dallage_contre_le_sol()
        {
            Assert.Equal(FormworkFaceRole.AgainstSoil, FormworkFaceRule.Classify(-1.0, true, false, Defaults));
            Assert.Equal(FormworkFaceRole.AgainstSoil, FormworkFaceRule.Classify(-1.0, false, true, Defaults));
            Assert.Equal(FormworkFaceRole.Side, FormworkFaceRule.Classify(0.0, true, false, Defaults));
        }

        [Fact]
        public void Fondations_coulees_pleine_fouille()
        {
            var s = new FormworkSettings { FoundationsCastAgainstSoil = true };
            Assert.Equal(FormworkFaceRole.AgainstSoil, FormworkFaceRule.Classify(0.0, true, false, s));
            Assert.Equal(FormworkFaceRole.Side, FormworkFaceRule.Classify(0.0, false, false, s));
        }

        [Fact]
        public void Sous_face_de_fondation_coffree_si_option_desactivee()
        {
            var s = new FormworkSettings { ExcludeFoundationBottoms = false };
            Assert.Equal(FormworkFaceRole.Soffit, FormworkFaceRule.Classify(-1.0, true, false, s));
        }
    }

    public class RebarRatioTests
    {
        private static readonly List<RebarRatio> Ratios = new List<RebarRatio>
        {
            new RebarRatio { Category = TestData.List("OST_StructuralColumns"), Designation = "poteaux", KgPerM3 = 150m, NeedsValidation = true }
        };

        [Fact]
        public void Element_sans_armature_modelisee_estime_par_ratio()
        {
            var column = TestData.Concrete(10, 0.27);
            List<ElementQuantity> r = RebarRatioRule.Apply(new[] { column }, new HashSet<string>(), Ratios);

            ElementQuantity q = Assert.Single(r);
            Assert.Equal(QuantityKinds.Reinforcement, q.Kind);
            Assert.Equal(BoqUnit.Kilogram, q.Unit);
            Assert.Equal(40.5, q.Value, 9);
            Assert.Equal(Reliability.Estimated, q.Reliability);
            Assert.Contains("ESTIMÉ (ratio)", q.Assumption);
            Assert.Contains("150 kg/m³", q.Assumption);
            Assert.Contains("à valider", q.Assumption);
            Assert.Equal(10, q.ElementId);
            Assert.Equal("OST_StructuralColumns", q.Parameters["HoteCategorie"]);
        }

        [Fact]
        public void Element_portant_une_armature_modelisee_jamais_estime()
        {
            var column = TestData.Concrete(10, 0.27);
            var hosts = new HashSet<string> { RebarRatioRule.HostKey("", 10) };
            Assert.Empty(RebarRatioRule.Apply(new[] { column }, hosts, Ratios));
        }

        [Fact]
        public void Categorie_sans_ratio_donne_une_quantite_manquante_jamais_inventee()
        {
            var beam = TestData.Concrete(11, 1.0, "OST_StructuralFraming");
            ElementQuantity q = Assert.Single(RebarRatioRule.Apply(new[] { beam }, new HashSet<string>(), Ratios));
            Assert.Equal(Reliability.Missing, q.Reliability);
            Assert.Equal(0, q.Value);
        }

        [Fact]
        public void Bout_en_bout_le_ratio_d_une_fondation_va_dans_le_lot_fondations()
        {
            BoqConfiguration config = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration;
            var footing = TestData.Concrete(20, 2.0, "OST_StructuralFoundation");
            List<ElementQuantity> rebar = RebarRatioRule.Apply(new[] { footing }, new HashSet<string>(), config.Extraction.RebarRatios);

            BoqResult r = new BoqEngine().Build(new[] { footing }.Concat(rebar), config);
            BoqLine line = r.AllLines.Single(l => l.Unit == BoqUnit.Kilogram);
            Assert.Equal("02.05", line.Code);
            Assert.Equal(Reliability.Estimated, line.Reliability);
            Assert.Equal(120m, line.NetQuantity); // 2 m³ x 60 kg/m³
        }
    }

    public class SteelTests
    {
        [Theory]
        [InlineData("IPE 200", "IPE200")]
        [InlineData("IPE200", "IPE200")]
        [InlineData("Poutre IPE-270", "IPE270")]
        [InlineData("HE200A", "HEA200")]
        [InlineData("HE 300 B", "HEB300")]
        [InlineData("HEA240", "HEA240")]
        [InlineData("UPN 160", "UPN160")]
        [InlineData("Tube 100x100x5", null)]
        [InlineData("L 50x50x5", null)]
        [InlineData("", null)]
        public void Designation_reconnue_jamais_devinee(string typeName, string expected)
        {
            Assert.Equal(expected, SteelProfileName.Normalize(typeName));
        }

        private static SteelProfileTable Table() =>
            ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.SteelProfiles;

        [Fact]
        public void Priorite_au_poids_du_type()
        {
            SteelMass m = SteelMassRule.Resolve("IPE200", 22.0, 6.0, 0.0172, Table(), new SteelSettings());
            Assert.Equal(132.0, m.Kg, 9);
            Assert.Equal("CUT_LENGTH_X_TYPE_MASS", m.MethodCode);
            Assert.Equal(Reliability.Measured, m.Reliability);
        }

        [Fact]
        public void Sinon_la_table_des_profils()
        {
            SteelMass m = SteelMassRule.Resolve("IPE 200", null, 6.0, 0.0172, Table(), new SteelSettings());
            Assert.Equal(6.0 * 22.4, m.Kg, 9);
            Assert.Equal("CUT_LENGTH_X_TABLE_MASS", m.MethodCode);
            Assert.Contains("IPE200", m.MethodDescription);
        }

        [Fact]
        public void A_defaut_le_volume_et_c_est_une_estimation()
        {
            SteelMass m = SteelMassRule.Resolve("Tube 100x100x5", null, 6.0, 0.0111, Table(), new SteelSettings());
            Assert.Equal(0.0111 * 7850, m.Kg, 9);
            Assert.Equal(Reliability.Estimated, m.Reliability);
            Assert.NotNull(m.Assumption);
        }

        [Fact]
        public void Rien_de_lisible_donne_une_quantite_manquante()
        {
            SteelMass m = SteelMassRule.Resolve("Tube", null, null, null, Table(), new SteelSettings());
            Assert.Equal(Reliability.Missing, m.Reliability);
            Assert.Equal(0, m.Kg);
        }

        [Fact]
        public void La_table_livree_croit_dans_chaque_serie_et_ses_cles_sont_normalisees()
        {
            SteelProfileTable t = Table();
            foreach (string series in new[] { "IPE", "HEA", "HEB", "UPN" })
            {
                var masses = t.KgPerMeter.Where(p => p.Key.StartsWith(series, StringComparison.Ordinal))
                    .OrderBy(p => int.Parse(p.Key.Substring(3)))
                    .Select(p => p.Value)
                    .ToList();
                Assert.True(masses.Count >= 12, series);
                for (int i = 1; i < masses.Count; i++)
                {
                    Assert.True(masses[i] > masses[i - 1], series + " : masse non croissante au rang " + i);
                }
            }
            Assert.All(t.KgPerMeter.Keys, k => Assert.Equal(k, SteelProfileName.Normalize(k)));
        }

        [Fact]
        public void Cle_de_profil_non_normalisee_refusee()
        {
            var config = TestData.Config();
            config.SteelProfiles.KgPerMeter["IPE 200"] = 22.4m;
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "PROFIL_NOM");
        }

        [Fact]
        public void Part_des_assemblages_saisie_en_pourcentage_entier_refusee()
        {
            var config = TestData.Config();
            config.Extraction.Steel.ConnectionsShare = 10m;
            Assert.Contains(ConfigurationValidator.Validate(config), i => i.Code == "ASSEMBLAGES_PART");
        }
    }

    public class ShippedStep4ConfigurationTests
    {
        [Fact]
        public void Ratios_livres_tous_a_valider_et_couvrant_les_categories_beton()
        {
            ExtractionSettings s = ConfigurationLoader.LoadDirectory(TestData.ConfigDirectory()).Configuration.Extraction;
            Assert.All(s.RebarRatios, r => Assert.True(r.NeedsValidation));
            foreach (string cat in new[] { "OST_StructuralFoundation", "OST_StructuralColumns", "OST_StructuralFraming", "OST_Floors", "OST_Walls", "OST_StairsRuns" })
            {
                Assert.Contains(s.RebarRatios, r => r.Category.IsMatch(cat));
            }
            Assert.Equal(0.10m, s.Steel.ConnectionsShare);
            Assert.Equal(30.0, s.Formwork.MaxUncoveredTopSlopeDeg);
        }
    }
}
