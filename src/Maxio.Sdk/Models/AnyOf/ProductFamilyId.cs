using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.AnyOf;

[JsonConverter(typeof(ProductFamilyIdConverter))]
public record ProductFamilyId
{
    private readonly Optional<string> _stringValue;

    private readonly Optional<int> _intValue;

    private ProductFamilyId(Optional<string> stringValue, Optional<int> intValue)
    {
        _stringValue = stringValue;
        _intValue = intValue;
    }

    public static ProductFamilyId String(string value) => new(Optional<string>.Some(value), default);

    public static ProductFamilyId Int(int value) => new(default, Optional<int>.Some(value));

    public bool TryGetString(out string value) => _stringValue.TryGetValue(out value);

    public bool TryGetInt(out int value) => _intValue.TryGetValue(out value);

    public static implicit operator ProductFamilyId(string value) => String(value);

    public static implicit operator ProductFamilyId(int value) => Int(value);
}

file sealed class ProductFamilyIdConverter : JsonConverter<ProductFamilyId>
{
    public override ProductFamilyId Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (root.ValueKind == JsonValueKind.String)
        {
            var value = root.GetString()!;
            return ProductFamilyId.String(value);
        }
        if (root.ValueKind == JsonValueKind.Number)
        {
            if (root.TryGetInt32(out var intValue))
            {
                return ProductFamilyId.Int(intValue);
            }
        }
        throw new JsonException($"JSON does not match string or int schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, ProductFamilyId value, JsonSerializerOptions options)
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
            throw new JsonException($"{nameof(ProductFamilyId)} contains no valid value to serialize.");
        }
    }
}
