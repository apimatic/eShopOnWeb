using System.Text.Json.Serialization;

namespace FirecrawlApi.Models;

public record SupportAskRequest
{
    /// <summary>
    /// Question or issue for the support agent to diagnose.
    /// </summary>
    [JsonPropertyName("question")]
    public required string Question { get; init; }

    /// <summary>
    /// Optional context about what the end user is trying to accomplish.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [JsonPropertyName("rationale")]
    public string? Rationale { get; init; }
}
