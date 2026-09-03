using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// The allocated quantity set into effect by the allocation. String for components supporting fractional quantities
/// </summary>
[JsonConverter(typeof(QuantityConverter))]
public record Quantity
{
    private readonly Optional<int> _intValue;

    private readonly Optional<string> _stringValue;

    private Quantity(Optional<int> intValue, Optional<string> stringValue)
    {
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public static Quantity Int(int value) => new(Optional<int>.Some(value), default);

    public static Quantity String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator Quantity(int value) => Int(value);

    public static implicit operator Quantity(string value) => String(value);
}

file sealed class QuantityConverter : JsonConverter<Quantity>
{
    public override Quantity Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return Quantity.Int(intValue);
            }
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return Quantity.String(value);
        }
        throw new JsonException($"JSON does not match int or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Quantity value, JsonSerializerOptions options)
    {
        if (value.TryGetInt(out var intValue))
        {
            JsonSerializer.Serialize(writer, intValue, options);
        }
        else if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(Quantity)} contains no valid value to serialize.");
        }
    }
}
