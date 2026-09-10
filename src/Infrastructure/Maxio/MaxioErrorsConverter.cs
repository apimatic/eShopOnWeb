using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Reads Maxio's <c>errors</c> field, which may be a string, an array of strings, or an object
/// whose values are strings or arrays of strings, into a flat list of messages.
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<List<string>>
{
    public override List<string> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var messages = new List<string>();
        CollectMessages(ref reader, messages);
        return messages;
    }

    private static void CollectMessages(ref Utf8JsonReader reader, List<string> messages)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.String:
                var value = reader.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                {
                    messages.Add(value!);
                }
                break;

            case JsonTokenType.StartArray:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                {
                    CollectMessages(ref reader, messages);
                }
                break;

            case JsonTokenType.StartObject:
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    // Current token is a property name; advance to its value.
                    reader.Read();
                    CollectMessages(ref reader, messages);
                }
                break;

            default:
                reader.Skip();
                break;
        }
    }

    public override void Write(Utf8JsonWriter writer, List<string> value, JsonSerializerOptions options)
        => throw new NotSupportedException();
}
