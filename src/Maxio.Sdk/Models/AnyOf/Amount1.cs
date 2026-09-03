using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Extensions;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// Dollar amount of the sum of the invoices payment (eg. "10.50" =&gt; $10.50).
/// </summary>
[JsonConverter(typeof(Amount1Converter))]
public record Amount1
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<double> _doubleValue;

    private Amount1(Optional<string> stringValue, Optional<double> doubleValue)
    {
        _stringValue = stringValue;
        _doubleValue = doubleValue;
    }

    public static Amount1 String(string value) => new(Optional<string>.Some(value), default);

    public static Amount1 Double(double value) => new(default, Optional<double>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetDouble(out double value) => _doubleValue.TryGetValue(out value);

    public static implicit operator Amount1(string value) => String(value);

    public static implicit operator Amount1(double value) => Double(value);
}

file sealed class Amount1Converter : JsonConverter<Amount1>
{
    public override Amount1 Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return Amount1.String(value);
        }
        if (JsonSerializer.TryDeserialize<double>(root, options, out var doubleValue))
        {
            return Amount1.Double(doubleValue);
        }
        throw new JsonException($"JSON does not match string or double schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, Amount1 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(Amount1)} contains no valid value to serialize.");
        }
    }
}
