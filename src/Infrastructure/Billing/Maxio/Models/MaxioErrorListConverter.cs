using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing.Maxio.Models;

/// <summary>
/// Maxio error payloads use either a string array or a named-field object.
/// </summary>
internal sealed class MaxioErrorListConverter : JsonConverter<IReadOnlyList<string>>
{
    public override IReadOnlyList<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
                    if (!reader.Read())
                    {
                        break;
                    }

                    switch (reader.TokenType)
                    {
                        case JsonTokenType.String:
                            var text = reader.GetString();
                            if (!string.IsNullOrWhiteSpace(text))
                            {
                                messages.Add(string.IsNullOrEmpty(name) ? text : $"{name}: {text}");
                            }
                            break;
                        case JsonTokenType.StartArray:
                            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                            {
                                if (reader.TokenType == JsonTokenType.String)
                                {
                                    var item = reader.GetString();
                                    if (!string.IsNullOrWhiteSpace(item))
                                    {
                                        messages.Add(string.IsNullOrEmpty(name) ? item : $"{name}: {item}");
                                    }
                                }
                                else
                                {
                                    reader.Skip();
                                }
                            }
                            break;
                        default:
                            reader.Skip();
                            break;
                    }
                }
                break;

            case JsonTokenType.String:
                var single = reader.GetString();
                if (!string.IsNullOrWhiteSpace(single))
                {
                    messages.Add(single);
                }
                break;

            default:
                reader.Skip();
                break;
        }

        return messages;
    }

    public override void Write(Utf8JsonWriter writer, IReadOnlyList<string> value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var item in value)
        {
            writer.WriteStringValue(item);
        }
        writer.WriteEndArray();
    }
}
