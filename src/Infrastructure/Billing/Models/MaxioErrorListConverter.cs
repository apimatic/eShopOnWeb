using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

/// <summary>
/// Maxio 422 bodies use either <c>{ "errors": ["msg"] }</c> or <c>{ "errors": { "field": "msg" } }</c>.
/// </summary>
internal sealed class MaxioErrorListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var errors = new List<string>();
        switch (reader.TokenType)
        {
            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        var value = reader.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            errors.Add(value);
                        }
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                break;
            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    if (reader.TokenType != JsonTokenType.PropertyName)
                    {
                        continue;
                    }

                    var name = reader.GetString();
                    if (!reader.Read())
                    {
                        break;
                    }

                    string? message = reader.TokenType switch
                    {
                        JsonTokenType.String => reader.GetString(),
                        JsonTokenType.Number => reader.GetInt64().ToString(),
                        JsonTokenType.True => "true",
                        JsonTokenType.False => "false",
                        JsonTokenType.Null => null,
                        JsonTokenType.StartObject or JsonTokenType.StartArray => SkipAndFormat(ref reader, name),
                        _ => null
                    };

                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(message))
                    {
                        errors.Add($"{name}: {message}");
                    }
                }
                break;
            case JsonTokenType.String:
                var single = reader.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    errors.Add(single);
                }
                break;
            default:
                reader.Skip();
                break;
        }

        return errors.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static string SkipAndFormat(ref Utf8JsonReader reader, string? name)
    {
        reader.Skip();
        return string.IsNullOrWhiteSpace(name) ? "invalid" : "invalid";
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var error in value)
        {
            writer.WriteStringValue(error);
        }
        writer.WriteEndArray();
    }
}
