using System;
using System.Collections.Generic;
using System.IO;
using DanCI.BOQ.Core.Model;
using Xunit;

namespace DanCI.BOQ.Tests
{
    public class InterfaceSettingsTests
    {
        [Fact]
        public void Fond_par_defaut_redonne_le_theme_d_origine()
        {
            BackgroundShades s = BackgroundShades.Derive(null);
            Assert.Equal("#070D1A", s.Night);
            Assert.Equal("#0F1A2E", s.Panel);
            Assert.Equal("#13233D", s.Panel2);
            Assert.Equal("#1E3A5F", s.Line);
        }

        [Fact]
        public void Teintes_proposees_toutes_acceptees_et_nuances_de_plus_en_plus_claires()
        {
            foreach (BackgroundPreset p in BackgroundShades.Presets)
            {
                Assert.Null(BackgroundShades.Validate(p.Color));
                BackgroundShades s = BackgroundShades.Derive(p.Color);
                Assert.Equal(BackgroundShades.Normalize(p.Color), s.Night);
                double L(string c) { BackgroundShades.TryParse(c, out byte r, out byte g, out byte b); return BackgroundShades.Luminance(r, g, b); }
                Assert.True(L(s.Night) <= L(s.Panel) && L(s.Panel) <= L(s.Panel2) && L(s.Panel2) <= L(s.Line), p.Name);
            }
        }

        [Theory]
        [InlineData("#FFFFFF")]
        [InlineData("#C0C0C0")]
        [InlineData("#00E5FF")]
        public void Teinte_claire_refusee(string color)
        {
            Assert.Contains("trop claire", BackgroundShades.Validate(color));
            Assert.Equal("#070D1A", BackgroundShades.Derive(color).Night);   // repli sur le fond par defaut
        }

        [Theory]
        [InlineData("bleu")]
        [InlineData("#12345")]
        [InlineData("")]
        public void Saisie_invalide_refusee(string color) => Assert.Contains("#RRGGBB", BackgroundShades.Validate(color));

        [Fact]
        public void Formes_acceptees_normalisees()
        {
            Assert.Equal("#0A1F3D", BackgroundShades.Normalize("0a1f3d"));
            Assert.Equal("#0A1F3D", BackgroundShades.Normalize("#FF0A1F3D"));
        }

        [Fact]
        public void Enregistrement_et_relecture_fichier_absent_ou_trafique()
        {
            string dir = Path.Combine(Path.GetTempPath(), "danci_iface_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, InterfaceSettingsStore.FileName);
            try
            {
                Assert.Equal(InterfaceSettings.DefaultBackground, InterfaceSettingsStore.Load(path, new List<BoqIssue>()).Background);
                InterfaceSettingsStore.Save(path, new InterfaceSettings { Background = "#0A1F3D" });
                Assert.Equal("#0A1F3D", InterfaceSettingsStore.Load(path, new List<BoqIssue>()).Background);
                InterfaceSettingsStore.Save(path, new InterfaceSettings { Background = "#FFFFFF" });   // fichier modifie a la main
                Assert.Equal(InterfaceSettings.DefaultBackground, InterfaceSettingsStore.Load(path, new List<BoqIssue>()).Background);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }
    }
}
