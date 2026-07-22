using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>Serialisation settings shared by every Maxio request and response.</summary>
internal static class MaxioJson
{
    /// <summary>
    /// Maxio uses snake_case throughout. Nulls are dropped on the way out so optional request
    /// fields are simply absent rather than explicitly null.
    /// </summary>
    public static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };
}

/// <summary>
/// Reads a decimal that the API may send either as a JSON number or as a JSON string —
/// <c>Usage.quantity</c> is typed <c>integer | string</c> in the specification.
/// </summary>
internal sealed class FlexibleDecimalConverter : JsonConverter<decimal>
{
    public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleDecimal.Read(ref reader) ?? 0m;

    public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
        writer.WriteNumberValue(value);
}

/// <summary>
/// Reads an optional decimal sent as a number, a string, or null — <c>Component.unit_price</c> is
/// typed <c>string | null</c> in the specification.
/// </summary>
internal sealed class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        FlexibleDecimal.Read(ref reader);

    public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
    {
        if (value is null)
        {
            writer.WriteNullValue();
        }
        else
        {
            writer.WriteNumberValue(value.Value);
        }
    }
}

internal static class FlexibleDecimal
{
    public static decimal? Read(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return null;
                }

                // Maxio always formats money with an invariant decimal point (e.g. "0.01").
                return decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : throw new JsonException($"Could not read '{text}' as a decimal.");
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a decimal.");
        }
    }
}
