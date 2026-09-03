using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// The amount the customer will be charged per unit when the pricing scheme is “per_unit”. The price can contain up to 8 decimal places. i.e., 1.00 or 0.0012 or 0.00000065
/// </summary>
[JsonConverter(typeof(UnitPrice5Converter))]
public record UnitPrice5
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<double> _doubleValue;

    private UnitPrice5(Optional<string> stringValue, Optional<double> doubleValue)
    {
        _stringValue = stringValue;
        _doubleValue = doubleValue;
    }

    public static UnitPrice5 String(string value) => new(Optional<string>.Some(value), default);

    public static UnitPrice5 Double(double value) => new(default, Optional<double>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public static implicit operator UnitPrice5(string value) => String(value);

    public static implicit operator UnitPrice5(double value) => Double(value);
}

file sealed class UnitPrice5Converter : JsonConverter<UnitPrice5>
{
    public override UnitPrice5 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return UnitPrice5.String(value);
        }
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return UnitPrice5.Double(doubleValue);
        }
        throw new JsonException($"JSON does not match string or double schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, UnitPrice5 value, JsonSerializerOptions options)
    {
        if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else if (value.TryGetDouble(out var doubleValue))
        {
            JsonSerializer.Serialize(writer, doubleValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(UnitPrice5)} contains no valid value to serialize.");
        }
    }
}
