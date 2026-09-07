using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace DanCI.Structural.Core.Settings
{
    /// <summary>Une configuration nommee, rechargeable en un clic.</summary>
    public sealed class Preset<T>
    {
        public string Name { get; set; }
        public T Settings { get; set; }
    }

    /// <summary>
    /// Enregistre les configurations de l'utilisateur dans son profil Windows, sous
    /// %APPDATA%\DanCI Structural Studio. Un fichier par famille de reglages.
    /// Ne leve jamais d'exception a la lecture : une configuration illisible est ignoree.
    /// </summary>
    public sealed class PresetStore<T>
    {
        private readonly string _fileName;

        public PresetStore(string fileName)
        {
            _fileName = fileName;
        }

        public static string Folder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "DanCI Structural Studio");
            }
        }

        public string FilePath
        {
            get { return Path.Combine(Folder, _fileName); }
        }

        public List<Preset<T>> Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return new List<Preset<T>>();
                var presets = JsonSerializer.Deserialize<List<Preset<T>>>(File.ReadAllText(FilePath));
                if (presets == null) return new List<Preset<T>>();
                presets.RemoveAll(p => p == null || string.IsNullOrWhiteSpace(p.Name) || p.Settings == null);
                return presets;
            }
            catch (Exception)
            {
                return new List<Preset<T>>();
            }
        }

        public bool Save(List<Preset<T>> presets, out string error)
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

        public bool AddOrReplace(string name, T settings, out string error)
        {
            List<Preset<T>> presets = Load();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            presets.Add(new Preset<T> { Name = name, Settings = settings });
            return Save(presets, out error);
        }

        public bool Remove(string name, out string error)
        {
            List<Preset<T>> presets = Load();
            presets.RemoveAll(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            return Save(presets, out error);
        }
    }
}
