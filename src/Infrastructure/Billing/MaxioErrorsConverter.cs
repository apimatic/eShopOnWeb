using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

/// <summary>
/// Maxio returns <c>errors</c> as either a string array or an object of field messages.
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<List<string>>
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
                    reader.Read();
                    if (reader.TokenType == JsonTokenType.String)
                    {
                        errors.Add($"{name}: {reader.GetString()}");
                    }
                    else if (reader.TokenType == JsonTokenType.StartArray)
                    {
                        var nested = JsonSerializer.Deserialize<List<string>>(ref reader, options) ?? new List<string>();
                        errors.AddRange(nested.Select(v => $"{name}: {v}"));
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
        JsonSerializer.Serialize(writer, value, options);
    }
}
