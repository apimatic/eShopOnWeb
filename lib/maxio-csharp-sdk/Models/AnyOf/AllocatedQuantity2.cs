using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// For Quantity-based components: The current allocation for the component on the given subscription. For On/Off components: Use 1 for on. Use 0 for off.
/// </summary>
[JsonConverter(typeof(AllocatedQuantity2Converter))]
public record AllocatedQuantity2
{
    private readonly Optional<int> _intValue;

    private readonly Optional<string> _stringValue;

    private AllocatedQuantity2(Optional<int> intValue, Optional<string> stringValue)
    {
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public static AllocatedQuantity2 Int(int value) => new(Optional<int>.Some(value), default);

    public static AllocatedQuantity2 String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator AllocatedQuantity2(int value) => Int(value);

    public static implicit operator AllocatedQuantity2(string value) => String(value);
}

file sealed class AllocatedQuantity2Converter : JsonConverter<AllocatedQuantity2>
{
    public override AllocatedQuantity2 Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return AllocatedQuantity2.Int(intValue);
            }
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return AllocatedQuantity2.String(value);
        }
        throw new JsonException($"JSON does not match int or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, AllocatedQuantity2 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(AllocatedQuantity2)} contains no valid value to serialize.");
        }
    }
}
