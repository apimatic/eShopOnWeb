using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Billing;

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Maxio returns <c>errors</c> as either a string array or a keyed object.
/// </summary>
internal sealed class MaxioErrorsConverter : JsonConverter<List<string>?>
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
                    list.Add(reader.GetString() ?? string.Empty);
                }
                else
                {
                    list.Add(JsonDocument.ParseValue(ref reader).RootElement.GetRawText());
                }
            }
            return list;
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var list = new List<string>();
            using var doc = JsonDocument.ParseValue(ref reader);
            foreach (var property in doc.RootElement.EnumerateObject())
            {
                list.Add($"{property.Name}: {property.Value.ToString()}");
            }
            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            return new List<string> { reader.GetString() ?? string.Empty };
        }

        reader.Skip();
        return null;
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
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
