using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio 422 payloads use <c>errors</c> as either a string array or a field-to-message map
/// (see Customer-Error-Response / Error-List-Response in the OpenAPI spec).
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

                    if (reader.TokenType == JsonTokenType.String)
                    {
                        errors.Add($"{name}: {reader.GetString()}");
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                        {
                            if (reader.TokenType == JsonTokenType.String)
                            {
                                errors.Add($"{name}: {reader.GetString()}");
                            }
                            else
                            {
                                reader.Skip();
                            }
                        }
                    }
                    else
                    {
                        reader.Skip();
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

        return errors;
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
