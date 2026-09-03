using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// The price can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065
/// </summary>
[JsonConverter(typeof(UnitPriceConverter))]
public record UnitPrice
{
    private readonly Optional<double> _doubleValue;

    private readonly Optional<string> _stringValue;

    private UnitPrice(Optional<double> doubleValue, Optional<string> stringValue)
    {
        _doubleValue = doubleValue;
        _stringValue = stringValue;
    }

    public static UnitPrice Double(double value) => new(Optional<double>.Some(value), default);

    public static UnitPrice String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator UnitPrice(double value) => Double(value);

    public static implicit operator UnitPrice(string value) => String(value);
}

file sealed class UnitPriceConverter : JsonConverter<UnitPrice>
{
    public override UnitPrice Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return UnitPrice.Double(doubleValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return UnitPrice.String(value);
        }
        throw new JsonException($"JSON does not match double or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, UnitPrice value, JsonSerializerOptions options)
    {
        if (value.TryGetDouble(out var doubleValue))
        {
            JsonSerializer.Serialize(writer, doubleValue, options);
        }
        else if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(UnitPrice)} contains no valid value to serialize.");
        }
    }
}
