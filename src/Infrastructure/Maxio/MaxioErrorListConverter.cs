using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

/// <summary>
/// Maxio 422 bodies use either <c>{"errors":["msg"]}</c> or <c>{"errors":{"field":["msg"]}}</c>.
/// </summary>
internal sealed class MaxioErrorListConverter : JsonConverter<List<string>?>
{
    public override List<string>? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
            return list;
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

                var field = reader.GetString();
                reader.Read();
                if (reader.TokenType == JsonTokenType.StartArray)
                {
                    while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    {
                        if (reader.TokenType == JsonTokenType.String)
                        {
                            list.Add($"{field}: {reader.GetString()}");
                        }
                    }
                }
                else if (reader.TokenType == JsonTokenType.String)
                {
                    list.Add($"{field}: {reader.GetString()}");
                }
                else
                {
                    reader.Skip();
                }
            }
            return list;
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
