using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

// Retain the legacy application's plain-field JSON representation while using
// the supported serializer in the bundled modern .NET runtime.
namespace System.Web.Script.Serialization
{
    public sealed class JavaScriptSerializer
    {
        public int MaxJsonLength { get; set; } = 2097152;
        private static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
            IncludeFields = true, PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyProperties = true, Converters = { new LegacyObjectConverter() }
        };
        public T Deserialize<T>(string text)
        {
            if (text == null || text.Length > MaxJsonLength) throw new InvalidDataException("JSON exceeds the permitted size.");
            return JsonSerializer.Deserialize<T>(text, Options);
        }
        public string Serialize(object value)
        {
            string text = JsonSerializer.Serialize(value, Options);
            if (text.Length > MaxJsonLength) throw new InvalidDataException("JSON exceeds the permitted size.");
            return text;
        }
        private sealed class LegacyObjectConverter : JsonConverter<object>
        {
            public override object Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
            { using (var document = JsonDocument.ParseValue(ref reader)) return ConvertElement(document.RootElement); }
            private static object ConvertElement(JsonElement value)
            {
                switch (value.ValueKind)
                {
                    case JsonValueKind.Object:
                        var dictionary = new Dictionary<string, object>();
                        foreach (var property in value.EnumerateObject()) dictionary.Add(property.Name, ConvertElement(property.Value));
                        return dictionary;
                    case JsonValueKind.Array:
                        var items = new List<object>();
                        foreach (var item in value.EnumerateArray()) items.Add(ConvertElement(item));
                        return items.ToArray();
                    case JsonValueKind.String: return value.GetString();
                    case JsonValueKind.True: return true;
                    case JsonValueKind.False: return false;
                    case JsonValueKind.Number:
                        int number; if (value.TryGetInt32(out number)) return number;
                        long large; if (value.TryGetInt64(out large)) return large;
                        return value.GetDouble();
                    default: return null;
                }
            }
            public override void Write(Utf8JsonWriter writer, object value, JsonSerializerOptions options)
            {
                if (value == null) { writer.WriteNullValue(); return; }
                if (value.GetType() == typeof(object)) { writer.WriteStartObject(); writer.WriteEndObject(); return; }
                JsonSerializer.Serialize(writer, value, value.GetType(), options);
            }
        }
    }
}
