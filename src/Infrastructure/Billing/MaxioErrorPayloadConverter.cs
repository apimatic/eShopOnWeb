using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal sealed class MaxioErrorPayloadConverter : JsonConverter<List<string>>
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
                if (reader.TokenType == JsonTokenType.String)
                {
                    var value = reader.GetString();
                    errors.Add(string.IsNullOrWhiteSpace(name) ? value ?? string.Empty : $"{name}: {value}");
                }
                else if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            var value = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(value))
                            {
                                errors.Add(string.IsNullOrWhiteSpace(name) ? value : $"{name}: {value}");
                            }
                        }
                    }
                }
                else
                {
                    reader.Skip();
                }
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

        return errors;
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
