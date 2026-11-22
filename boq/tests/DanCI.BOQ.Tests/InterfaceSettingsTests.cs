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

        [Fact]
        public void Style_classique_enregistre_et_relu_valeur_inconnue_futuriste()
        {
            string dir = Path.Combine(Path.GetTempPath(), "danci_iface_" + Guid.NewGuid().ToString("N"));
            string path = Path.Combine(dir, InterfaceSettingsStore.FileName);
            try
            {
                Assert.False(InterfaceSettingsStore.Load(path, new List<BoqIssue>()).IsClassic);
                InterfaceSettingsStore.Save(path, new InterfaceSettings { Style = "CLASSIQUE" });
                Assert.True(InterfaceSettingsStore.Load(path, new List<BoqIssue>()).IsClassic);
                InterfaceSettingsStore.Save(path, new InterfaceSettings { Style = "rose" });
                Assert.Equal(InterfaceSettings.Futuristic, InterfaceSettingsStore.Load(path, new List<BoqIssue>()).Style);
            }
            finally
            {
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
            }
        }

        private static double Contrast(string a, string b)
        {
            BackgroundShades.TryParse(a, out byte r1, out byte g1, out byte b1);
            BackgroundShades.TryParse(b, out byte r2, out byte g2, out byte b2);
            double l1 = BackgroundShades.Luminance(r1, g1, b1), l2 = BackgroundShades.Luminance(r2, g2, b2);
            return (System.Math.Max(l1, l2) + 0.05) / (System.Math.Min(l1, l2) + 0.05);
        }

        [Fact]
        public void Textes_lisibles_dans_les_deux_styles()
        {
            // Contraste WCAG >= 4,5 pour le texte, >= 3 pour les couleurs d'accent.
            foreach (var (ink, bg) in new[] { (InkSet.Light, BackgroundShades.Classic), (InkSet.Dark, BackgroundShades.Default) })
            {
                Assert.True(Contrast(ink.Text, bg.Night) >= 4.5);
                Assert.True(Contrast(ink.Text, bg.Panel) >= 4.5);
                Assert.True(Contrast(ink.Muted, bg.Panel) >= 3);
                foreach (string c in new[] { ink.Cyan, ink.Green, ink.Orange, ink.Amber, ink.Red, ink.Violet, ink.Blue })
                {
                    Assert.True(Contrast(c, bg.Panel) >= 3, c);
                }
            }
        }

        [Fact]
        public void Couleurs_de_lot_assombries_lisibles_sur_fond_clair()
        {
            foreach (string lot in new[] { "00", "01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12", "NC", "DEM" })
            {
                string c = InkSet.Darken(DanCI.BOQ.Core.Dashboard.Palette.ForLot(lot));
                Assert.True(Contrast(c, "#FFFFFF") >= 3, lot + " " + c);
            }
        }
    }
}
