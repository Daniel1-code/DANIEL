using System;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace DanCI.Structural.Core.Results
{
    /// <summary>
    /// Empreinte des donnees ayant servi a un calcul. Comparer l'empreinte stockee dans le
    /// modele a celle des donnees actuelles permet de savoir si un element doit etre
    /// recalcule : c'est le mecanisme de la commande de mise a jour.
    /// </summary>
    public sealed class DesignFingerprint
    {
        private readonly StringBuilder _content = new StringBuilder();

        public DesignFingerprint Add(string name, string value)
        {
            _content.Append(name).Append('=').Append(value ?? string.Empty).Append(';');
            return this;
        }

        public DesignFingerprint Add(string name, double value)
        {
            // Arrondi au centieme : une variation numerique insignifiante ne doit pas
            // declencher un recalcul.
            return Add(name, Math.Round(value, 2).ToString("F2", CultureInfo.InvariantCulture));
        }

        public DesignFingerprint Add(string name, int value)
        {
            return Add(name, value.ToString(CultureInfo.InvariantCulture));
        }

        public DesignFingerprint Add(string name, bool value)
        {
            return Add(name, value ? "1" : "0");
        }

        /// <summary>Empreinte hexadecimale de 16 caracteres.</summary>
        public string Compute()
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(_content.ToString()));
            var builder = new StringBuilder(16);
            for (int i = 0; i < 8; i++) builder.Append(bytes[i].ToString("x2"));
            return builder.ToString();
        }

        public override string ToString()
        {
            return Compute();
        }
    }
}
