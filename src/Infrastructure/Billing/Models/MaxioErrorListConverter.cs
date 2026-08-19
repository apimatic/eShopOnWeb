using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Models;

/// <summary>
/// Maxio returns <c>errors</c> as either a string array or an object of field messages
/// (see Customer-Error-Response in the OpenAPI spec).
/// </summary>
internal sealed class MaxioErrorListConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var errors = new List<string>();

        if (reader.TokenType == JsonTokenType.StartArray)
        {
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

            return errors;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName)
                {
                    continue;
                }

                var name = reader.GetString();
                reader.Read();
                string? message;
                if (reader.TokenType == JsonTokenType.String)
                {
                    message = reader.GetString();
                }
                else if (reader.TokenType == JsonTokenType.Number)
                {
                    message = reader.GetInt64().ToString();
                }
                else
                {
                    message = reader.TokenType.ToString();
                    reader.Skip();
                }
                errors.Add($"{name}: {message}");
            }

            return errors;
        }

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
