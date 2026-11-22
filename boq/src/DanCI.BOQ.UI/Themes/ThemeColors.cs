using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.UI.Themes
{
    /// <summary>
    /// Apparence choisie par l'utilisateur (Parametres > Interface) : style futuriste (sombre, fond au
    /// choix) ou classique (clair, controles Windows, comme les premieres versions). Appliquee en direct
    /// a toutes les fenetres DanCI BOQ par des ressources dynamiques Fx.*.
    /// </summary>
    public static class ThemeColors
    {
        private static InterfaceSettings settings;
        private static readonly List<(WeakReference<ResourceDictionary> Target, bool DarkOnly)> targets =
            new List<(WeakReference<ResourceDictionary>, bool)>();

        /// <summary>Le style (futuriste / classique) a change : les fenetres ajoutent ou retirent le theme des controles.</summary>
        public static event EventHandler StyleChanged;

        public static InterfaceSettings Settings
        {
            get
            {
                if (settings == null)
                {
                    try
                    {
                        settings = InterfaceSettingsStore.Load(InterfaceSettingsStore.DefaultPath, new List<BoqIssue>());
                    }
                    catch (Exception)
                    {
                        settings = InterfaceSettings.Default();
                    }
                }
                return settings;
            }
        }

        public static bool IsClassic => Settings.IsClassic;

        /// <summary>Nuances du fond des fenetres (claires en style classique).</summary>
        public static BackgroundShades Shades => IsClassic ? BackgroundShades.Classic : BackgroundShades.Derive(Settings.Background);

        /// <summary>Nuances des zones toujours sombres (tableau de bord, panneau Diagramme).</summary>
        public static BackgroundShades DarkShades => IsClassic ? BackgroundShades.Default : BackgroundShades.Derive(Settings.Background);

        public static InkSet Ink => IsClassic ? InkSet.Light : InkSet.Dark;

        /// <summary>Applique sans enregistrer.</summary>
        public static void Preview(string background, string style)
        {
            bool wasClassic = IsClassic;
            settings = new InterfaceSettings
            {
                Background = BackgroundShades.Normalize(background) ?? InterfaceSettings.DefaultBackground,
                Style = string.Equals(style, InterfaceSettings.Classic, StringComparison.OrdinalIgnoreCase) ? InterfaceSettings.Classic : InterfaceSettings.Futuristic
            };
            lock (targets)
            {
                targets.RemoveAll(t => !t.Target.TryGetTarget(out _));
                foreach (var t in targets)
                {
                    // Fenetres DanCI BOQ : toutes sur le fil d'interface de Revit.
                    if (t.Target.TryGetTarget(out ResourceDictionary rd)) Write(rd, t.DarkOnly);
                }
            }
            if (wasClassic != IsClassic) StyleChanged?.Invoke(null, EventArgs.Empty);
        }

        /// <summary>Enregistre (interface.json) et applique partout.</summary>
        public static void Save(string background, string style)
        {
            Preview(background, style);
            InterfaceSettingsStore.Save(InterfaceSettingsStore.DefaultPath, settings);
        }

        /// <summary>Pose les pinceaux Fx.* dans ces ressources et les tient a jour.</summary>
        /// <param name="darkOnly">Zone toujours sombre (tableau de bord, panneau Diagramme), meme en style classique.</param>
        public static void Attach(ResourceDictionary resources, bool darkOnly = false)
        {
            Write(resources, darkOnly);
            lock (targets)
            {
                targets.Add((new WeakReference<ResourceDictionary>(resources), darkOnly));
            }
        }

        /// <summary>Pose les pinceaux une fois (fenetre d'un autre fil, ex. progression), sans suivi.</summary>
        public static void ApplyOnce(ResourceDictionary resources) => Write(resources, false);

        public static Brush Brush(string hex)
        {
            var b = new SolidColorBrush(Color(hex));
            b.Freeze();
            return b;
        }

        public static Color Color(string hex)
        {
            BackgroundShades.TryParse(hex, out byte r, out byte g, out byte b);
            return System.Windows.Media.Color.FromRgb(r, g, b);
        }

        private static void Write(ResourceDictionary rd, bool darkOnly)
        {
            BackgroundShades s = darkOnly ? DarkShades : Shades;
            InkSet ink = darkOnly ? InkSet.Dark : Ink;
            rd["Fx.Night"] = Brush(s.Night);
            rd["Fx.Strip"] = Brush(s.Strip);
            rd["Fx.Field"] = Brush(s.Field);
            rd["Fx.Panel"] = Brush(s.Panel);
            rd["Fx.Alt"] = Brush(s.Alternate);
            rd["Fx.Panel2"] = Brush(s.Panel2);
            rd["Fx.Select"] = Brush(s.Select);
            rd["Fx.Line"] = Brush(s.Line);
            rd["Fx.NightColor"] = Color(s.Night);
            rd["Fx.GlowColor"] = Color(s.Glow);
            rd["Fx.Text"] = Brush(ink.Text);
            rd["Fx.Muted"] = Brush(ink.Muted);
            rd["Fx.Ink.Strong"] = Brush(ink.Strong);
            rd["Fx.Ink.Cyan"] = Brush(ink.Cyan);
            rd["Fx.Ink.Green"] = Brush(ink.Green);
            rd["Fx.Ink.Orange"] = Brush(ink.Orange);
            rd["Fx.Ink.Amber"] = Brush(ink.Amber);
            rd["Fx.Ink.Red"] = Brush(ink.Red);
            rd["Fx.Ink.Violet"] = Brush(ink.Violet);
            rd["Fx.Ink.Blue"] = Brush(ink.Blue);
            rd["Fx.Unclassified"] = Brush(ink.Unclassified);
        }
    }

    /// <summary>
    /// Couleur vive calculee (statut, lot, gravite) : telle quelle en style futuriste, assombrie en
    /// style classique pour rester lisible sur fond clair (evaluee a l'affichage de la ligne).
    /// </summary>
    public sealed class InkConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            string c = value as string ?? (value as SolidColorBrush)?.Color.ToString();
            if (string.IsNullOrEmpty(c)) return value;
            return ThemeColors.Brush(ThemeColors.IsClassic ? InkSet.Darken(c) : c);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
    }
}
