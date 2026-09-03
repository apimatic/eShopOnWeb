using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// The unit_price can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065. If you submit a value with more than 8 decimal places, we will round it down to the 8th decimal place.
/// </summary>
[JsonConverter(typeof(UnitPrice7Converter))]
public record UnitPrice7
{
    private readonly Optional<double> _doubleValue;

    private readonly Optional<string> _stringValue;

    private UnitPrice7(Optional<double> doubleValue, Optional<string> stringValue)
    {
        _doubleValue = doubleValue;
        _stringValue = stringValue;
    }

    public static UnitPrice7 Double(double value) => new(Optional<double>.Some(value), default);

    public static UnitPrice7 String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator UnitPrice7(double value) => Double(value);

    public static implicit operator UnitPrice7(string value) => String(value);
}

file sealed class UnitPrice7Converter : JsonConverter<UnitPrice7>
{
    public override UnitPrice7 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return UnitPrice7.Double(doubleValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return UnitPrice7.String(value);
        }
        throw new JsonException($"JSON does not match double or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, UnitPrice7 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(UnitPrice7)} contains no valid value to serialize.");
        }
    }
}
