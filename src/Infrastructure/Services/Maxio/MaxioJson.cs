using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services.Maxio;

/// <summary>
/// Serializer configuration for the Maxio Advanced Billing wire format (snake_case JSON).
/// </summary>
internal static class MaxioJson
{
    public static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            NumberHandling = JsonNumberHandling.AllowReadingFromString
        };
        options.Converters.Add(new FlexibleDecimalConverter());
        options.Converters.Add(new FlexibleNullableDecimalConverter());

        return options;
    }

    /// <summary>
    /// Maxio reports some quantities and prices as JSON numbers and others as strings
    /// (e.g. <c>unit_price: "0.01"</c>, <c>quantity: "20.0"</c>); both must read as decimals.
    /// </summary>
    private class FlexibleDecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDecimal(ref reader) ?? decimal.Zero;

        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) =>
            writer.WriteNumberValue(value);
    }

    private class FlexibleNullableDecimalConverter : JsonConverter<decimal?>
    {
        public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            ReadDecimal(ref reader);

        public override void Write(Utf8JsonWriter writer, decimal? value, JsonSerializerOptions options)
        {
            if (value.HasValue)
            {
                writer.WriteNumberValue(value.Value);
            }
            else
            {
                writer.WriteNullValue();
            }
        }
    }

    private static decimal? ReadDecimal(ref Utf8JsonReader reader)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var text = reader.GetString();
                return decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a decimal value.");
        }
    }
}
