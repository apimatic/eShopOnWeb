using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio returns errors as a string array or as a map of field → messages.
/// Flatten either shape into a single readable string.
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return Flatten(ref reader);
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(value);
    }

    internal static string Flatten(ref Utf8JsonReader reader)
    {
        var messages = new List<string>();
        Collect(ref reader, messages);
        return messages.Count == 0 ? "Maxio request failed." : string.Join("; ", messages.Distinct());
    }

    private static void Collect(ref Utf8JsonReader reader, List<string> messages)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var text = reader.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    messages.Add(text);
                }
                break;
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    Collect(ref reader, messages);
                }
                break;
            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType == JsonTokenType.PropertyName)
                    {
                        reader.Read();
                    }
                    Collect(ref reader, messages);
                }
                break;
        }
    }
}
