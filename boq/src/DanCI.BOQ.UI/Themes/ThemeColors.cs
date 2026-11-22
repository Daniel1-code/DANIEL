using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using DanCI.BOQ.Core.Model;

namespace DanCI.BOQ.UI.Themes
{
    /// <summary>
    /// Couleur de fond choisie par l'utilisateur, appliquee a toutes les fenetres DanCI BOQ en direct
    /// (ressources dynamiques Fx.*). Lecture du fichier interface.json au premier usage.
    /// </summary>
    public static class ThemeColors
    {
        private static InterfaceSettings settings;
        private static readonly List<WeakReference<ResourceDictionary>> targets = new List<WeakReference<ResourceDictionary>>();

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

        public static BackgroundShades Shades => BackgroundShades.Derive(Settings.Background);

        /// <summary>Change le fond de toutes les fenetres ouvertes (sans enregistrer).</summary>
        public static void Preview(string background)
        {
            settings = new InterfaceSettings { Background = BackgroundShades.Normalize(background) ?? InterfaceSettings.DefaultBackground };
            BackgroundShades shades = Shades;
            lock (targets)
            {
                targets.RemoveAll(w => !w.TryGetTarget(out _));
                foreach (WeakReference<ResourceDictionary> w in targets)
                {
                    // Fenetres DanCI BOQ : toutes sur le fil d'interface de Revit.
                    if (w.TryGetTarget(out ResourceDictionary rd)) Write(rd, shades);
                }
            }
        }

        /// <summary>Enregistre le fond (interface.json) et l'applique partout.</summary>
        public static void Save(string background)
        {
            Preview(background);
            InterfaceSettingsStore.Save(InterfaceSettingsStore.DefaultPath, settings);
        }

        /// <summary>Pose les pinceaux Fx.* dans ces ressources et les tient a jour.</summary>
        public static void Attach(ResourceDictionary resources)
        {
            Write(resources, Shades);
            lock (targets)
            {
                targets.Add(new WeakReference<ResourceDictionary>(resources));
            }
        }

        /// <summary>Pose les pinceaux une fois (fenetre d'un autre fil, ex. progression), sans suivi.</summary>
        public static void ApplyOnce(ResourceDictionary resources) => Write(resources, Shades);

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

        private static void Write(ResourceDictionary rd, BackgroundShades s)
        {
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
        }
    }
}
