using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Detailed typography information.
/// </summary>
public record Typography
{
    /// <summary>
    /// Font families by role.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fontFamilies")]
    public FontFamilies? FontFamilies { get; init; }

    /// <summary>
    /// Font sizes for different text levels.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fontSizes")]
    public FontSizes? FontSizes { get; init; }

    /// <summary>
    /// Font weight definitions.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("fontWeights")]
    public FontWeights? FontWeights { get; init; }

    /// <summary>
    /// Line height values for different text types.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("lineHeights")]
    public LineHeights? LineHeights { get; init; }
}
