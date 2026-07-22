using System;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Microsoft.eShopWeb.Infrastructure.Services;

/// <summary>
/// Reads a Maxio numeric field that the API may serialise either as a JSON number or as a decimal
/// string. The spec types several money and quantity fields this way — for example a component's
/// <c>unit_price</c> ("0.01") and a usage <c>quantity</c> (1 or "20.0").
/// </summary>
public class FlexibleDecimalConverter : JsonConverter<decimal?>
{
    public override decimal? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.Number:
                return reader.GetDecimal();
            case JsonTokenType.String:
                var raw = reader.GetString();
                return decimal.TryParse(raw, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed)
                    ? parsed
                    : null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a decimal value.");
        }
    }

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
