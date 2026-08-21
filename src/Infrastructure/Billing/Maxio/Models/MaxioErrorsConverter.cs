using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;

/// <summary>
/// Maxio error bodies are either <c>{ "errors": ["msg"] }</c> or
/// <c>{ "errors": { "customer": "can't be blank" } }</c> (Customer-Error-Response).
/// </summary>
public sealed class MaxioErrorsConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var messages = new List<string>();
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
                            messages.Add(value);
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
                        messages.Add($"{name}: {reader.GetString()}");
                    }
                    else
                    {
                        reader.Skip();
                    }
                }
                break;
            case JsonTokenType.String:
                {
                    var value = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        messages.Add(value);
                    }
                    break;
                }
            default:
                reader.Skip();
                break;
        }

        return messages;
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
