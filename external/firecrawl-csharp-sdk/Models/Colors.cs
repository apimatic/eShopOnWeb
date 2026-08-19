using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// Brand colors extracted from the page.
/// </summary>
public record Colors
{
    /// <summary>
    /// Primary brand color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("primary")]
    public string? Primary { get; init; }

    /// <summary>
    /// Secondary brand color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("secondary")]
    public string? Secondary { get; init; }

    /// <summary>
    /// Accent color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("accent")]
    public string? Accent { get; init; }

    /// <summary>
    /// Background color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("background")]
    public string? Background { get; init; }

    /// <summary>
    /// Primary text color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("textPrimary")]
    public string? TextPrimary { get; init; }

    /// <summary>
    /// Secondary text color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("textSecondary")]
    public string? TextSecondary { get; init; }

    /// <summary>
    /// Link color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("link")]
    public string? Link { get; init; }

    /// <summary>
    /// Success/positive color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("success")]
    public string? Success { get; init; }

    /// <summary>
    /// Warning color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("warning")]
    public string? Warning { get; init; }

    /// <summary>
    /// Error/danger color (hex).
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("error")]
    public string? Error { get; init; }
}
