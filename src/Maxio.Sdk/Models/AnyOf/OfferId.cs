using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

/// <summary>
/// Use in place of passing product and component information to set up the subscription with an existing offer. May be either the Chargify id of the offer or its handle prefixed with <c>handle:</c>.
/// </summary>
[JsonConverter(typeof(OfferIdConverter))]
public record OfferId
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<int> _intValue;

    private OfferId(Optional<string> stringValue, Optional<int> intValue)
    {
        _stringValue = stringValue;
        _intValue = intValue;
    }

    public static OfferId String(string value) => new(Optional<string>.Some(value), default);

    public static OfferId Int(int value) => new(default, Optional<int>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public static implicit operator OfferId(string value) => String(value);

    public static implicit operator OfferId(int value) => Int(value);
}

file sealed class OfferIdConverter : JsonConverter<OfferId>
{
    public override OfferId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return OfferId.String(value);
        }
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return OfferId.Int(intValue);
            }
        }
        throw new JsonException($"JSON does not match string or int schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, OfferId value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(OfferId)} contains no valid value to serialize.");
        }
    }
}
