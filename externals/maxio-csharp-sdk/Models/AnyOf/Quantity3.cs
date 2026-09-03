using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// The quantity can contain up to 8 decimal places. e.g., 1.00 or 0.0012 or 0.00000065. If you submit a value with more than 8 decimal places, we will round it down to the 8th decimal place.
/// </summary>
[JsonConverter(typeof(Quantity3Converter))]
public record Quantity3
{
    private readonly Optional<double> _doubleValue;

    private readonly Optional<string> _stringValue;

    private Quantity3(Optional<double> doubleValue, Optional<string> stringValue)
    {
        _doubleValue = doubleValue;
        _stringValue = stringValue;
    }

    public static Quantity3 Double(double value) => new(Optional<double>.Some(value), default);

    public static Quantity3 String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator Quantity3(double value) => Double(value);

    public static implicit operator Quantity3(string value) => String(value);
}

file sealed class Quantity3Converter : JsonConverter<Quantity3>
{
    public override Quantity3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return Quantity3.Double(doubleValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return Quantity3.String(value);
        }
        throw new JsonException($"JSON does not match double or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Quantity3 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(Quantity3)} contains no valid value to serialize.");
        }
    }
}
