using System;
using System.Text.Json;
using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models.OneOf;

[JsonConverter(typeof(RenewalConfigurationItemConverter))]
public record RenewalConfigurationItem
{
    private readonly Optional<ScheduledRenewalItemRequestBodyComponent> _scheduledRenewalItemRequestBodyComponentValue;

    private readonly Optional<ScheduledRenewalItemRequestBodyProduct> _scheduledRenewalItemRequestBodyProductValue;

    private RenewalConfigurationItem(Optional<ScheduledRenewalItemRequestBodyComponent> scheduledRenewalItemRequestBodyComponentValue,
        Optional<ScheduledRenewalItemRequestBodyProduct> scheduledRenewalItemRequestBodyProductValue)
    {
        _scheduledRenewalItemRequestBodyComponentValue = scheduledRenewalItemRequestBodyComponentValue;
        _scheduledRenewalItemRequestBodyProductValue = scheduledRenewalItemRequestBodyProductValue;
    }

    public static RenewalConfigurationItem ScheduledRenewalItemRequestBodyComponent(ScheduledRenewalItemRequestBodyComponent value) =>
        new(Optional<ScheduledRenewalItemRequestBodyComponent>.Some(value), default);

    public static RenewalConfigurationItem ScheduledRenewalItemRequestBodyProduct(ScheduledRenewalItemRequestBodyProduct value) =>
        new(default, Optional<ScheduledRenewalItemRequestBodyProduct>.Some(value));

    public bool TryGetScheduledRenewalItemRequestBodyComponent(out ScheduledRenewalItemRequestBodyComponent value) =>
        _scheduledRenewalItemRequestBodyComponentValue.TryGetValue(out value);

    public bool TryGetScheduledRenewalItemRequestBodyProduct(out ScheduledRenewalItemRequestBodyProduct value) =>
        _scheduledRenewalItemRequestBodyProductValue.TryGetValue(out value);

    public static implicit operator RenewalConfigurationItem(ScheduledRenewalItemRequestBodyComponent value) =>
        ScheduledRenewalItemRequestBodyComponent(value);

    public static implicit operator RenewalConfigurationItem(ScheduledRenewalItemRequestBodyProduct value) =>
        ScheduledRenewalItemRequestBodyProduct(value);
}

file sealed class RenewalConfigurationItemConverter : JsonConverter<RenewalConfigurationItem>
{
    public override RenewalConfigurationItem Read(ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        using var doc = JsonDocument.ParseValue(ref reader);
        var root = doc.RootElement;
        if (!root.TryGetProperty("item_type", out var typeProperty))
        {
            throw new JsonException("Missing required 'item_type' discriminator field");
        }
        var discriminator = typeProperty.GetString();
        return discriminator switch
        {
            "Component" => RenewalConfigurationItem.ScheduledRenewalItemRequestBodyComponent(root.Deserialize<ScheduledRenewalItemRequestBodyComponent>(options)!),
            "Product" => RenewalConfigurationItem.ScheduledRenewalItemRequestBodyProduct(root.Deserialize<ScheduledRenewalItemRequestBodyProduct>(options)!),
            _ => throw new JsonException($"JSON does not match ScheduledRenewalItemRequestBodyComponent or ScheduledRenewalItemRequestBodyProduct schemas: {root.ToString()}")
        };
    }

    public override void Write(Utf8JsonWriter writer, RenewalConfigurationItem value, JsonSerializerOptions options)
    {
        if (value.TryGetScheduledRenewalItemRequestBodyComponent(out var scheduledRenewalItemRequestBodyComponentValue))
        {
            JsonSerializer.Serialize(writer, scheduledRenewalItemRequestBodyComponentValue, options);
        }
        else if (value.TryGetScheduledRenewalItemRequestBodyProduct(out var scheduledRenewalItemRequestBodyProductValue))
        {
            JsonSerializer.Serialize(writer, scheduledRenewalItemRequestBodyProductValue, options);
        }
        else
        {
            throw new JsonException($"{nameof(RenewalConfigurationItem)} contains no valid value to serialize.");
        }
    }
}
