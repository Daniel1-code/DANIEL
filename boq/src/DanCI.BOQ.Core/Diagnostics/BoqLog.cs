using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace DanCI.BOQ.Core.Diagnostics
{
    /// <summary>
    /// Journal fichier minimal, sans dependance : %AppData%\DanCI\BOQ\logs\boq-AAAA-MM-JJ.log.
    /// Regle : le journal ne doit JAMAIS faire echouer une commande. Toute erreur d'ecriture
    /// est avalee (un disque plein ne doit pas empecher d'ouvrir Revit).
    /// </summary>
    public static class BoqLog
    {
        private static readonly object Gate = new object();
        private static string directory = DefaultDirectory;

        public static string DefaultDirectory =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "DanCI", "BOQ", "logs");

        /// <summary>Dossier courant du journal.</summary>
        public static string Directory => directory;

        /// <summary>Fichier du jour.</summary>
        public static string CurrentFile =>
            Path.Combine(directory, "boq-" + DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ".log");

        /// <summary>Change le dossier du journal (tests, diagnostic).</summary>
        public static void UseDirectory(string path)
        {
            lock (Gate)
            {
                directory = string.IsNullOrWhiteSpace(path) ? DefaultDirectory : path;
            }
        }

        public static void Info(string message) => Write("INFO ", message, null);

        public static void Warn(string message) => Write("WARN ", message, null);

        public static void Error(string message, Exception exception = null) => Write("ERROR", message, exception);

        private static void Write(string level, string message, Exception exception)
        {
            try
            {
                var text = new StringBuilder();
                text.Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture))
                    .Append(' ').Append(level).Append(' ').Append(message);
                if (exception != null)
                {
                    text.AppendLine().Append(exception);
                }
                text.AppendLine();

                lock (Gate)
                {
                    System.IO.Directory.CreateDirectory(directory);
                    File.AppendAllText(CurrentFile, text.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Volontairement silencieux : voir la regle en tete de classe.
            }
        }
    }
}
