using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using DanCI.BOQ.Core.Configuration;

namespace DanCI.BOQ.Core.Model
{
    /// <summary>
    /// Preferences d'affichage de l'utilisateur (toutes les fenetres DanCI BOQ, tous les projets) :
    /// %AppData%\DanCI\BOQ\interface.json. N'a aucun effet sur le BOQ, le PDF ou Excel.
    /// </summary>
    public sealed class InterfaceSettings
    {
        public const string DefaultBackground = "#070D1A";

        /// <summary>Couleur de fond #RRGGBB.</summary>
        public string Background { get; set; } = DefaultBackground;

        public static InterfaceSettings Default() => new InterfaceSettings();
    }

    /// <summary>Fond propose dans les parametres (tous sombres : le texte de l'interface est clair).</summary>
    public sealed class BackgroundPreset
    {
        public BackgroundPreset(string name, string color)
        {
            Name = name;
            Color = color;
        }

        public string Name { get; }
        public string Color { get; }
    }

    /// <summary>
    /// Nuances derivees d'une couleur de fond : fond de fenetre, panneaux, champs, filets.
    /// Calcul pur (texte #RRGGBB) ; le fond par defaut redonne exactement le theme d'origine.
    /// </summary>
    public sealed class BackgroundShades
    {
        public string Night { get; private set; }
        public string Strip { get; private set; }
        public string Field { get; private set; }
        public string Panel { get; private set; }
        public string Alternate { get; private set; }
        public string Panel2 { get; private set; }
        public string Select { get; private set; }
        public string Line { get; private set; }
        /// <summary>Haut du degrade du tableau de bord.</summary>
        public string Glow { get; private set; }

        /// <summary>Luminance relative maximale acceptee (au-dela, le texte clair devient illisible).</summary>
        public const double MaxLuminance = 0.06;

        public static IReadOnlyList<BackgroundPreset> Presets { get; } = new[]
        {
            new BackgroundPreset("Nuit (par défaut)", InterfaceSettings.DefaultBackground),
            new BackgroundPreset("Bleu marine", "#0A1F3D"),
            new BackgroundPreset("Ardoise", "#1A2430"),
            new BackgroundPreset("Anthracite", "#16181D"),
            new BackgroundPreset("Noir", "#000000"),
            new BackgroundPreset("Violet nuit", "#160C2A"),
            new BackgroundPreset("Vert forêt", "#0A2018"),
            new BackgroundPreset("Bordeaux", "#260B14"),
        };

        public static BackgroundShades Default { get; } = Derive(InterfaceSettings.DefaultBackground);

        /// <summary>Nuances d'un fond valide (sinon celles du fond par defaut).</summary>
        public static BackgroundShades Derive(string background)
        {
            if (!TryParse(background, out byte r, out byte g, out byte b) || Validate(background) != null)
            {
                TryParse(InterfaceSettings.DefaultBackground, out r, out g, out b);
            }
            if (Hex(r, g, b) == InterfaceSettings.DefaultBackground)
            {
                // Theme d'origine a l'identique.
                return new BackgroundShades
                {
                    Night = "#070D1A", Strip = "#0B1426", Field = "#0A1426", Panel = "#0F1A2E", Alternate = "#122038",
                    Panel2 = "#13233D", Select = "#1B4A73", Line = "#1E3A5F", Glow = "#0F2140"
                };
            }
            return new BackgroundShades
            {
                Night = Hex(r, g, b),
                Strip = Lighten(r, g, b, 0.025),
                Field = Lighten(r, g, b, 0.02),
                Panel = Lighten(r, g, b, 0.04),
                Alternate = Lighten(r, g, b, 0.065),
                Panel2 = Lighten(r, g, b, 0.08),
                Select = Lighten(r, g, b, 0.22),
                Line = Lighten(r, g, b, 0.15),
                Glow = Lighten(r, g, b, 0.06)
            };
        }

        /// <summary>Null si la couleur convient ; sinon la raison, en francais.</summary>
        public static string Validate(string background)
        {
            if (!TryParse(background, out byte r, out byte g, out byte b))
            {
                return "Couleur non reconnue : saisissez-la au format #RRGGBB (exemple : #0A1F3D).";
            }
            double l = Luminance(r, g, b);
            if (l > MaxLuminance)
            {
                return "Couleur trop claire (luminance " + (l * 100).ToString("0", CultureInfo.GetCultureInfo("fr-FR")) +
                       " %, maximum " + (MaxLuminance * 100).ToString("0", CultureInfo.InvariantCulture) +
                       " %) : le texte clair de l'interface deviendrait illisible. Choisissez une teinte plus sombre.";
            }
            return null;
        }

        /// <summary>#RRGGBB, RRGGBB ou #AARRGGBB (l'opacite est ignoree) ; renvoie la forme #RRGGBB.</summary>
        public static string Normalize(string color) =>
            TryParse(color, out byte r, out byte g, out byte b) ? Hex(r, g, b) : null;

        public static bool TryParse(string color, out byte r, out byte g, out byte b)
        {
            r = g = b = 0;
            string h = (color ?? string.Empty).Trim().TrimStart('#');
            if (h.Length == 8) h = h.Substring(2);
            if (h.Length != 6) return false;
            return byte.TryParse(h.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out r)
                && byte.TryParse(h.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out g)
                && byte.TryParse(h.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out b);
        }

        /// <summary>Luminance relative WCAG (0 = noir, 1 = blanc).</summary>
        public static double Luminance(byte r, byte g, byte b)
        {
            double Lin(byte c)
            {
                double s = c / 255.0;
                return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Lin(r) + 0.7152 * Lin(g) + 0.0722 * Lin(b);
        }

        private static string Lighten(byte r, byte g, byte b, double f) =>
            Hex((byte)Math.Round(r + (255 - r) * f), (byte)Math.Round(g + (255 - g) * f), (byte)Math.Round(b + (255 - b) * f));

        private static string Hex(byte r, byte g, byte b) => "#" + r.ToString("X2") + g.ToString("X2") + b.ToString("X2");
    }

    public static class InterfaceSettingsStore
    {
        public const string FileName = "interface.json";

        public static string DefaultPath =>
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DanCI", "BOQ", FileName);

        /// <summary>Fichier absent ou illisible : reglages par defaut (une couleur invalide est ignoree).</summary>
        public static InterfaceSettings Load(string path, List<BoqIssue> issues)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return InterfaceSettings.Default();
            InterfaceSettings s;
            try
            {
                s = ConfigurationLoader.ReadText<InterfaceSettings>(File.ReadAllText(path), FileName, issues);
            }
            catch (IOException)
            {
                return InterfaceSettings.Default();
            }
            if (s == null) return InterfaceSettings.Default();
            if (BackgroundShades.Validate(s.Background) != null) s.Background = InterfaceSettings.DefaultBackground;
            s.Background = BackgroundShades.Normalize(s.Background);
            return s;
        }

        public static void Save(string path, InterfaceSettings settings)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temp = path + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(settings, BoqJson.Options), new UTF8Encoding(false));
            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }
    }
}
