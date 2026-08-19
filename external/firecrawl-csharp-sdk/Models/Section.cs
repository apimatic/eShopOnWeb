using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record Section
{
    /// <summary>
    /// The section identifier.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("id")]
    public string? Id { get; init; }

    /// <summary>
    /// The section name.
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// The section description.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// The items in the section.
    /// </summary>
    [JsonPropertyName("items")]
    public required IReadOnlyList<Item> Items { get; init; }
}
