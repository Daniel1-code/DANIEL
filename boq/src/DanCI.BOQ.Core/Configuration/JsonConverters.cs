using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using DanCI.BOQ.Core.Model;
using DanCI.BOQ.Core.Rules;

namespace DanCI.BOQ.Core.Configuration
{
    /// <summary>Unites ecrites comme dans un devis : "m3", "m³", "m2", "ml", "kg", "t", "U", "Ff".</summary>
    public sealed class BoqUnitJsonConverter : JsonConverter<BoqUnit>
    {
        public override BoqUnit Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.String)
            {
                throw new JsonException("Une unité s'écrit en texte (\"m3\", \"m2\", \"ml\", \"kg\", \"t\", \"U\", \"Ff\").");
            }
            return Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, BoqUnit value, JsonSerializerOptions options) =>
            writer.WriteStringValue(value.Symbol());

        public override BoqUnit ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            Parse(reader.GetString());

        public override void WriteAsPropertyName(Utf8JsonWriter writer, BoqUnit value, JsonSerializerOptions options) =>
            writer.WritePropertyName(value.Symbol());

        private static BoqUnit Parse(string text)
        {
            if (BoqUnits.TryParse(text, out BoqUnit unit))
            {
                return unit;
            }
            throw new JsonException("Unité inconnue : « " + text + " ». Unités admises : ml, m2, m3, kg, t, U, Ff.");
        }
    }

    /// <summary>Un critere de regle : une chaine ("OST_Walls") ou un tableau (["OST_Walls", "OST_Floors"]).</summary>
    public sealed class PatternListJsonConverter : JsonConverter<PatternList>
    {
        public override PatternList Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                return new PatternList(new[] { reader.GetString() });
            }
            if (reader.TokenType != JsonTokenType.StartArray)
            {
                throw new JsonException("Un critère s'écrit en texte ou en tableau de textes.");
            }
            var items = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType != JsonTokenType.String)
                {
                    throw new JsonException("Un critère en tableau ne contient que des textes.");
                }
                items.Add(reader.GetString());
            }
            return new PatternList(items);
        }

        public override void Write(Utf8JsonWriter writer, PatternList value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (TextPattern p in value.Items)
            {
                writer.WriteStringValue(p.Pattern);
            }
            writer.WriteEndArray();
        }
    }

    public static class BoqJson
    {
        /// <summary>
        /// Options de lecture des fichiers de configuration. Une propriete inconnue est une
        /// ERREUR : une faute de frappe dans un critere ("categorei") ferait sinon une regle
        /// qui correspond a tout.
        /// </summary>
        public static JsonSerializerOptions Options { get; } = Create();

        private static JsonSerializerOptions Create()
        {
            var options = new JsonSerializerOptions
            {
                ReadCommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = false,
                UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            // Ordre significatif : le premier convertisseur qui accepte un type l'emporte, et un
            // convertisseur d'options passe AVANT l'attribut [JsonConverter] du type.
            options.Converters.Add(new BoqUnitJsonConverter());
            options.Converters.Add(new JsonStringEnumConverter(namingPolicy: null, allowIntegerValues: false));
            return options;
        }
    }
}
