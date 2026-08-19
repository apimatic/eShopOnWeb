using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Item
{
    /// <summary>
    /// The item identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The item name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The item description.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Item images.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("images")]
    public IReadOnlyList<Images4>? Images { get; init; }

    /// <summary>
    /// The price of the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("price")]
    public Price1? Price { get; init; }

    /// <summary>
    /// The availability of the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("availability")]
    public Availability1? Availability { get; init; }

    /// <summary>
    /// Dietary tags for the item (e.g. ['vegetarian']).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("dietary")]
    public IReadOnlyList<string>? Dietary { get; init; }

    /// <summary>
    /// The item's calorie count.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("calories")]
    public double? Calories { get; init; }

    /// <summary>
    /// Option/modifier groups for the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("optionGroups")]
    public IReadOnlyList<object>? OptionGroups { get; init; }

    /// <summary>
    /// Merchant-specific identifiers for the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("identifiers")]
    public Identifiers? Identifiers { get; init; }

    /// <summary>
    /// The canonical URL of the item.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("url")]
    public string? Url { get; init; }

    /// <summary>
    /// The URL the item was extracted from.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("sourceUrl")]
    public string? SourceUrl { get; init; }
}
