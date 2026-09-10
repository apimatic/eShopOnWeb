using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using MaxioAdvancedBilling.Core.Extensions;
using MaxioAdvancedBilling.Core.Models;

namespace MaxioAdvancedBilling.Models.AnyOf;

[JsonConverter(typeof(PricePointConverter))]
public record PricePoint
{
    private readonly Optional<CreateComponentPricePoint> _createComponentPricePointValue;

    private readonly Optional<CreatePrepaidUsageComponentPricePoint> _createPrepaidUsageComponentPricePointValue;

    private PricePoint(Optional<CreateComponentPricePoint> createComponentPricePointValue,
        Optional<CreatePrepaidUsageComponentPricePoint> createPrepaidUsageComponentPricePointValue)
    {
        _createComponentPricePointValue = createComponentPricePointValue;
        _createPrepaidUsageComponentPricePointValue = createPrepaidUsageComponentPricePointValue;
    }

    public static PricePoint CreateComponentPricePoint(CreateComponentPricePoint value) =>
        new(Optional<CreateComponentPricePoint>.Some(value), default);

    public static PricePoint CreatePrepaidUsageComponentPricePoint(CreatePrepaidUsageComponentPricePoint value) =>
        new(default, Optional<CreatePrepaidUsageComponentPricePoint>.Some(value));

    public bool TryGetCreateComponentPricePoint(out CreateComponentPricePoint value) =>
        _createComponentPricePointValue.TryGetValue(out value);

    public bool TryGetCreatePrepaidUsageComponentPricePoint(out CreatePrepaidUsageComponentPricePoint value) =>
        _createPrepaidUsageComponentPricePointValue.TryGetValue(out value);

    public static implicit operator PricePoint(CreateComponentPricePoint value) =>
        CreateComponentPricePoint(value);

    public static implicit operator PricePoint(CreatePrepaidUsageComponentPricePoint value) =>
        CreatePrepaidUsageComponentPricePoint(value);
}

file sealed class PricePointConverter : JsonConverter<PricePoint>
{
    public override PricePoint Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (JsonSerializer.TryDeserialize<CreateComponentPricePoint>(root,
            options,
            out var createComponentPricePointValue))
        {
            return PricePoint.CreateComponentPricePoint(createComponentPricePointValue);
        }
        if (JsonSerializer.TryDeserialize<CreatePrepaidUsageComponentPricePoint>(root,
            options,
            out var createPrepaidUsageComponentPricePointValue))
        {
            return PricePoint.CreatePrepaidUsageComponentPricePoint(createPrepaidUsageComponentPricePointValue);
        }
        throw new JsonException($"JSON does not match CreateComponentPricePoint or CreatePrepaidUsageComponentPricePoint schemas: {root.ToString()}");
    }

    public override void Write(Utf8JsonWriter writer, PricePoint value, JsonSerializerOptions options)
    {
        if (value.TryGetCreateComponentPricePoint(out var createComponentPricePointValue))
        {
            JsonSerializer.Serialize(writer, createComponentPricePointValue, options);
        }
        else if (value.TryGetCreatePrepaidUsageComponentPricePoint(out var createPrepaidUsageComponentPricePointValue))
        {
            JsonSerializer.Serialize(writer, createPrepaidUsageComponentPricePointValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(PricePoint)} contains no valid value to serialize.");
        }
    }
}
