using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Maxio;

internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseLower));
        return options;
    }

    public static T Deserialize<T>(string json)
    {
        var value = JsonSerializer.Deserialize<T>(json, Options);
        if (value is null)
        {
            throw new InvalidOperationException($"Maxio returned empty JSON for {typeof(T).Name}.");
        }

        return value;
    }

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);
}

internal sealed class MaxioErrorPayload
{
    [JsonConverter(typeof(MaxioErrorsConverter))]
    public List<string>? Errors { get; set; }
}

/// <summary>
/// Maxio returns <c>errors</c> as either a string array or an object of field messages.
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

                var name = reader.GetString();
                reader.Read();
                var value = reader.TokenType == JsonTokenType.String
                    ? reader.GetString()
                    : reader.TokenType.ToString();
                list.Add(string.IsNullOrWhiteSpace(name) ? value ?? string.Empty : $"{name}: {value}");
                if (reader.TokenType is JsonTokenType.StartObject or JsonTokenType.StartArray)
                {
                    reader.Skip();
                }
            }

            return list;
        }

        if (reader.TokenType == JsonTokenType.String)
        {
            var value = reader.GetString();
            return string.IsNullOrWhiteSpace(value) ? new List<string>() : new List<string> { value };
        }

        reader.Skip();
        return new List<string>();
    }

    public override void Write(Utf8JsonWriter writer, List<string>? value, JsonSerializerOptions options)
    {
        JsonSerializer.Serialize(writer, value, options);
    }
}
