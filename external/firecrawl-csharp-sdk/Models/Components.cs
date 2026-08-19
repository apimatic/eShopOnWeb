using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

/// <summary>
/// UI component styles.
/// </summary>
public record Components
{
    /// <summary>
    /// Primary button styles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("buttonPrimary")]
    public ButtonPrimary? ButtonPrimary { get; init; }

    /// <summary>
    /// Secondary button styles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("buttonSecondary")]
    public ButtonSecondary? ButtonSecondary { get; init; }

    /// <summary>
    /// Input field styles.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("input")]
    public object? Input { get; init; }
}
