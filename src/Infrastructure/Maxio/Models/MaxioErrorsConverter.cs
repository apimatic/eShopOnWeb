using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio.Models;

/// <summary>
/// Maxio error bodies are either <c>{ "errors": ["msg"] }</c> (Error-List-Response)
/// or <c>{ "errors": { "field": "msg" } }</c> (Customer-Error-Response oneOf).
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<string[]?>
{
    public override string[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
        {
            return null;
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var list = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
            {
                if (reader.TokenType == JsonTokenType.String)
                {
                    var value = reader.GetString();
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        list.Add(value);
                    }
                }
                else
                {
                    reader.Skip();
                }
            }
            return list.ToArray();
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var list = new List<string>();
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
                    list.Add($"{name}: {reader.GetString()}");
                }
                else if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            list.Add($"{name}: {reader.GetString()}");
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
            return list.ToArray();
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, string[]? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
