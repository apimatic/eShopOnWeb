using System.Text.Json.Serialization;
using Twilio.Core.Models;

namespace Twilio.Models;

public record Dimension
{
    /// <summary>
    /// Identifier used to reference this dimension in queries
    /// </summary>
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    /// <summary>
    /// Detailed explanation of what this dimension represents
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("description")]
    public string? Description { get; init; }

    /// <summary>
    /// Data type of the dimension (e.g., string, number, boolean, date)
    /// </summary>
    [JsonPropertyName("type")]
    public required string Type { get; init; }

    [JsonExtensionData]
    public AdditionalProperties AdditionalProperties { get; init; } = [];
}
