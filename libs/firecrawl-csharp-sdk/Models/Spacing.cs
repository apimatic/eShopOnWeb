using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Spacing and layout information.
/// </summary>
public record Spacing
{
    /// <summary>
    /// Base spacing unit in pixels.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("baseUnit")]
    public int? BaseUnit { get; init; }

    /// <summary>
    /// Default border radius.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("borderRadius")]
    public string? BorderRadius { get; init; }

    /// <summary>
    /// Padding values.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("padding")]
    public object? Padding { get; init; }

    /// <summary>
    /// Margin values.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("margins")]
    public object? Margins { get; init; }
}
