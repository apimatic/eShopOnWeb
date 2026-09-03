using System.Text.Json.Serialization;
using Maxio.Core.Models;

namespace Maxio.Models;

public record CustomFieldValueChange
{
    [JsonPropertyName("event_type")]
    public required string EventType { get; init; }

    [JsonPropertyName("metafield_name")]
    public required string MetafieldName { get; init; }

    [JsonPropertyName("metafield_id")]
    public required int MetafieldId { get; init; }

    [JsonPropertyName("old_value")]
    public required string? OldValue { get; init; }

    [JsonPropertyName("new_value")]
    public required string? NewValue { get; init; }

    [JsonPropertyName("resource_type")]
    public required string ResourceType { get; init; }

    [JsonPropertyName("resource_id")]
    public required int ResourceId { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
