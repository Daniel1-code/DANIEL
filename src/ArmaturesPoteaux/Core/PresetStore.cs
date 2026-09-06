using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ArmaturesPoteaux.Core
{
    /// <summary>Une configuration nommee, rechargeable en un clic.</summary>
    public class Preset
    {
        public string Name { get; set; }
        public DesignInput Input { get; set; }
    }

    /// <summary>
    /// Enregistre les configurations de l'utilisateur dans son profil Windows, pour
    /// qu'un poteau courant, un poteau sismique ou un poteau fortement charge se
    /// reglent en un clic au lieu d'une dizaine de champs.
    /// </summary>
    public static class PresetStore
    {
        private static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "ArmaturesPoteaux");
            }
        }

        private static string FilePath
        {
            get { return Path.Combine(Folder, "presets.json"); }
        }

        /// <summary>Configurations livrees avec le plugin, toujours proposees en tete de liste.</summary>
        public static List<Preset> BuiltIn()
        {
            var courant = new DesignInput();

            var sismique = new DesignInput
            {
                Seismic = true,
                TargetRatioPercent = 1.2,
                UseCriticalZones = true,
                AddCrossTies = true,
                CoverMm = 35.0
            };

            var charge = new DesignInput
            {
                ConcreteStrengthMPa = 35.0,
                TargetRatioPercent = 2.0,
                CoverMm = 35.0
            };

            var verifie = new DesignInput
            {
                VerifyCapacity = true,
                AxialLoadKn = 1000.0,
                MomentAboutXKnm = 50.0,
                BucklingFactor = 0.7
            };

            return new List<Preset>
            {
                new Preset { Name = "Poteau courant (C25/30, 1 %)", Input = courant },
                new Preset { Name = "Poteau sismique (zones critiques allongees)", Input = sismique },
                new Preset { Name = "Poteau fortement charge (C35/45, 2 %)", Input = charge },
                new Preset { Name = "Poteau verifie N-M (exemple a adapter)", Input = verifie }
            };
        }

        /// <summary>Configurations enregistrees par l'utilisateur. Ne leve jamais d'exception.</summary>
        public static List<Preset> LoadUserPresets()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Preset>();
                string json = File.ReadAllText(FilePath);
                var presets = JsonSerializer.Deserialize<List<Preset>>(json);
                if (presets == null) return new List<Preset>();
                presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Input == null);
                return presets;
            }
            catch (Exception)
            {
                return new List<Preset>();
            }
        }

        /// <summary>Enregistre la liste complete des configurations utilisateur.</summary>
        public static bool SaveUserPresets(List<Preset> presets, out string error)
        {
            error = null;
            try
            {
                Directory.CreateDirectory(Folder);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(FilePath, JsonSerializer.Serialize(presets, options));
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Ajoute ou remplace une configuration utilisateur.</summary>
        public static bool AddOrReplace(string name, DesignInput input, out string error)
        {
            List<Preset> presets = LoadUserPresets();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            presets.Add(new Preset { Name = name, Input = input });
            return SaveUserPresets(presets, out error);
        }

        /// <summary>Supprime une configuration utilisateur.</summary>
        public static bool Remove(string name, out string error)
        {
            List<Preset> presets = LoadUserPresets();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return SaveUserPresets(presets, out error);
        }
    }
}
