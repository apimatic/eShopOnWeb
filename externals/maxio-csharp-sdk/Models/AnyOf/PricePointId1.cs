using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// Price point that the allocation should be charged at. Accepts either the price point's id (integer) or handle (string). When not specified, the default price point will be used.
/// </summary>
[JsonConverter(typeof(PricePointId1Converter))]
public record PricePointId1
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<int> _intValue;

    private PricePointId1(Optional<string> stringValue, Optional<int> intValue)
    {
        _stringValue = stringValue;
        _intValue = intValue;
    }

    public static PricePointId1 String(string value) => new(Optional<string>.Some(value), default);

    public static PricePointId1 Int(int value) => new(default, Optional<int>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public static implicit operator PricePointId1(string value) => String(value);

    public static implicit operator PricePointId1(int value) => Int(value);
}

file sealed class PricePointId1Converter : JsonConverter<PricePointId1>
{
    public override PricePointId1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return PricePointId1.String(value);
        }
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return PricePointId1.Int(intValue);
            }
        }
        throw new JsonException($"JSON does not match string or int schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, PricePointId1 value, JsonSerializerOptions options)
    {
        if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else if (value.TryGetInt(out var intValue))
        {
            JsonSerializer.Serialize(writer, intValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(PricePointId1)} contains no valid value to serialize.");
        }
    }
}
