using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

[JsonConverter(typeof(Amount3Converter))]
public record Amount3
{
    private readonly Optional<double> _doubleValue;

    private readonly Optional<string> _stringValue;

    private Amount3(Optional<double> doubleValue, Optional<string> stringValue)
    {
        _doubleValue = doubleValue;
        _stringValue = stringValue;
    }

    public static Amount3 Double(double value) => new(Optional<double>.Some(value), default);

    public static Amount3 String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator Amount3(double value) => Double(value);

    public static implicit operator Amount3(string value) => String(value);
}

file sealed class Amount3Converter : JsonConverter<Amount3>
{
    public override Amount3 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return Amount3.Double(doubleValue);
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return Amount3.String(value);
        }
        throw new JsonException($"JSON does not match double or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Amount3 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(Amount3)} contains no valid value to serialize.");
        }
    }
}
