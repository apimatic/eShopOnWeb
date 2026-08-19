using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Font families by role.
/// </summary>
public record FontFamilies
{
    /// <summary>
    /// Primary font family.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("primary")]
    public string? Primary { get; init; }

    /// <summary>
    /// Heading font family.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("heading")]
    public string? Heading { get; init; }

    /// <summary>
    /// Code/monospace font family.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("code")]
    public string? Code { get; init; }
}
