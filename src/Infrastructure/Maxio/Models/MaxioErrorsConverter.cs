using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Billing API error payloads are either a string array or a keyed object of messages.
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return Array.Empty<string>();
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? new List<string>();
            return list;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            using var doc = JsonDocument.ParseValue(ref reader);
            var messages = new List<string>();
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                messages.AddRange(Flatten(property.Name, property.Value));
            }

            return messages;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new[] { reader.GetString() ?? string.Empty };
        }

        reader.Skip();
        return Array.Empty<string>();
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }

    private static IEnumerable<string> Flatten(string key, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.String:
                yield return $"{key}: {element.GetString()}";
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String)
                    {
                        yield return $"{key}: {item.GetString()}";
                    }
                    else
                    {
                        yield return $"{key}: {item.GetRawText()}";
                    }
                }
                break;
            default:
                yield return $"{key}: {element.GetRawText()}";
                break;
        }
    }
}
