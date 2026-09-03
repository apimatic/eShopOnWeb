using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// (Optional when performing an Import via vault_token, required otherwise) The 4-digit credit card expiration year, as an integer or string, e.g., 2012
/// </summary>
[JsonConverter(typeof(ExpirationYear1Converter))]
public record ExpirationYear1
{
    private readonly Optional<int> _intValue;

    private readonly Optional<string> _stringValue;

    private ExpirationYear1(Optional<int> intValue, Optional<string> stringValue)
    {
        _intValue = intValue;
        _stringValue = stringValue;
    }

    public static ExpirationYear1 Int(int value) => new(Optional<int>.Some(value), default);

    public static ExpirationYear1 String(string value) => new(default, Optional<string>.Some(value));

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public static implicit operator ExpirationYear1(int value) => Int(value);

    public static implicit operator ExpirationYear1(string value) => String(value);
}

file sealed class ExpirationYear1Converter : JsonConverter<ExpirationYear1>
{
    public override ExpirationYear1 Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return ExpirationYear1.Int(intValue);
            }
        }
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return ExpirationYear1.String(value);
        }
        throw new JsonException($"JSON does not match int or string schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, ExpirationYear1 value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(ExpirationYear1)} contains no valid value to serialize.");
        }
    }
}
