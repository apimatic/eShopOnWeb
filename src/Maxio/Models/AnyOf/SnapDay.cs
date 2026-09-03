using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// A day of month that subscription will be processed on. Can be 1 up to 28 or 'end'.
/// </summary>
[JsonConverter(typeof(SnapDayConverter))]
public record SnapDay
{
    private readonly Optional<int> _intValue;

    private readonly Optional<string> _stringValue;

    private SnapDay(Optional<int> intValue, Optional<string> stringValue)
    {
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public static SnapDay Int(int value) => new(Optional<int>.Some(value), default);

    public static SnapDay String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator SnapDay(int value) => Int(value);

    public static implicit operator SnapDay(string value) => String(value);
}

file sealed class SnapDayConverter : JsonConverter<SnapDay>
{
    public override SnapDay Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return SnapDay.Int(intValue);
            }
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return SnapDay.String(value);
        }
        throw new JsonException($"JSON does not match int or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, SnapDay value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(SnapDay)} contains no valid value to serialize.");
        }
    }
}
