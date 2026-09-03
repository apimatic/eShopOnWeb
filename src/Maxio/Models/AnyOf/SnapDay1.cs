using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// A day of month that subscription will be processed on. Can be 1 up to 28 or 'end'.
/// </summary>
[JsonConverter(typeof(SnapDay1Converter))]
public record SnapDay1
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<int> _intValue;

    private SnapDay1(Optional<string> stringValue, Optional<int> intValue)
    {
        _stringValue = stringValue;
        _intValue = intValue;
    }

    public static SnapDay1 String(string value) => new(Optional<string>.Some(value), default);

    public static SnapDay1 Int(int value) => new(default, Optional<int>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public static implicit operator SnapDay1(string value) => String(value);

    public static implicit operator SnapDay1(int value) => Int(value);
}

file sealed class SnapDay1Converter : JsonConverter<SnapDay1>
{
    public override SnapDay1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return SnapDay1.String(value);
        }
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return SnapDay1.Int(intValue);
            }
        }
        throw new JsonException($"JSON does not match string or int schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, SnapDay1 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(SnapDay1)} contains no valid value to serialize.");
        }
    }
}
