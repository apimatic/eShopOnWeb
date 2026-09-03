using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// (Optional)
/// </summary>
[JsonConverter(typeof(InitialChargeInCentsConverter))]
public record InitialChargeInCents
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<long> _longValue;

    private InitialChargeInCents(Optional<string> stringValue, Optional<long> longValue)
    {
        _stringValue = stringValue;
        _longValue = longValue;
    }

    public static InitialChargeInCents String(string value) => new(Optional<string>.Some(value), default);

    public static InitialChargeInCents Long(long value) => new(default, Optional<long>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetLong(out long value) => _longValue.TryGetValue(out value);

    public static implicit operator InitialChargeInCents(string value) => String(value);

    public static implicit operator InitialChargeInCents(long value) => Long(value);
}

file sealed class InitialChargeInCentsConverter : JsonConverter<InitialChargeInCents>
{
    public override InitialChargeInCents Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return InitialChargeInCents.String(value);
        }
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt64(out var longValue))
            {
                return InitialChargeInCents.Long(longValue);
            }
        }
        throw new JsonException($"JSON does not match string or long schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, InitialChargeInCents value, JsonSerializerOptions options)
    {
        if (value.TryGetString(out var stringValue))
        {
            JsonSerializer.Serialize(writer, stringValue, options);
        }
        else if (value.TryGetLong(out var longValue))
        {
            JsonSerializer.Serialize(writer, longValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(InitialChargeInCents)} contains no valid value to serialize.");
        }
    }
}
