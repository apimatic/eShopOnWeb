using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Twilio.Models;

/// <summary>
/// twilio/catalog type lets recipients view list of catalog products, ask questions about products, order products.
/// </summary>
public record TwilioCatalog
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("title")]
    public string? Title { get; init; }

    [JsonPropertyName("body")]
    public required string Body { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("subtitle")]
    public string? Subtitle { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("items")]
    public IReadOnlyList<CatalogItem>? Items { get; init; }

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dynamic_items")]
    public string? DynamicItems { get; init; }
}
